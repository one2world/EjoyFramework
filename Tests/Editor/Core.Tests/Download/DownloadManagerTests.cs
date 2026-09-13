//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.Download;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Core.Tests.Download
{
    /// <summary>
    /// DownloadManager 单测：用可手动驱动的 fake IDownloadAgentHelper（同步触发进度 / 完成 / 错误）验证
    /// 序列号递增、并发上限排队、成功后腾出代理拉起下一个、失败续传重试后失败、以及移除排队任务。
    /// 不依赖 Unity / 网络。
    /// </summary>
    public class DownloadManagerTests
    {
        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        /// <summary>
        /// 可手动驱动的 fake：记录最近一次 Download 调用的参数与续传起点；提供 Complete / Error 方法在测试中显式触发对应事件。
        /// 进度通过 Progress(delta) 触发。Download/Reset 计数便于断言续传重试。
        /// </summary>
        private sealed class FakeAgentHelper : IDownloadAgentHelper
        {
            public string LastUri;
            public long LastFromPosition;
            public string LastSavePath;
            public int DownloadCallCount;
            public int ResetCallCount;
            public bool IsDownloading;

            public event Action<IDownloadAgentHelper, long> DownloadAgentHelperUpdate;
            public event Action<IDownloadAgentHelper, long> DownloadAgentHelperComplete;
            public event Action<IDownloadAgentHelper, string> DownloadAgentHelperError;

            public void Download(string downloadUri, long fromPosition, string savePath)
            {
                DownloadCallCount++;
                LastUri = downloadUri;
                LastFromPosition = fromPosition;
                LastSavePath = savePath;
                IsDownloading = true;
            }

            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                // fake 不自走；进度/完成/失败由测试显式触发。
            }

            public void Reset()
            {
                ResetCallCount++;
                IsDownloading = false;
            }

            // ===== 测试驱动 API =====

            public void SimulateProgress(long deltaBytes)
            {
                DownloadAgentHelperUpdate?.Invoke(this, deltaBytes);
            }

            public void SimulateComplete(long totalLength)
            {
                IsDownloading = false;
                DownloadAgentHelperComplete?.Invoke(this, totalLength);
            }

            public void SimulateError(string message)
            {
                IsDownloading = false;
                DownloadAgentHelperError?.Invoke(this, message);
            }
        }

        private static DownloadManager NewManager(out FakeAgentHelper[] fakes, int agentCount)
        {
            var mgr = new DownloadManager();
            fakes = new FakeAgentHelper[agentCount];
            for (int i = 0; i < agentCount; i++)
            {
                fakes[i] = new FakeAgentHelper();
                mgr.AddAgentHelper(fakes[i]);
            }
            return mgr;
        }

        // ===== 序列号 =====

        [Test]
        public void AddDownload_ReturnsIncreasingSerialIds()
        {
            var mgr = NewManager(out _, agentCount: 1);

            int a = mgr.AddDownload("http://h/a", "/tmp/a");
            int b = mgr.AddDownload("http://h/b", "/tmp/b");
            int c = mgr.AddDownload("http://h/c", "/tmp/c");

            Assert.Greater(b, a);
            Assert.Greater(c, b);
        }

        [Test]
        public void AddDownload_InvalidArgs_Throw()
        {
            var mgr = NewManager(out _, agentCount: 1);

            Assert.Throws<ArgumentException>(() => mgr.AddDownload(null, "/tmp/a"));
            Assert.Throws<ArgumentException>(() => mgr.AddDownload("http://h/a", ""));
        }

        [Test]
        public void AddAgentHelper_Null_Throws()
        {
            var mgr = new DownloadManager();
            Assert.Throws<ArgumentNullException>(() => mgr.AddAgentHelper(null));
        }

        [Test]
        public void UnityHelper_AppendMode_OnlyEnabledForResume()
        {
            MethodInfo method = typeof(UnityWebRequestDownloadAgentHelper).GetMethod(
                "ShouldAppendToExistingFile",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(method);
            Assert.IsFalse((bool)method.Invoke(null, new object[] { 0L }), "新任务必须截断旧的半文件");
            Assert.IsTrue((bool)method.Invoke(null, new object[] { 1L }), "续传任务必须追加");
        }

        // ===== 并发上限 / 排队 =====

        [Test]
        public void AddDownload_RespectsConcurrencyCap_ExtraTasksStayQueued()
        {
            var mgr = NewManager(out var fakes, agentCount: 3);
            mgr.MaxConcurrentDownloads = 2;

            mgr.AddDownload("http://h/1", "/tmp/1");
            mgr.AddDownload("http://h/2", "/tmp/2");
            mgr.AddDownload("http://h/3", "/tmp/3"); // 超出并发上限 2，应排队

            Assert.AreEqual(2, mgr.WorkingAgentCount, "并发上限 2 时应只有 2 个代理在工作");
            Assert.AreEqual(1, mgr.WaitingTaskCount, "第 3 个任务应排队等待");
            // 仅前两个代理被启动。
            int started = 0;
            for (int i = 0; i < fakes.Length; i++) if (fakes[i].DownloadCallCount > 0) started++;
            Assert.AreEqual(2, started);
        }

        [Test]
        public void AddDownload_LimitedByAgentCount_EvenIfCapHigher()
        {
            var mgr = NewManager(out _, agentCount: 1);
            mgr.MaxConcurrentDownloads = 5;

            mgr.AddDownload("http://h/1", "/tmp/1");
            mgr.AddDownload("http://h/2", "/tmp/2");

            Assert.AreEqual(1, mgr.WorkingAgentCount, "只有 1 个代理，实际并发受代理数封顶");
            Assert.AreEqual(1, mgr.WaitingTaskCount);
            Assert.AreEqual(1, mgr.TotalAgentCount);
            Assert.AreEqual(0, mgr.FreeAgentCount);
        }

        // ===== 成功后拉起下一个 =====

        [Test]
        public void Success_FiresDownloadSuccess_FreesAgent_AndStartsNextWaiting()
        {
            var mgr = NewManager(out var fakes, agentCount: 1);
            mgr.MaxConcurrentDownloads = 1;

            var successes = new List<int>();
            mgr.DownloadSuccess += e => successes.Add(e.SerialId);

            int s1 = mgr.AddDownload("http://h/1", "/tmp/1");
            int s2 = mgr.AddDownload("http://h/2", "/tmp/2");

            Assert.AreEqual(1, mgr.WorkingAgentCount);
            Assert.AreEqual(1, mgr.WaitingTaskCount);

            // 第一个任务成功 → 触发成功、腾出代理、拉起第二个。
            fakes[0].SimulateComplete(123L);

            Assert.AreEqual(1, successes.Count);
            Assert.AreEqual(s1, successes[0]);
            Assert.AreEqual(0, mgr.WaitingTaskCount, "第二个任务应已被拉起");
            Assert.AreEqual(1, mgr.WorkingAgentCount);
            Assert.AreEqual("http://h/2", fakes[0].LastUri, "同一代理被复用于第二个任务");

            // 第二个也完成。
            fakes[0].SimulateComplete(456L);
            Assert.AreEqual(2, successes.Count);
            Assert.AreEqual(s2, successes[1]);
            Assert.AreEqual(0, mgr.WorkingAgentCount);
            Assert.AreEqual(1, mgr.FreeAgentCount);
        }

        [Test]
        public void Progress_FiresDownloadUpdate_WithAccumulatedLength()
        {
            var mgr = NewManager(out var fakes, agentCount: 1);

            var updates = new List<long>();
            mgr.DownloadUpdate += e => updates.Add(e.CurrentLength);

            mgr.AddDownload("http://h/1", "/tmp/1");
            fakes[0].SimulateProgress(10L);
            fakes[0].SimulateProgress(15L);

            Assert.AreEqual(2, updates.Count);
            Assert.AreEqual(10L, updates[0]);
            Assert.AreEqual(25L, updates[1], "增量应累计");
        }

        [Test]
        public void Start_FiresDownloadStart()
        {
            var mgr = NewManager(out _, agentCount: 1);

            DownloadStartEventArgs started = null;
            mgr.DownloadStart += e => started = e;

            int serial = mgr.AddDownload("http://h/1", "/tmp/1", userData: "ctx");

            Assert.IsNotNull(started);
            Assert.AreEqual(serial, started.SerialId);
            Assert.AreEqual("http://h/1", started.DownloadUri);
            Assert.AreEqual("/tmp/1", started.SavePath);
            Assert.AreEqual("ctx", started.UserData);
        }

        // ===== 失败 + 续传重试 =====

        [Test]
        public void Failure_RetriesUpToRetryCount_ThenFiresFailure()
        {
            var mgr = NewManager(out var fakes, agentCount: 1);
            mgr.RetryCount = 2;

            DownloadFailureEventArgs failure = null;
            mgr.DownloadFailure += e => failure = e;

            int serial = mgr.AddDownload("http://h/1", "/tmp/1");
            Assert.AreEqual(1, fakes[0].DownloadCallCount, "首发");

            // 累计一些进度后报错 → 第一次重试（续传）。
            fakes[0].SimulateProgress(50L);
            fakes[0].SimulateError("net err 1");
            Assert.IsNull(failure, "尚未耗尽重试");
            Assert.AreEqual(2, fakes[0].DownloadCallCount, "第一次重试");
            Assert.AreEqual(50L, fakes[0].LastFromPosition, "应从已累计字节处续传");

            // 再次报错 → 第二次重试。
            fakes[0].SimulateError("net err 2");
            Assert.IsNull(failure);
            Assert.AreEqual(3, fakes[0].DownloadCallCount, "第二次重试");

            // 再报错 → 重试耗尽，触发失败并腾出代理。
            fakes[0].SimulateError("net err 3");
            Assert.IsNotNull(failure);
            Assert.AreEqual(serial, failure.SerialId);
            Assert.AreEqual("net err 3", failure.ErrorMessage);
            Assert.AreEqual(0, mgr.WorkingAgentCount, "失败后代理应空闲");
        }

        [Test]
        public void Failure_WithZeroRetry_FiresFailureImmediately_AndStartsNext()
        {
            var mgr = NewManager(out var fakes, agentCount: 1);
            mgr.MaxConcurrentDownloads = 1;
            mgr.RetryCount = 0;

            var failures = new List<int>();
            mgr.DownloadFailure += e => failures.Add(e.SerialId);

            int s1 = mgr.AddDownload("http://h/1", "/tmp/1");
            mgr.AddDownload("http://h/2", "/tmp/2");

            fakes[0].SimulateError("boom");

            Assert.AreEqual(1, failures.Count);
            Assert.AreEqual(s1, failures[0]);
            Assert.AreEqual(0, mgr.WaitingTaskCount, "失败后应拉起下一个等待任务");
            Assert.AreEqual("http://h/2", fakes[0].LastUri);
        }

        // ===== 移除 =====

        [Test]
        public void RemoveDownload_QueuedTask_RemovesFromQueue()
        {
            var mgr = NewManager(out _, agentCount: 1);
            mgr.MaxConcurrentDownloads = 1;

            mgr.AddDownload("http://h/1", "/tmp/1"); // 工作中
            int queued = mgr.AddDownload("http://h/2", "/tmp/2"); // 排队

            Assert.AreEqual(1, mgr.WaitingTaskCount);

            bool removed = mgr.RemoveDownload(queued);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, mgr.WaitingTaskCount);
            Assert.AreEqual(1, mgr.WorkingAgentCount, "工作中的任务不受影响");
        }

        [Test]
        public void RemoveDownload_WorkingTask_AbortsAgent_AndStartsNext()
        {
            var mgr = NewManager(out var fakes, agentCount: 1);
            mgr.MaxConcurrentDownloads = 1;

            int working = mgr.AddDownload("http://h/1", "/tmp/1");
            mgr.AddDownload("http://h/2", "/tmp/2");

            Assert.AreEqual(1, mgr.WorkingAgentCount);
            Assert.AreEqual(1, mgr.WaitingTaskCount);

            bool removed = mgr.RemoveDownload(working);

            Assert.IsTrue(removed);
            Assert.GreaterOrEqual(fakes[0].ResetCallCount, 1, "在途请求应被中止");
            Assert.AreEqual(0, mgr.WaitingTaskCount, "下一个应被拉起");
            Assert.AreEqual("http://h/2", fakes[0].LastUri);
        }

        [Test]
        public void RemoveDownload_UnknownSerial_ReturnsFalse()
        {
            var mgr = NewManager(out _, agentCount: 1);
            mgr.AddDownload("http://h/1", "/tmp/1");

            Assert.IsFalse(mgr.RemoveDownload(99999));
        }

        [Test]
        public void RemoveAllDownloads_ClearsQueueAndAbortsWorking()
        {
            var mgr = NewManager(out var fakes, agentCount: 2);
            mgr.MaxConcurrentDownloads = 2;

            mgr.AddDownload("http://h/1", "/tmp/1");
            mgr.AddDownload("http://h/2", "/tmp/2");
            mgr.AddDownload("http://h/3", "/tmp/3"); // 排队

            Assert.AreEqual(2, mgr.WorkingAgentCount);
            Assert.AreEqual(1, mgr.WaitingTaskCount);

            mgr.RemoveAllDownloads();

            Assert.AreEqual(0, mgr.WaitingTaskCount);
            Assert.AreEqual(0, mgr.WorkingAgentCount);
            Assert.GreaterOrEqual(fakes[0].ResetCallCount, 1);
            Assert.GreaterOrEqual(fakes[1].ResetCallCount, 1);
        }

        // ===== Update 驱动 =====

        [Test]
        public void Update_DrivesWorkingAgentHelper()
        {
            var mgr = NewManager(out var fakes, agentCount: 1);

            // 用一个会在 Update 内完成的 helper 来验证 manager.Update 会驱动 helper 并收口。
            var updates = new List<long>();
            mgr.DownloadUpdate += e => updates.Add(e.CurrentLength);
            var successes = new List<int>();
            mgr.DownloadSuccess += e => successes.Add(e.SerialId);

            mgr.AddDownload("http://h/1", "/tmp/1");

            // 通过 fake 在外部模拟一次进度，再 Update（fake 的 Update 是空操作，但要验证不抛且状态正确）。
            fakes[0].SimulateProgress(5L);
            mgr.Update(0.016f, 0.016f);
            Assert.AreEqual(1, mgr.WorkingAgentCount);
            Assert.AreEqual(1, updates.Count);

            fakes[0].SimulateComplete(5L);
            mgr.Update(0.016f, 0.016f);
            Assert.AreEqual(1, successes.Count);
            Assert.AreEqual(0, mgr.WorkingAgentCount);
        }

        [Test]
        public void Shutdown_ClearsAgentsAndQueue()
        {
            var mgr = NewManager(out _, agentCount: 2);
            mgr.AddDownload("http://h/1", "/tmp/1");
            mgr.AddDownload("http://h/2", "/tmp/2");
            mgr.AddDownload("http://h/3", "/tmp/3");

            mgr.Shutdown();

            Assert.AreEqual(0, mgr.TotalAgentCount);
            Assert.AreEqual(0, mgr.WaitingTaskCount);
            Assert.AreEqual(0, mgr.WorkingAgentCount);
        }
    }
}
