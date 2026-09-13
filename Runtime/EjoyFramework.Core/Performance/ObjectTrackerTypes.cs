//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Performance
{
    /// <summary>被追踪对象的存活状态。</summary>
    public enum TrackedObjectLiveness
    {
        /// <summary>托管对象已被 GC 回收（健康；该条目将在下次 Sweep 移除）。</summary>
        Collected = 0,

        /// <summary>普通托管对象仍被引用（存活）。</summary>
        Alive = 1,

        /// <summary>Unity 对象：托管与原生均存活。</summary>
        UnityNativeAlive = 2,

        /// <summary>Unity 对象：原生已 Destroy，但托管引用仍持有 —— 悬垂引用泄漏，重点排查。</summary>
        UnityNativeDestroyed = 3,
    }

    /// <summary>按类型聚合的追踪统计快照。</summary>
    public struct ObjectTrackerStats
    {
        /// <summary>类型全名。</summary>
        public string TypeName;

        /// <summary>该类型是否为 UnityEngine.Object 子类（需已注入 <see cref="IUnityObjectInspector"/> 才能判定）。</summary>
        public bool IsUnityObject;

        /// <summary>托管存活条目总数（普通对象 + Unity 对象）。</summary>
        public int Alive;

        /// <summary>其中：Unity 原生存活的条目数。</summary>
        public int UnityNativeAlive;

        /// <summary>其中：Unity 原生已 Destroy 但仍被托管持有的条目数（泄漏信号）。</summary>
        public int UnityNativeDestroyed;
    }

    /// <summary>单个被追踪对象的明细，用于钻取泄漏（含创建堆栈）。</summary>
    public struct ObjectTrackerRecord
    {
        /// <summary>类型全名。</summary>
        public string TypeName;

        /// <summary>登记时传入的标签（可为 null）。</summary>
        public string Tag;

        /// <summary>当前存活状态。</summary>
        public TrackedObjectLiveness Liveness;

        /// <summary>自登记以来经过的秒数。</summary>
        public float AgeSeconds;

        /// <summary>创建（登记）处的调用堆栈；未捕获时为 null。</summary>
        public string CreationStack;
    }
}
