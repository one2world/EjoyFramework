//------------------------------------------------------------
// EjoyGame Framework Tests (PlayMode)
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EjoyFramework.Core;
using EjoyFramework.Core.Benchmarking;
using EjoyFramework.Core.Quality;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Tests.PlayMode
{
    /// <summary>WS4-M4：真机采集组件在播放循环里逐帧采集、按时长自动停、写报告（工程内目录）、暂停并恢复自动画质。</summary>
    public sealed class PerfCapturePlayModeTests : PlayModeTestBase
    {
        private GameObject m_Go;
        private string m_Dir;

        public override void SetUp()
        {
            base.SetUp();
            m_Dir = Path.GetFullPath(Path.Combine("Temp", "EjoyTests", "perfcapture_" + Guid.NewGuid().ToString("N")));
        }

        public override void TearDown()
        {
            if (m_Go != null) UnityEngine.Object.DestroyImmediate(m_Go);
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator Capture_RecordsFrames_AutoStops_WritesReport_RestoresAutoQuality()
        {
            IQualityManager quality = Framework.GetModule<IQualityManager>();
            m_Go = CreateGameObject("Capture");
            PerfCaptureComponent capture = m_Go.AddComponent<PerfCaptureComponent>();
            capture.ReportDirectory = m_Dir;
            yield return null;

            capture.StartCapture("bench run", 0.25f);
            Assert.IsTrue(capture.IsCapturing);
            Assert.Throws<FrameworkException>(() => capture.StartCapture("again"));
            capture.Mark("half");
            yield return WaitUntil(() => !capture.IsCapturing, 10f);

            PerfCaptureSummary s = capture.Capture.Summary;
            Assert.Greater(s.Frames, 0);
            Assert.GreaterOrEqual(s.DurationSeconds, 0.25);
            Assert.Greater(s.AvgFrameMs, 0f);
            Assert.Greater(s.PeakTotalMB, 0, "至少采到一次内存。");

            Assert.IsNotNull(capture.LastReportPath);
            StringAssert.StartsWith(m_Dir, capture.LastReportPath, "报告写到指定的工程内目录。");
            StringAssert.Contains("bench_run-", Path.GetFileName(capture.LastReportPath), "文件名里的空格被替换。");
            string text = File.ReadAllText(capture.LastReportPath);
            StringAssert.StartsWith("# ejoy-perfcapture 1\n# label\tbench run\n", text);
            StringAssert.Contains("marker\t", text);
            StringAssert.Contains("# unity\t", text);

            // 采集期间 SuspendAuto 一次、结束 ResumeAuto 一次：再 Resume 必然不配对
            Assert.Throws<FrameworkException>(() => quality.ResumeAuto());
            Assert.Throws<FrameworkException>(() => capture.StopCapture());
        }
    }
}
