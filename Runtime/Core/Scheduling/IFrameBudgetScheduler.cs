//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Scheduling
{
    /// <summary>
    /// 帧预算调度器：把"一次做完会卡帧"的大批量工作（区块流送的实例化、种群生成、批量反序列化、
    /// 大表重建索引）切成小步，在每帧固定的毫秒预算内推进。
    ///
    /// 模型：
    ///   • 任务实现 <see cref="IBudgetedTask.Step"/>，每次做一小片工作，返回 true 表示完成。步长由任务自己控制
    ///     （建议单步 ≤ 0.2 ms），调度器只负责"这帧还能不能再来一步"。
    ///   • 按优先级（数值小者先）执行；同优先级 FIFO。高优先级任务未完成时低优先级不会被推进——
    ///     这是有意的：流送中"玩家脚下的区块"必须压过"远处的装饰"。
    ///   • 每帧预算 <see cref="BudgetMilliseconds"/>；无论预算是否已被上一步耗尽，每帧至少推进 1 步（进度保证）。
    ///   • 任务抛出异常 = 任务失败并移除，不影响其它任务；<see cref="ScheduledTask.Status"/> 可查。
    ///   • 句柄带版本号：取消/完成后的旧句柄查询返回 <see cref="ScheduledTaskStatus.None"/>，不会误指向复用槽位。
    ///
    /// 与协程 / Timer 的分工：协程按帧/时间等待，Timer 按时间触发；本调度器按**时间预算**推进工作量，
    /// 是三者中唯一能保证"这帧最多花 X 毫秒"的。
    ///
    /// 线程契约：仅主线程。
    /// </summary>
    public interface IFrameBudgetScheduler
    {
        /// <summary>每帧可用于推进任务的毫秒预算。默认 2 ms。</summary>
        float BudgetMilliseconds { get; set; }

        /// <summary>待完成任务数（含进行中）。</summary>
        int PendingCount { get; }

        /// <summary>上一帧实际消耗的毫秒数。</summary>
        float LastFrameMilliseconds { get; }

        /// <summary>上一帧执行的步数。</summary>
        int LastFrameSteps { get; }

        /// <summary>累计完成的任务数。</summary>
        long CompletedCount { get; }

        /// <summary>累计失败（抛异常）的任务数。</summary>
        long FailedCount { get; }

        /// <summary>提交任务。优先级数值小者先执行。</summary>
        ScheduledTask Schedule(IBudgetedTask task, int priority = 0);

        /// <summary>取消任务。已完成/已取消/无效句柄返回 false。任务在取消时收到 <see cref="IBudgetedTask.OnCancelled"/>。</summary>
        bool Cancel(ScheduledTask handle);

        /// <summary>查询句柄状态。</summary>
        ScheduledTaskStatus GetStatus(ScheduledTask handle);

        /// <summary>
        /// 立即同步推进直到指定任务完成（无视预算）。用于"必须在这帧完成"的兜底（如玩家已经站在未加载的区块上）。
        /// 返回最终状态。
        /// </summary>
        ScheduledTaskStatus RunToCompletion(ScheduledTask handle);
    }

    /// <summary>可被帧预算调度的任务。</summary>
    public interface IBudgetedTask
    {
        /// <summary>推进一小步；返回 true 表示任务完成。</summary>
        bool Step();

        /// <summary>任务被取消时调用（未开始或进行中）；用于释放中间状态。</summary>
        void OnCancelled();
    }

    /// <summary>任务状态。</summary>
    public enum ScheduledTaskStatus
    {
        /// <summary>句柄无效或已被复用。</summary>
        None = 0,

        /// <summary>排队或进行中。</summary>
        Pending,

        /// <summary>已完成。</summary>
        Completed,

        /// <summary>已取消。</summary>
        Cancelled,

        /// <summary>Step 抛出异常。</summary>
        Failed,
    }

    /// <summary>任务句柄（值类型，含版本号）。</summary>
    public readonly struct ScheduledTask : IEquatable<ScheduledTask>
    {
        /// <summary>无效句柄。</summary>
        public static readonly ScheduledTask None = default(ScheduledTask);

        public readonly int Id;
        public readonly int Version;

        public ScheduledTask(int id, int version)
        {
            Id = id;
            Version = version;
        }

        /// <summary>是否为有效（非默认）句柄；不代表任务仍在进行，状态请查 <see cref="IFrameBudgetScheduler.GetStatus"/>。</summary>
        public bool IsValid
        {
            get { return Version != 0; }
        }

        public bool Equals(ScheduledTask other)
        {
            return Id == other.Id && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is ScheduledTask && Equals((ScheduledTask)obj);
        }

        public override int GetHashCode()
        {
            return Id * 397 ^ Version;
        }
    }
}
