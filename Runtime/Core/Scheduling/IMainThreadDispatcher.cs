//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Scheduling
{
    /// <summary>
    /// 主线程派发器：把后台线程（网络收发、下载、文件 IO、Job 完成回调）的结果安全地交回主线程执行。
    /// 取代各模块各自手写的 <c>ConcurrentQueue&lt;Action&gt;</c> + Update drain。
    ///
    /// 两种投递方式：
    ///   • <see cref="Post(Action)"/> —— 最简单，调用方承担闭包分配；适合低频事件（连接成功/失败）。
    ///   • <see cref="Post(IMainThreadWork)"/> —— 零分配路径：工作项对象由调用方池化（IReference），
    ///     派发器执行完 <see cref="IMainThreadWork.Execute"/> 后调用 <see cref="ReferencePool.Release"/> 归还。
    ///     适合高频（每包一次的网络回调、每分片一次的下载进度）。
    ///
    /// 语义：
    ///   • FIFO；同一线程投递的顺序被保留。
    ///   • 每帧最多执行 <see cref="MaxDrainMilliseconds"/> 毫秒（默认不限）——防止一次性涌入的大批回调把一帧撑爆，
    ///     剩余的下一帧继续，顺序不变。
    ///   • 工作项抛出的异常被隔离记录，不影响后续项。
    ///   • 主线程投递也允许（会在下一次 Update 执行，而不是立即执行）：这让"总是异步"的语义保持一致。
    ///
    /// 线程契约：Post 任意线程；Update 主线程。
    /// </summary>
    public interface IMainThreadDispatcher
    {
        /// <summary>待执行工作项数量（近似值，跨线程读取）。</summary>
        int PendingCount { get; }

        /// <summary>每帧 drain 的毫秒上限；&lt;= 0 表示不限（默认）。</summary>
        float MaxDrainMilliseconds { get; set; }

        /// <summary>累计已执行的工作项数。</summary>
        long ExecutedCount { get; }

        /// <summary>累计执行时抛出异常的工作项数。</summary>
        long FailedCount { get; }

        /// <summary>当前线程是否为主线程（Framework.MarkMainThread 标记的线程）。</summary>
        bool IsMainThread { get; }

        /// <summary>投递一个委托到主线程。</summary>
        void Post(Action action);

        /// <summary>投递一个（通常池化的）工作项到主线程；执行后自动 ReferencePool.Release。</summary>
        void Post(IMainThreadWork work);
    }

    /// <summary>
    /// 池化的主线程工作项。实现类从 <see cref="ReferencePool"/> 取得、填充字段、<see cref="IMainThreadDispatcher.Post(IMainThreadWork)"/>；
    /// 派发器在主线程执行后负责归还。<see cref="IReference.Clear"/> 里清掉持有的引用。
    /// </summary>
    public interface IMainThreadWork : IReference
    {
        /// <summary>在主线程执行。</summary>
        void Execute();
    }
}
