//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using Unity.Jobs;

namespace EjoyFramework.Core.Jobs.Unity
{
    /// <summary>
    /// Job 调度管理器接口。封装 Unity C# Job System 的常见样板，解决三类"不方便"：
    ///   1) <b>忘记 Complete / 主线程同步点卡顿</b> —— 用 <see cref="CompleteInLateUpdate"/> 在 Update 里调度、
    ///      框架在 LateUpdate 统一 Complete（"早调度、晚完成"），无需手动 Complete、也避免在调度点立即阻塞主线程。
    ///   2) <b>依赖句柄合并繁琐</b> —— <see cref="Combine(JobHandle, JobHandle)"/> 等便捷重载（params 版内部自管理 NativeArray）。
    ///   3) <b>等待 Job 完成</b> —— 协程 <see cref="WaitFor"/> / await <see cref="WaitForAsync"/>，不阻塞主线程。
    ///
    /// 通过 <see cref="Framework.GetModule{T}"/>（T = <see cref="IJobSchedulerManager"/>）获取。
    /// 容器生命周期（NativeArray 分配/释放）由 <see cref="JobScope"/> 配套处理。
    /// </summary>
    public interface IJobSchedulerManager
    {
        /// <summary>当前登记、待本帧 LateUpdate 完成的 Job 数量（诊断用）。</summary>
        int PendingCount { get; }

        /// <summary>
        /// 登记一个句柄，由框架在<b>本帧 LateUpdate</b> 统一 <c>Complete()</c>。
        /// 适合"Update 调度、当帧用不到结果、帧末需落定"的并行任务，免去手动 Complete 和过早的主线程阻塞。
        /// 句柄默认值（default）视为已完成，直接忽略。
        /// </summary>
        void CompleteInLateUpdate(JobHandle handle);

        /// <summary>立即完成并清空所有已登记的延迟句柄（如在切场景/存档等同步点前主动收口）。</summary>
        void CompleteAll();

        /// <summary>合并两个依赖句柄。</summary>
        JobHandle Combine(JobHandle a, JobHandle b);

        /// <summary>合并三个依赖句柄。</summary>
        JobHandle Combine(JobHandle a, JobHandle b, JobHandle c);

        /// <summary>合并任意数量依赖句柄（内部用 <see cref="Unity.Collections.Allocator.Temp"/> NativeArray 自管理）。</summary>
        JobHandle Combine(params JobHandle[] handles);

        /// <summary>
        /// 协程等待句柄完成：逐帧 yield 直到 <see cref="JobHandle.IsCompleted"/>，随后调用 <c>Complete()</c> 落定。
        /// 不在调度帧阻塞主线程。
        /// </summary>
        IEnumerator WaitFor(JobHandle handle);

#if UNITY_2023_1_OR_NEWER
        /// <summary>
        /// await 风格等待句柄完成（逐帧轮询 <see cref="JobHandle.IsCompleted"/>，完成后 <c>Complete()</c>）。
        /// 支持 <see cref="System.Threading.CancellationToken"/>（取消仅停止等待，不中断已调度的 Job）。
        /// </summary>
        UnityEngine.Awaitable WaitForAsync(JobHandle handle, System.Threading.CancellationToken cancellationToken = default);
#endif
    }
}
