//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;

namespace EjoyFramework.Core.Coroutines
{
    /// <summary>
    /// 协程管理器。把 Unity 协程（或任何 IEnumerator 调度器）的运行集中托管，
    /// 让业务代码可以：按 owner/tag 批量停止；查询当前在跑的协程清单（Debugger 用）；统一 Profiler 标记。
    ///
    /// 接入：Unity 层的 CoroutineComponent 在 Start 时调 SetHelper(...) 注入 UnityCoroutineHelper。
    /// 使用：
    ///   var h = manager.Run(MyRoutine(), tag: "LoadScene/main", owner: this);
    ///   manager.Stop(h);
    ///   manager.StopByOwner(this);  // MonoBehaviour OnDestroy 一键清理
    /// </summary>
    public interface ICoroutineManager
    {
        /// <summary>当前在跑的协程数。</summary>
        int RunningCount { get; }

        /// <summary>
        /// 驱动器是否已注入。Unity 层的 CoroutineComponent.Awake 调用 SetHelper 后变 true。
        /// 业务模块（ResourceComponent / SceneComponent 等）应在自身 InitializeAsync 阶段
        /// 用此属性做先决条件检查，给出 actionable 错误，而不是等到 Run 时才崩。
        /// </summary>
        bool HasHelper { get; }

        /// <summary>
        /// 注入驱动器（通常是 Unity 层 UnityCoroutineHelper）。
        /// 替换已有驱动器时会先停止并清理旧驱动器上的在途协程，避免 cookie 跨驱动器混用。
        /// </summary>
        void SetHelper(ICoroutineHelper helper);

        /// <summary>
        /// 调度一个 IEnumerator。tag 用于调试/Profiler；owner 用于按对象批量停止（可为 null）。
        /// </summary>
        CoroutineHandle Run(IEnumerator routine, string tag, object owner = null);

        /// <summary>停止指定协程；已结束/无效 handle 返回 false。</summary>
        bool Stop(CoroutineHandle handle);

        /// <summary>停止指定 owner 持有的全部协程；返回停止数。</summary>
        int StopByOwner(object owner);

        /// <summary>停止指定 tag 的全部协程；返回停止数。</summary>
        int StopByTag(string tag);

        /// <summary>停止全部协程。</summary>
        int StopAll();

        /// <summary>获取所有在跑协程的只读快照（顺序不保证）。</summary>
        CoroutineSnapshot[] GetAllRunning();
    }

    /// <summary>
    /// 协程驱动器接口。Unity 层通过 MonoBehaviour.StartCoroutine/StopCoroutine 实现。
    /// 返回 cookie 由 manager 持有，用于精确停止。
    /// </summary>
    public interface ICoroutineHelper
    {
        object StartCoroutine(IEnumerator routine);
        void StopCoroutine(object cookie);
    }

    /// <summary>
    /// 可选的协程驱动器生命周期状态。未实现此接口的自定义 helper 默认视为可用，
    /// Unity helper 用它报告所持 MonoBehaviour 是否已被销毁。
    /// </summary>
    public interface ICoroutineHelperStatus
    {
        bool IsAvailable { get; }
    }

    /// <summary>
    /// 协程句柄。Id 由 manager 自增分配；0/默认值表示无效。
    /// </summary>
    public readonly struct CoroutineHandle : System.IEquatable<CoroutineHandle>
    {
        public readonly int Id;
        public CoroutineHandle(int id) { Id = id; }
        public bool IsValid { get { return Id > 0; } }
        public bool Equals(CoroutineHandle other) { return Id == other.Id; }
        public override bool Equals(object obj) { return obj is CoroutineHandle h && Equals(h); }
        public override int GetHashCode() { return Id; }
        public static bool operator ==(CoroutineHandle a, CoroutineHandle b) { return a.Id == b.Id; }
        public static bool operator !=(CoroutineHandle a, CoroutineHandle b) { return a.Id != b.Id; }
    }

    /// <summary>
    /// 协程快照（Debugger 渲染、统计用）。
    /// </summary>
    public sealed class CoroutineSnapshot
    {
        public int Id;
        public string Tag;
        public string OwnerName;
        public float ElapsedSeconds;
    }
}
