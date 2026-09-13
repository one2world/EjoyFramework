//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Activities
{
    /// <summary>
    /// 限时活动 / 事件排期表的对外访问接口。供 <see cref="Framework.GetModule{T}"/> 解析与调用方依赖，
    /// 屏蔽具体实现与时钟来源（实现可由 <see cref="ActivitySchedule"/> 提供并以注入的时钟驱动）。
    /// 所有时间语义均以 UTC 计算，详见各成员说明与 <see cref="ActivityRecurrence"/>。
    /// </summary>
    public interface IActivitySchedule
    {
        /// <summary>
        /// 注册一个活动定义。
        /// </summary>
        /// <param name="def">活动定义，不可为空。</param>
        /// <exception cref="ArgumentNullException">def 为 null。</exception>
        /// <exception cref="ArgumentException">活动 Id 已存在。</exception>
        void Define(ActivityDefinition def);

        /// <summary>
        /// 移除一个活动定义及其差分基线。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>确有该活动并被移除返回 true；否则返回 false。</returns>
        bool Remove(string id);

        /// <summary>
        /// 计算某活动在“当前 now”的状态。未注册的活动返回 <see cref="ActivityStatus.NotStarted"/>。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>活动状态。</returns>
        ActivityStatus GetStatus(string id);

        /// <summary>
        /// 判断某活动当前是否处于开放窗口内。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>处于 Active 返回 true。</returns>
        bool IsActive(string id);

        /// <summary>
        /// 距离“下一次开始”的毫秒数。
        /// 若当前已 Active，或未来不再有开始（Once 已 Ended、未注册），返回 &lt;= 0（恒为 0）。
        /// 边界：恰好处于开始时刻视为 Active，返回 0。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>距离下一次开始的毫秒数；无未来开始或已 Active 时为 0。</returns>
        long TimeUntilStartMs(string id);

        /// <summary>
        /// 距离“当前窗口结束”的毫秒数。若当前未 Active，返回 &lt;= 0（恒为 0）。
        /// 边界：窗口右端为开区间，结束时刻返回的是“到结束”的差值（&gt; 0）。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>距离当前窗口结束的毫秒数；未 Active 时为 0。</returns>
        long TimeUntilEndMs(string id);

        /// <summary>
        /// 当前处于 Active 的所有活动 Id，按注册顺序枚举（已快照，枚举期间可安全增删）。
        /// </summary>
        IEnumerable<string> ActiveActivityIds { get; }

        /// <summary>
        /// 在当前 now 重新计算所有活动的状态，并对自上次 <see cref="Refresh"/>（或 <see cref="Define"/>）以来
        /// 发生变化的活动触发 <see cref="OnStatusChanged"/>。回调参数：排期表、活动 Id、新状态。
        /// 触发前会先更新基线，确保回调内再次查询到的是一致的新状态。
        /// </summary>
        void Refresh();

        /// <summary>
        /// 当某活动状态在 <see cref="Refresh"/> 中相对上次发生变化时触发。
        /// 参数：排期表、活动 Id、变化后的新状态。
        /// </summary>
        event Action<IActivitySchedule, string, ActivityStatus> OnStatusChanged;
    }
}
