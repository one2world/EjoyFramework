//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.Video;

namespace EjoyFramework.Core.Tests.Video
{
    /// <summary>
    /// VideoManager 单测：用一个可驱动的 fake IVideoPlayerHelper 验证
    /// 播放转发与事件编排（Started / Completed / Error）、单次回调、Skip 门控、控制方法转发，
    /// 以及未设置 Helper 时的优雅失败。不依赖 Unity / 真实视频。
    /// </summary>
    public class VideoManagerTests
    {
        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        /// <summary>
        /// 可驱动 fake：记录控制方法调用；提供方法主动模拟底层 Prepared / Completed / Error 事件。
        /// </summary>
        private sealed class FakeVideoPlayerHelper : IVideoPlayerHelper
        {
            public int PlayCount;
            public int StopCount;
            public int PauseCount;
            public int ResumeCount;
            public int UpdateCount;
            public string LastSource;
            public bool LastLoop;

            private bool m_IsPlaying;
            private bool m_IsPaused;

            public bool IsPlaying { get { return m_IsPlaying; } }
            public bool IsPaused { get { return m_IsPaused; } }

            public void Play(string source, bool loop)
            {
                PlayCount++;
                LastSource = source;
                LastLoop = loop;
                m_IsPlaying = true;
                m_IsPaused = false;
            }

            public void Stop()
            {
                StopCount++;
                m_IsPlaying = false;
                m_IsPaused = false;
            }

            public void Pause()
            {
                PauseCount++;
                m_IsPaused = true;
            }

            public void Resume()
            {
                ResumeCount++;
                m_IsPaused = false;
            }

            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                UpdateCount++;
            }

            // ===== 测试驱动入口：模拟底层事件 =====
            public void SimulatePrepared() { Prepared?.Invoke(this); }

            public void SimulateCompleted()
            {
                m_IsPlaying = false;
                Completed?.Invoke(this);
            }

            public void SimulateError(string message)
            {
                m_IsPlaying = false;
                ErrorOccurred?.Invoke(this, message);
            }

            public event Action<IVideoPlayerHelper> Prepared;
            public event Action<IVideoPlayerHelper> Completed;
            public event Action<IVideoPlayerHelper, string> ErrorOccurred;
        }

        private static (VideoManager mgr, FakeVideoPlayerHelper helper) NewManager()
        {
            var helper = new FakeVideoPlayerHelper();
            var mgr = new VideoManager();
            mgr.SetHelper(helper);
            return (mgr, helper);
        }

        // ===== 播放转发 + Started 事件 =====

        [Test]
        public void Play_ForwardsToHelper_AndFiresStarted()
        {
            var (mgr, helper) = NewManager();

            string startedSource = null;
            int startedCount = 0;
            mgr.OnVideoStarted += (m, s) => { startedCount++; startedSource = s; };

            mgr.Play("intro.mp4", loop: true);

            Assert.AreEqual(1, helper.PlayCount, "应转发一次 helper.Play");
            Assert.AreEqual("intro.mp4", helper.LastSource);
            Assert.IsTrue(helper.LastLoop, "loop 标志应透传");
            Assert.AreEqual(1, startedCount, "应触发一次 OnVideoStarted");
            Assert.AreEqual("intro.mp4", startedSource);
            Assert.IsTrue(mgr.IsPlaying);
        }

        // ===== 完成事件 + 单次 onCompleted =====

        [Test]
        public void HelperCompleted_FiresCompleted_AndInvokesPerCallOnCompleted()
        {
            var (mgr, helper) = NewManager();

            int completedEventCount = 0;
            string completedSource = null;
            mgr.OnVideoCompleted += (m, s) => { completedEventCount++; completedSource = s; };

            int perCall = 0;
            mgr.Play("cg.mp4", onCompleted: () => perCall++);

            helper.SimulateCompleted();

            Assert.AreEqual(1, completedEventCount, "应触发一次 OnVideoCompleted");
            Assert.AreEqual("cg.mp4", completedSource);
            Assert.AreEqual(1, perCall, "应触发一次单次 onCompleted");
        }

        [Test]
        public void PerCallOnCompleted_NotInvokedTwice_OnSecondCompletedSignal()
        {
            var (mgr, helper) = NewManager();

            int perCall = 0;
            mgr.Play("cg.mp4", onCompleted: () => perCall++);

            helper.SimulateCompleted();
            // 第二次完成信号（无活动播放）不应再次触发单次回调。
            helper.SimulateCompleted();

            Assert.AreEqual(1, perCall, "单次 onCompleted 仅触发一次，结算后清空");
        }

        // ===== 错误事件 + 单次 onError =====

