//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 本帧"工作耗时"采样：max(主线程耗时 − Present 等待, 渲染线程耗时, GPU 耗时)，来自 <see cref="FrameTimingManager"/>。
    /// 墙钟帧间隔会被 targetFrameRate / 垂直同步封顶，看不出富余；工作耗时才反映真实负载。
    /// 需 Player Settings 勾选 Frame Timing Stats；不可用时 <see cref="TrySample"/> 返回 false。每帧最多调用一次。
    /// </summary>
    public sealed class FrameWorkTimeSampler
    {
        private readonly FrameTiming[] m_Timings = new FrameTiming[1];
        private readonly bool m_Supported;

        public FrameWorkTimeSampler()
        {
            m_Supported = FrameTimingManager.IsFeatureEnabled();
        }

        public bool IsSupported { get { return m_Supported; } }

        /// <summary>采样（零分配）。成功返回 true 并给出工作耗时毫秒。</summary>
        public bool TrySample(out float workMs)
        {
            workMs = 0f;
            if (!m_Supported) return false;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, m_Timings) == 0) return false;
            FrameTiming t = m_Timings[0];
            double busiest = t.cpuMainThreadFrameTime - t.cpuMainThreadPresentWaitTime;
            if (t.cpuRenderThreadFrameTime > busiest) busiest = t.cpuRenderThreadFrameTime;
            if (t.gpuFrameTime > busiest) busiest = t.gpuFrameTime;
            if (!(busiest > 0.0)) return false;
            workMs = (float)busiest;
            return true;
        }
    }
}
