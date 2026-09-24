//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Benchmarking;
using UnityEngine.Profiling;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 用 Profiler 的 "GC.Alloc" 标记计数当前线程的托管分配次数（与 Test Framework 的 <c>Is.Not.AllocatingGCMemory</c> 同一机制，
    /// 在 Mono/Boehm 下有效；<c>GC.GetAllocatedBytesForCurrentThread</c> 在那里恒为 0）。
    /// 计的是分配**次数**不是字节。Recorder 不可用（部分发布构建）时 <see cref="IsAvailable"/> 为 false。
    /// </summary>
    public sealed class UnityAllocationProbe : IAllocationProbe
    {
        private readonly Recorder m_Recorder = Recorder.Get("GC.Alloc");

        public bool IsAvailable { get { return m_Recorder != null && m_Recorder.isValid; } }

        public string Unit { get { return "allocs"; } }

        public void Begin()
        {
            m_Recorder.enabled = false;
            m_Recorder.FilterToCurrentThread();
            m_Recorder.enabled = true;
        }

        public long End()
        {
            m_Recorder.enabled = false;
            m_Recorder.CollectFromAllThreads();
            return m_Recorder.sampleBlockCount;
        }
    }
}