        [Test]
        public void HelperError_FiresError_AndInvokesPerCallOnError()
        {
            var (mgr, helper) = NewManager();

            int errorEventCount = 0;
            string errSource = null;
            string errMessage = null;
            mgr.OnVideoError += (m, s, msg) => { errorEventCount++; errSource = s; errMessage = msg; };

            string perCallMessage = null;
            mgr.Play("cg.mp4", onError: msg => perCallMessage = msg);

            helper.SimulateError("decode failed");

            Assert.AreEqual(1, errorEventCount, "应触发一次 OnVideoError");
            Assert.AreEqual("cg.mp4", errSource);
            Assert.AreEqual("decode failed", errMessage);
            Assert.AreEqual("decode failed", perCallMessage, "应触发单次 onError 并携带消息");
        }

        // ===== Skip 门控 =====

        [Test]
        public void Skip_DoesNothing_WhenNotSkippable()
        {
            var (mgr, helper) = NewManager();
            mgr.Skippable = false;

            int completedCount = 0;
            mgr.OnVideoCompleted += (m, s) => completedCount++;

            mgr.Play("cg.mp4");
            bool skipped = mgr.Skip();

            Assert.IsFalse(skipped, "不可跳过时 Skip 应返回 false");
            Assert.AreEqual(0, helper.StopCount, "不可跳过时不应停止");
            Assert.AreEqual(0, completedCount, "不可跳过时不应触发完成");
            Assert.IsTrue(mgr.IsPlaying, "仍应在播放");
        }

        [Test]
        public void Skip_StopsAndCompletes_WhenSkippable()
        {
            var (mgr, helper) = NewManager();
            mgr.Skippable = true;

            int completedCount = 0;
            string completedSource = null;
            mgr.OnVideoCompleted += (m, s) => { completedCount++; completedSource = s; };

            int perCall = 0;
            mgr.Play("cg.mp4", onCompleted: () => perCall++);

            bool skipped = mgr.Skip();

            Assert.IsTrue(skipped, "可跳过且正在播放时 Skip 应返回 true");
            Assert.AreEqual(1, helper.StopCount, "跳过应停止底层");
            Assert.AreEqual(1, completedCount, "跳过应按完成处理");
            Assert.AreEqual("cg.mp4", completedSource);
            Assert.AreEqual(1, perCall, "跳过应触发单次 onCompleted");
            Assert.IsFalse(mgr.IsPlaying);
        }

        [Test]
        public void Skip_ReturnsFalse_WhenNothingPlaying()
        {
            var (mgr, _) = NewManager();
            mgr.Skippable = true;

            Assert.IsFalse(mgr.Skip(), "无进行中的播放时 Skip 应返回 false");
        }

        // ===== 控制方法转发 =====

        [Test]
        public void Stop_Pause_Resume_ForwardToHelper()
        {
            var (mgr, helper) = NewManager();
            mgr.Play("cg.mp4");

            mgr.Pause();
            mgr.Resume();
            mgr.Stop();

            Assert.AreEqual(1, helper.PauseCount, "Pause 应转发");
            Assert.AreEqual(1, helper.ResumeCount, "Resume 应转发");
            Assert.AreEqual(1, helper.StopCount, "Stop 应转发");
        }

        [Test]
        public void Stop_DoesNotFireCompleted_NorPerCallOnCompleted()
        {
            var (mgr, helper) = NewManager();

            int completedCount = 0;
            mgr.OnVideoCompleted += (m, s) => completedCount++;

            int perCall = 0;
            mgr.Play("cg.mp4", onCompleted: () => perCall++);

            mgr.Stop();
            // Stop 后再来的完成信号（无活动播放）也不应触发回调。
            helper.SimulateCompleted();

            Assert.AreEqual(0, completedCount, "主动 Stop 不触发完成事件");
            Assert.AreEqual(0, perCall, "主动 Stop 不触发单次 onCompleted");
        }

        [Test]
        public void Update_ForwardsToHelper()
        {
            var (mgr, helper) = NewManager();

            mgr.Update(0.016f, 0.016f);
            mgr.Update(0.016f, 0.016f);

            Assert.AreEqual(2, helper.UpdateCount, "Update 应转发给 helper");
        }

        // ===== 无 Helper 时优雅失败 =====

        [Test]
        public void Play_NoHelper_FiresError_DoesNotThrow()
        {
            var mgr = new VideoManager(); // 未 SetHelper

            int errorCount = 0;
            string errSource = null;
            mgr.OnVideoError += (m, s, msg) => { errorCount++; errSource = s; };

            string perCallMessage = null;
            Assert.DoesNotThrow(() => mgr.Play("cg.mp4", onError: msg => perCallMessage = msg));

            Assert.AreEqual(1, errorCount, "无 Helper 时应触发 OnVideoError");
            Assert.AreEqual("cg.mp4", errSource);
            Assert.IsNotNull(perCallMessage, "无 Helper 时应触发单次 onError");
            Assert.IsFalse(mgr.IsPlaying);
        }

        // ===== 入参校验 =====

        [Test]
        public void Play_NullOrEmptySource_Throws()
        {
            var (mgr, _) = NewManager();

            Assert.Throws<ArgumentException>(() => mgr.Play(null));
            Assert.Throws<ArgumentException>(() => mgr.Play(string.Empty));
        }
    }
}
