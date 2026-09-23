//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Diagnostics;
using EjoyFramework.Core.Telemetry;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// WS4-M2：崩溃采集——指纹归一化、去重与上限、面包屑环、ANR（手动时钟 + 真实看门狗线程）、
    /// 上次会话异常退出分类、报告格式往返与损坏检测、上传（成功删除 / 失败重试上限 / 跨线程回报）、遥测记录。
    /// </summary>
    public sealed class CrashManagerTests
    {
        private sealed class Listener : ICrashReportListener
        {
            public readonly List<CrashReport> Reports = new List<CrashReport>();
            public void OnCrashReport(CrashReport report) { Reports.Add(report); }
        }

        private sealed class FakeUploader : ICrashUploader
        {
            public readonly List<CrashReport> Uploaded = new List<CrashReport>();
            public bool Fail;
            public bool Hold;
            public Action<CrashReport, bool> Pending;
            public CrashReport PendingReport;

            public void Upload(CrashReport report, byte[] payload, Action<CrashReport, bool> onComplete)
            {
                Uploaded.Add(report);
                if (Hold) { Pending = onComplete; PendingReport = report; return; }
                onComplete(report, !Fail);
            }
        }

        private string m_Dir;
        private double m_Now;
        private readonly List<CrashManager> m_Managers = new List<CrashManager>();

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Dir = Path.Combine(TestTempPaths.Root, "crash_" + Guid.NewGuid().ToString("N"));
            m_Now = 100.0;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_Managers.Count; i++) m_Managers[i].Shutdown();
            m_Managers.Clear();
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
        }

        private CrashManager NewManager(bool withStore = true)
        {
            CrashManager m = new CrashManager();
            m.ClockOverride = () => m_Now;
            m.WatchdogThreadEnabled = false;
            if (withStore) m.Configure(m_Dir);
            m_Managers.Add(m);
            return m;
        }

        private const string StackA = "Game.Enemy.Tick () (at Assets/Game/Enemy.cs:42)\nGame.World.Update () (at Assets/Game/World.cs:10)\n";
        private const string StackA2 = "Game.Enemy.Tick () (at Assets/Game/Enemy.cs:57)\nGame.World.Update () (at Assets/Game/World.cs:12)\n";
        private const string StackMono = "  at Game.Enemy.Tick () [0x0001a] in <9f3c>:0 \n  at Game.World.Update () [0x00000] in <9f3c>:0 \n";
        private const string StackB = "Game.Player.Jump () (at Assets/Game/Player.cs:5)\n";

        // ---------------- 指纹 ----------------

        [Test]
        public void Fingerprint_IgnoresLineNumbersAndStackFormat_ButSeparatesFrames()
        {
            uint a = CrashFingerprint.Compute("NullReferenceException: Object reference not set", StackA);
            Assert.AreEqual(a, CrashFingerprint.Compute("NullReferenceException: Object reference not set", StackA2), "行号变化不改变指纹。");
            Assert.AreEqual(a, CrashFingerprint.Compute("System.NullReferenceException: other text", StackMono), "Unity/Mono 两种栈格式、命名空间前缀、消息正文都不影响。");
            Assert.AreEqual(a, CrashFingerprint.Compute("UnhandledException | NullReferenceException: x", StackA), "诊断管道加的上下文前缀不影响。");
            Assert.AreNotEqual(a, CrashFingerprint.Compute("NullReferenceException: Object reference not set", StackB));
            Assert.AreNotEqual(a, CrashFingerprint.Compute("ArgumentException: Object reference not set", StackA));
        }

        [Test]
        public void Fingerprint_WithoutStack_IgnoresDigitsInMessage()
        {
            Assert.AreEqual(CrashFingerprint.Compute("Entity 1234 missing at (10,20)", null),
                            CrashFingerprint.Compute("Entity 99 missing at (3,4)", ""));
            Assert.AreNotEqual(CrashFingerprint.Compute("Entity 1 missing", null), CrashFingerprint.Compute("Entity 1 dead", null));
        }

        // ---------------- 去重 / 上限 / 面包屑 ----------------

        [Test]
        public void Exceptions_DedupByFingerprint_AndCapPerSession()
        {
            CrashManager m = NewManager(false);
            m.MaxReportsPerSession = 2;
            m.BeginSession("s1", "dev", "1.0");
            for (int i = 0; i < 100; i++) m.ReportException("NullReferenceException: boom " + i, StackA, false);
            m.ReportException("ArgumentException: a", StackB, false);
            m.ReportException("InvalidOperationException: c", "X.Y () (at a.cs:1)\n", false);   // 超过上限

            List<CrashReport> reports = new List<CrashReport>();
            m.GetSessionReports(reports);
            Assert.AreEqual(2, reports.Count);
            Assert.AreEqual(100, reports[0].Occurrences);
            Assert.AreEqual(CrashKind.Exception, reports[0].Kind);
            Assert.AreEqual(102, m.TotalExceptions);
            Assert.AreEqual(100, m.TotalSuppressed, "99 次重复 + 1 次超上限。");
        }

        [Test]
        public void FatalAfterSameNonFatal_UpgradesWithoutDoubleCounting()
        {
            CrashManager m = NewManager(false);
            m.BeginSession("s1", "dev", "1.0");
            m.ReportException("UnhandledException(terminating) | InvalidOperationException: boom", StackA, false);
            m.ReportException(new InvalidOperationException("boom").GetType().Name + ": boom", StackA, true);
            List<CrashReport> reports = new List<CrashReport>();
            m.GetSessionReports(reports);
            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual(CrashKind.Fatal, reports[0].Kind);
            Assert.AreEqual(1, reports[0].Occurrences);
        }

        [Test]
        public void Breadcrumbs_RingKeepsNewestInOrder_AndLogSinkFeedsIt()
        {
            CrashManager m = NewManager(false);
            m.BreadcrumbCapacity = 3;
            m.BeginSession("s1", "dev", "1.0");
            m.AddBreadcrumb("ui", "open bag");
            DiagEntry debug = new DiagEntry { Level = DiagLevel.Debug, Message = "too verbose" };
            m.OnLog(in debug);   // 低于 BreadcrumbMinLevel(Info) 丢弃
            DiagEntry warn = new DiagEntry { Level = DiagLevel.Warning, Message = "slow frame" };
            m.OnLog(in warn);
            m.AddBreadcrumb("net", "reconnect");
            m.AddBreadcrumb("scene", "enter town");
            DiagEntry ex = new DiagEntry { Level = DiagLevel.Exception, Message = "NullReferenceException: x", StackTrace = StackA };
            m.OnLog(in ex);      // Exception 级别直接生成报告

            List<CrashReport> reports = new List<CrashReport>();
            m.GetSessionReports(reports);
            Assert.AreEqual(1, reports.Count);
            string[] crumbs = reports[0].Breadcrumbs;
            Assert.AreEqual(3, crumbs.Length);
            StringAssert.Contains("slow frame", crumbs[0]);
            StringAssert.Contains("reconnect", crumbs[1]);
            StringAssert.Contains("enter town", crumbs[2]);
        }

        [Test]
        public void Report_CarriesPhaseUserKeysAndSession()
        {
            CrashManager m = NewManager(false);
            m.BeginSession("sess-9", "device-x", "3.1");
            m.SetPhase("Loading:Region_12");
            m.SetUserKey("uid", "42");
            m.SetUserKey("tmp", "1");
            m.SetUserKey("tmp", null);
            m.ReportException("IndexOutOfRangeException: i", StackB, false);
            List<CrashReport> reports = new List<CrashReport>();
            m.GetSessionReports(reports);
            CrashReport r = reports[0];
            Assert.AreEqual("sess-9", r.SessionId);
            Assert.AreEqual("device-x", r.DeviceInfo);
            Assert.AreEqual("3.1", r.AppVersion);
            Assert.AreEqual("Loading:Region_12", r.Phase);
            Assert.AreEqual(1, r.UserKeys.Length);
            Assert.AreEqual("uid", r.UserKeys[0].Key);
            Assert.AreEqual("42", r.UserKeys[0].Value);
        }

        // ---------------- 报告格式 ----------------

        [Test]
        public void ReportFormat_RoundTrips_AndRejectsCorruption()
        {
            CrashReport r = new CrashReport
            {
                ReportId = "0000000000001-abc-001", Kind = CrashKind.Hang, SessionId = "s", AppVersion = "1", DeviceInfo = "d",
                UnixMillis = 1234567, SessionSeconds = 12.5f, Message = "m", StackTrace = null, Fingerprint = 0xDEADBEEF,
                Phase = "p", HangSeconds = 7.25f, Occurrences = 3,
                Breadcrumbs = new[] { "a", "中文面包屑" },
                UserKeys = new[] { new KeyValuePair<string, string>("k", "v") },
            };
            byte[] bytes = r.ToBytes();
            CrashReport back;
            Assert.IsTrue(CrashReport.TryParse(bytes, out back));
            Assert.AreEqual(CrashKind.Hang, back.Kind);
            Assert.AreEqual(0xDEADBEEF, back.Fingerprint);
            Assert.AreEqual(string.Empty, back.StackTrace);
            Assert.AreEqual(7.25f, back.HangSeconds);
            Assert.AreEqual(3, back.Occurrences);
            Assert.AreEqual("中文面包屑", back.Breadcrumbs[1]);
            Assert.AreEqual("v", back.UserKeys[0].Value);

            byte[] truncated = new byte[bytes.Length - 3];
            Array.Copy(bytes, truncated, truncated.Length);
            Assert.IsFalse(CrashReport.TryParse(truncated, out back));
            byte[] trailing = new byte[bytes.Length + 1];
            Array.Copy(bytes, trailing, bytes.Length);
            Assert.IsFalse(CrashReport.TryParse(trailing, out back));
            byte[] badMagic = (byte[])bytes.Clone();
            badMagic[0] ^= 0xFF;
            Assert.IsFalse(CrashReport.TryParse(badMagic, out back));
            Assert.IsFalse(CrashReport.TryParse(null, out back));
        }

        // ---------------- ANR ----------------

        [Test]
        public void Hang_DetectedOnce_PersistedImmediately_ResolvedWithDuration()
        {
            CrashManager m = NewManager();
            Listener listener = new Listener();
            m.AddListener(listener);
            m.HangThresholdSeconds = 2f;
            m.BeginSession("s1", "dev", "1.0");
            m.SetPhase("Combat:Boss");
            m.Update(0f, 0f);                   // 心跳 @100

            m_Now = 101.5; m.PollWatchdog(m_Now);
            Assert.IsFalse(m.IsHanging);
            m_Now = 102.5; m.PollWatchdog(m_Now);
            Assert.IsTrue(m.IsHanging);
            m_Now = 104.0; m.PollWatchdog(m_Now);
            Assert.AreEqual(1, m.HangCount, "同一次卡死只报一次。");
            Assert.AreEqual(1, Directory.GetFiles(m_Dir, "*.ejcr").Length, "检测到即落盘（主线程还卡着）。");
            Assert.AreEqual(0, listener.Reports.Count, "监听器在主线程恢复后才回调。");

            m_Now = 107.0; m.Update(0f, 0f);    // 主线程恢复
            Assert.IsFalse(m.IsHanging);
            Assert.AreEqual(1, listener.Reports.Count);
            CrashReport hang = listener.Reports[0];
            Assert.AreEqual(CrashKind.Hang, hang.Kind);
            Assert.AreEqual(7f, hang.HangSeconds, 1e-4f, "时长 = 恢复时刻 - 最后心跳。");
            Assert.AreEqual("Combat:Boss", hang.Phase);

            CrashReport onDisk;
            Assert.IsTrue(CrashReport.TryParse(File.ReadAllBytes(Directory.GetFiles(m_Dir, "*.ejcr")[0]), out onDisk));
            Assert.AreEqual(7f, onDisk.HangSeconds, 1e-4f, "恢复后覆盖写入最终时长。");
        }

        [Test]
        public void Hang_NotDetected_WhenSuspendedOrBackgrounded()
        {
            CrashManager m = NewManager(false);
            m.HangThresholdSeconds = 1f;
            m.BeginSession("s1", "dev", "1.0");
            m.Update(0f, 0f);

            m.SuspendHangDetection();
            m_Now = 110; m.PollWatchdog(m_Now);
            Assert.IsFalse(m.IsHanging);
            m.ResumeHangDetection();            // 恢复时重置心跳
            m_Now = 110.5; m.PollWatchdog(m_Now);
            Assert.IsFalse(m.IsHanging);

            m.NotifyBackground(true);
            m_Now = 200; m.PollWatchdog(m_Now);
            Assert.IsFalse(m.IsHanging);
            m.NotifyBackground(false);
            m_Now = 200.5; m.PollWatchdog(m_Now);
            Assert.IsFalse(m.IsHanging, "回前台重置心跳，后台时长不算卡死。");

            Assert.Throws<FrameworkException>(() => m.ResumeHangDetection());
        }

        [Test]
        public void Hang_RealWatchdogThread_DetectsBlockedMainThread()
        {
            CrashManager m = new CrashManager();
            m_Managers.Add(m);
            m.HangThresholdSeconds = 0.2f;
            m.WatchdogThreadEnabled = true;
            m.BeginSession("s1", "dev", "1.0");
            m.Update(0f, 0f);
            Thread.Sleep(1000);                 // 模拟主线程卡住（看门狗 50ms 轮询）
            Assert.IsTrue(m.IsHanging, "看门狗线程应已判定卡死。");
            m.Update(0f, 0f);
            Assert.IsFalse(m.IsHanging);
            Assert.AreEqual(1, m.HangCount);
            List<CrashReport> reports = new List<CrashReport>();
            m.GetSessionReports(reports);
            Assert.GreaterOrEqual(reports[0].HangSeconds, 0.9f);
            m.EndSession();
        }

        // ---------------- 异常退出 ----------------

        [Test]
        public void CleanEnd_NoAbnormalReportNextSession()
        {
            CrashManager first = NewManager();
            first.BeginSession("s1", "dev", "1.0");
            first.EndSession();
            CrashManager second = NewManager();
            Assert.IsFalse(second.BeginSession("s2", "dev", "1.0"));
        }

        [TestCase(0, CrashKind.AbnormalExit)]
        [TestCase(1, CrashKind.BackgroundKill)]
        [TestCase(2, CrashKind.LowMemoryKill)]
        [TestCase(3, CrashKind.HangKill)]
        public void MissingEnd_ClassifiedFromLastKnownState(int scenario, CrashKind expected)
        {
            CrashManager first = NewManager();
            first.HangThresholdSeconds = 1f;
            first.BeginSession("s1", "dev-a", "1.0");
            first.SetPhase("Loading:Region_7");
            first.AddBreadcrumb("scene", "load region 7");
            first.Update(0f, 0f);
            m_Now += 2.0;
            first.Update(0f, 0f);               // 刷新延迟的会话标记（阶段变化）
            if (scenario == 1) first.NotifyBackground(true);
            if (scenario == 2) first.NotifyLowMemory();
            if (scenario == 3) { m_Now += 5.0; first.PollWatchdog(m_Now); }
            // 模拟进程被杀：不调用 EndSession / Shutdown
            m_Managers.Remove(first);

            CrashManager second = NewManager();
            Listener listener = new Listener();
            second.AddListener(listener);
            Assert.IsTrue(second.BeginSession("s2", "dev-b", "1.1"));
            second.Update(0f, 0f);

            CrashReport abnormal = listener.Reports.Find(r => r.Kind == expected);
            Assert.IsNotNull(abnormal, "应生成 " + expected + " 报告。");
            Assert.AreEqual("s1", abnormal.SessionId, "报告归属上一个会话。");
            Assert.AreEqual("dev-a", abnormal.DeviceInfo);
            Assert.AreEqual("Loading:Region_7", abnormal.Phase);
            Assert.IsTrue(Array.Exists(abnormal.Breadcrumbs, c => c.Contains("load region 7")));
        }

        [Test]
        public void FatalReported_NoDuplicateAbnormalExit()
        {
            CrashManager first = NewManager();
            first.BeginSession("s1", "dev", "1.0");
            first.ReportException("InvalidOperationException: fatal", StackA, true);
            m_Managers.Remove(first);

            CrashManager second = NewManager();
            Listener listener = new Listener();
            second.AddListener(listener);
            Assert.IsTrue(second.BeginSession("s2", "dev", "1.0"), "上次仍是异常结束。");
            second.Update(0f, 0f);
            Assert.AreEqual(0, listener.Reports.Count, "致命报告已单独落盘，不再生成异常退出报告。");
            Assert.AreEqual(1, second.PendingUploadCount, "上个会话的致命报告待上传。");
        }

        [Test]
        public void CorruptMarker_StillReportsAbnormalExit()
        {
            Directory.CreateDirectory(m_Dir);
            File.WriteAllBytes(Path.Combine(m_Dir, "session.ejsm"), new byte[] { 1, 2, 3 });
            CrashManager m = NewManager();
            Listener listener = new Listener();
            m.AddListener(listener);
            Assert.IsTrue(m.BeginSession("s2", "dev", "1.0"));
            m.Update(0f, 0f);
            Assert.AreEqual(1, listener.Reports.Count);
            Assert.AreEqual(CrashKind.AbnormalExit, listener.Reports[0].Kind);
        }

        // ---------------- 上传 / 存储上限 ----------------

        [Test]
        public void Upload_SuccessDeletesFile_CorruptFileDiscarded()
        {
            Directory.CreateDirectory(m_Dir);
            File.WriteAllBytes(Path.Combine(m_Dir, "0000000000000-bad-001.ejcr"), new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9 });
            CrashManager m = NewManager();
            FakeUploader uploader = new FakeUploader();
            m.SetUploader(uploader);
            m.BeginSession("s1", "dev", "1.0");
            m.ReportException("NullReferenceException: x", StackA, false);
            m.ReportException("ArgumentException: y", StackB, false);
            for (int i = 0; i < 6; i++) m.Update(0f, 0f);

            Assert.AreEqual(2, uploader.Uploaded.Count);
            Assert.AreEqual(2, m.UploadedCount);
            Assert.AreEqual(0, m.PendingUploadCount);
            Assert.AreEqual(0, Directory.GetFiles(m_Dir, "*.ejcr").Length, "成功上传删文件，损坏文件被丢弃。");
        }

        [Test]
        public void Upload_FailuresRetryThenStopForSession_FilesKept()
        {
            CrashManager m = NewManager();
            FakeUploader uploader = new FakeUploader { Fail = true };
            m.SetUploader(uploader);
            m.BeginSession("s1", "dev", "1.0");
            m.ReportException("NullReferenceException: x", StackA, false);
            for (int i = 0; i < 20; i++) m.Update(0f, 0f);
            Assert.AreEqual(3, uploader.Uploaded.Count, "本会话连续失败 3 次后停止。");
            Assert.AreEqual(1, Directory.GetFiles(m_Dir, "*.ejcr").Length, "失败的报告保留到下个会话。");
            Assert.AreEqual(1, m.PendingUploadCount);
        }

        [Test]
        public void Upload_CompletionFromWorkerThread_SettledOnMainThread()
        {
            CrashManager m = NewManager();
            FakeUploader uploader = new FakeUploader { Hold = true };
            m.SetUploader(uploader);
            m.BeginSession("s1", "dev", "1.0");
            m.ReportException("NullReferenceException: x", StackA, false);
            m.Update(0f, 0f);
            Assert.AreEqual(1, uploader.Uploaded.Count);
            Thread t = new Thread(() => uploader.Pending(uploader.PendingReport, true));
            t.Start();
            t.Join();
            Assert.AreEqual(0, m.UploadedCount);
            m.Update(0f, 0f);
            Assert.AreEqual(1, m.UploadedCount);
            Assert.AreEqual(0, Directory.GetFiles(m_Dir, "*.ejcr").Length);
        }

        [Test]
        public void StoredReports_TrimmedToMax_OldestFirst()
        {
            Directory.CreateDirectory(m_Dir);
            for (int i = 0; i < 5; i++)
            {
                CrashReport r = new CrashReport { ReportId = "000000000000" + i + "-old-001", Message = "m" + i };
                File.WriteAllBytes(Path.Combine(m_Dir, r.ReportId + ".ejcr"), r.ToBytes());
            }

            CrashManager m = NewManager();
            m.MaxStoredReports = 3;
            m.BeginSession("s1", "dev", "1.0");
            string[] files = Directory.GetFiles(m_Dir, "*.ejcr");
            Array.Sort(files, StringComparer.Ordinal);
            Assert.AreEqual(3, files.Length);
            StringAssert.Contains("0000000000002-old", files[0]);
            Assert.AreEqual(3, m.PendingUploadCount);
        }

        // ---------------- 遥测 / 线程 ----------------

        [Test]
        public void NewReports_WriteTelemetryCrashRecords_AndSummaryOnEnd()
        {
            TelemetryManager telemetry = new TelemetryManager();
            try
            {
                telemetry.BatchSize = 1000;
                telemetry.StartSession("s1", "dev", "1.0");
                CrashManager m = NewManager(false);
                m.SetTelemetry(telemetry);
                m.BeginSession("s1", "dev", "1.0");
                m.ReportException("NullReferenceException: x", StackA, false);
                m.ReportException("ArgumentException: y", StackB, false);
                m.Update(0f, 0f);
                Assert.AreEqual(2, telemetry.PendingRecordCount, "每份新报告一条记录。");
                for (int i = 0; i < 4; i++) m.ReportException("NullReferenceException: x", StackA, false);
                m.EndSession();
                Assert.AreEqual(3, telemetry.PendingRecordCount, "会话结束只为次数又增长的报告补记。");
            }
            finally
            {
                telemetry.Shutdown();
            }
        }

        [Test]
        public void ConcurrentReports_FromManyThreads_AreConsistent()
        {
            CrashManager m = NewManager();
            m.MaxReportsPerSession = 1000;
            m.BeginSession("s1", "dev", "1.0");
            Thread[] threads = new Thread[4];
            for (int t = 0; t < threads.Length; t++)
            {
                int id = t;
                threads[t] = new Thread(() =>
                {
                    for (int i = 0; i < 250; i++)
                    {
                        m.AddBreadcrumb("t", "crumb");
                        m.ReportException("NullReferenceException: x", "Worker.Run" + (i % 5) + " () (at w.cs:1)\n", false);
                    }
                });
            }

            foreach (Thread thread in threads) thread.Start();
            foreach (Thread thread in threads) thread.Join();

            List<CrashReport> reports = new List<CrashReport>();
            m.GetSessionReports(reports);
            Assert.AreEqual(5, reports.Count);
            int total = 0;
            foreach (CrashReport r in reports) total += r.Occurrences;
            Assert.AreEqual(1000, total);
            Assert.AreEqual(1000, m.TotalExceptions);
            Assert.AreEqual(5, Directory.GetFiles(m_Dir, "*.ejcr").Length);
        }

        [Test]
        public void Update_SteadyState_DoesNotAllocate()
        {
            CrashManager m = NewManager(false);
            m.BeginSession("s1", "dev", "1.0");
            TestDelegate body = () => { for (int i = 0; i < 100; i++) m.Update(0.016f, 0.016f); };
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }
    }
}
