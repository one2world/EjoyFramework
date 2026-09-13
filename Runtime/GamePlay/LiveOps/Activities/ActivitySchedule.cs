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
    /// 限时活动 / 事件排期表。纯逻辑、与引擎无关，可独立单元测试，同时作为框架模块经
    /// <see cref="Framework.GetModule{T}"/> 暴露（接口 <see cref="IActivitySchedule"/>）。
    /// 时钟由外部以 <see cref="Func{T}"/>（返回 UTC epoch 毫秒）注入，因此完全可控、可测；
    /// 无参构造默认使用实时挂钟（UTC），可经 <see cref="SetClock"/> 注入确定性时钟以便复现与测试。
    ///
    /// 语义（均以 UTC 计算）：
    /// <list type="bullet">
    /// <item><b>Once</b>：当 StartEpochMs &lt;= now &lt; EndEpochMs 时 Active；之前 NotStarted；之后 Ended。</item>
    /// <item><b>Daily</b>：由 now 取“当日秒数” sod = (now/1000) mod 86400。当 sod 落在 [DailyStartSec, DailyEndSec) 时 Active；
    /// 若 DailyEndSec &lt;= DailyStartSec，则窗口跨午夜（sod &gt;= start 或 sod &lt; end 时 Active）。Daily 永不 Ended——非 Active 即 NotStarted（报告下一次开始）。</item>
    /// <item><b>Weekly</b>：与 Daily 相同，但仅在星期掩码命中的那天生效。星期由 epoch 天数推得（1970-01-01 为周四=4）。</item>
    /// </list>
    /// </summary>
    public sealed class ActivitySchedule : FrameworkModule, IActivitySchedule
    {
        // 一天/一周的常量。
        private const long MsPerSecond = 1000L;
        private const long SecondsPerDay = 86400L;
        private const long MsPerDay = SecondsPerDay * MsPerSecond;
        private const int DaysPerWeek = 7;

        // 1970-01-01（epoch 第 0 天）为星期四，编号 4（0=周日 .. 6=周六）。
        private const int Epoch0Weekday = 4;

        // 时钟提供者：返回当前 UTC epoch 毫秒。无参构造默认实时挂钟，可经 SetClock 注入确定性时钟。
        private Func<long> m_NowProvider;

        // 已注册活动定义，键为活动 Id；以 m_Order 维护稳定枚举顺序。
        private readonly Dictionary<string, ActivityDefinition> m_Definitions;

        // 注册顺序，用于稳定枚举（ActiveActivityIds 与 Refresh 遍历）。
        private readonly List<string> m_Order;

        // 上一次对外报告过的状态，供 Refresh 做差分以触发 OnStatusChanged。
        private readonly Dictionary<string, ActivityStatus> m_LastStatus;

        /// <summary>
        /// 无参构造，供 <c>Framework.GetModule&lt;IActivitySchedule&gt;()</c> 通过 <see cref="System.Activator"/> 创建。
        /// 默认使用实时挂钟（UTC epoch 毫秒）；可经 <see cref="SetClock"/> 注入确定性时钟以便复现与测试。
        /// </summary>
        public ActivitySchedule()
            : this(null)
        {
        }

        /// <summary>
        /// 以指定时钟构造排期表。保留作为内部便捷构造（测试与样例直接 new），框架解析模块走无参构造。
        /// </summary>
        /// <param name="nowEpochMsProvider">时钟提供者，返回当前 UTC epoch 毫秒；为 null 时回退到实时挂钟。</param>
        internal ActivitySchedule(Func<long> nowEpochMsProvider)
        {
            m_NowProvider = nowEpochMsProvider ?? RealtimeUtcEpochMs;
            m_Definitions = new Dictionary<string, ActivityDefinition>();
            m_Order = new List<string>();
            m_LastStatus = new Dictionary<string, ActivityStatus>();
        }

        /// <summary>
        /// 注入时钟提供者（返回当前 UTC epoch 毫秒）。传 null 时回退到实时挂钟。
        /// 沿用核心模块 SetLoader/SetHelper 的注入惯例，便于 <c>GetModule</c> 拿到实例后做配置或在测试中确定性化。
        /// </summary>
        /// <param name="nowEpochMsProvider">时钟提供者，返回当前 UTC epoch 毫秒；为 null 时使用实时挂钟。</param>
        public void SetClock(Func<long> nowEpochMsProvider)
        {
            m_NowProvider = nowEpochMsProvider ?? RealtimeUtcEpochMs;
        }

        /// <summary>
        /// 注册一个活动定义。
        /// </summary>
        /// <param name="def">活动定义，不可为空。</param>
        /// <exception cref="ArgumentNullException">def 为 null。</exception>
        /// <exception cref="ArgumentException">活动 Id 已存在。</exception>
        public void Define(ActivityDefinition def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            if (m_Definitions.ContainsKey(def.Id))
            {
                throw new ArgumentException($"活动 Id 已存在：{def.Id}", nameof(def));
            }

            m_Definitions.Add(def.Id, def);
            m_Order.Add(def.Id);
            // 以当前时刻初始化“上次状态”，使后续 Refresh 仅在状态真正变化时触发回调。
            m_LastStatus[def.Id] = ComputeStatus(def, Now());
        }

        /// <summary>
        /// 移除一个活动定义及其差分基线。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>确有该活动并被移除返回 true；否则返回 false。</returns>
        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id) || !m_Definitions.Remove(id))
            {
                return false;
            }

            m_Order.Remove(id);
            m_LastStatus.Remove(id);
            return true;
        }

        /// <summary>
        /// 计算某活动在“当前 now”的状态。未注册的活动返回 <see cref="ActivityStatus.NotStarted"/>。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>活动状态。</returns>
        public ActivityStatus GetStatus(string id)
        {
            if (!TryGet(id, out ActivityDefinition def))
            {
                return ActivityStatus.NotStarted;
            }

            return ComputeStatus(def, Now());
        }

        /// <summary>
        /// 判断某活动当前是否处于开放窗口内。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>处于 Active 返回 true。</returns>
        public bool IsActive(string id)
        {
            return GetStatus(id) == ActivityStatus.Active;
        }

        /// <summary>
        /// 距离“下一次开始”的毫秒数。
        /// 若当前已 Active，或未来不再有开始（Once 已 Ended、未注册），返回 &lt;= 0（恒为 0）。
        /// 边界：恰好处于开始时刻视为 Active，返回 0。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>距离下一次开始的毫秒数；无未来开始或已 Active 时为 0。</returns>
        public long TimeUntilStartMs(string id)
        {
            if (!TryGet(id, out ActivityDefinition def))
            {
                return 0L;
            }

            long now = Now();
            if (ComputeStatus(def, now) == ActivityStatus.Active)
            {
                return 0L;
            }

            long nextStart = NextStartEpochMs(def, now);
            if (nextStart < 0L)
            {
                // 未来不再有开始（Once 已结束）。
                return 0L;
            }

            long delta = nextStart - now;
            return delta > 0L ? delta : 0L;
        }

        /// <summary>
        /// 距离“当前窗口结束”的毫秒数。若当前未 Active，返回 &lt;= 0（恒为 0）。
        /// 边界：窗口右端为开区间，结束时刻返回的是“到结束”的差值（&gt; 0）。
        /// </summary>
        /// <param name="id">活动 Id。</param>
        /// <returns>距离当前窗口结束的毫秒数；未 Active 时为 0。</returns>
        public long TimeUntilEndMs(string id)
        {
            if (!TryGet(id, out ActivityDefinition def))
            {
                return 0L;
            }

            long now = Now();
            if (ComputeStatus(def, now) != ActivityStatus.Active)
            {
                return 0L;
            }

            long end = CurrentWindowEndEpochMs(def, now);
            long delta = end - now;
            return delta > 0L ? delta : 0L;
        }

        /// <summary>
        /// 当前处于 Active 的所有活动 Id，按注册顺序枚举（已快照，枚举期间可安全增删）。
        /// </summary>
        public IEnumerable<string> ActiveActivityIds
        {
            get
            {
                long now = Now();
                // 先快照成列表，避免调用方在 foreach 中 Define/Remove 触发字典枚举失效。
                List<string> result = new List<string>();
                for (int i = 0; i < m_Order.Count; i++)
                {
                    string id = m_Order[i];
                    if (ComputeStatus(m_Definitions[id], now) == ActivityStatus.Active)
                    {
                        result.Add(id);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 在当前 now 重新计算所有活动的状态，并对自上次 <see cref="Refresh"/>（或 <see cref="Define"/>）以来
        /// 发生变化的活动触发 <see cref="OnStatusChanged"/>。回调参数：排期表、活动 Id、新状态。
        /// 触发前会先更新基线，确保回调内再次查询到的是一致的新状态。
        /// </summary>
        public void Refresh()
        {
            long now = Now();

            // 先快照 Id，避免回调内 Define/Remove 改动集合导致枚举失效。
            string[] ids = m_Order.ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (!m_Definitions.TryGetValue(id, out ActivityDefinition def))
                {
                    // 可能已在上一轮回调中被移除。
                    continue;
                }

                ActivityStatus current = ComputeStatus(def, now);
                ActivityStatus previous = m_LastStatus.TryGetValue(id, out ActivityStatus p)
                    ? p
                    : current;

                if (current != previous)
                {
                    m_LastStatus[id] = current;
                    OnStatusChanged?.Invoke(this, id, current);
                }
                else
                {
                    m_LastStatus[id] = current;
                }
            }
        }

        /// <summary>
        /// 当某活动状态在 <see cref="Refresh"/> 中相对上次发生变化时触发。
        /// 参数：排期表、活动 Id、变化后的新状态。
        /// </summary>
        public event Action<IActivitySchedule, string, ActivityStatus> OnStatusChanged;

        /// <summary>
        /// 获取游戏框架模块优先级。排期表为纯数据计算，使用默认优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 游戏框架模块轮询。每帧基于当前时钟刷新活动状态并按需触发 <see cref="OnStatusChanged"/>。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            Refresh();
        }

        /// <summary>
        /// 关闭并清理游戏框架模块，移除全部已注册活动、差分基线与回调订阅。
        /// </summary>
        public override void Shutdown()
        {
            m_Definitions.Clear();
            m_Order.Clear();
            m_LastStatus.Clear();
            OnStatusChanged = null;
        }

        // 读取当前时间。
        private long Now()
        {
            return m_NowProvider();
        }

        // 实时挂钟：当前 UTC epoch 毫秒。作为无参构造与 SetClock(null) 的默认时钟。
        private static long RealtimeUtcEpochMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // 查找已注册定义。
        private bool TryGet(string id, out ActivityDefinition def)
        {
            if (string.IsNullOrEmpty(id))
            {
                def = null;
                return false;
            }

            return m_Definitions.TryGetValue(id, out def);
        }

        // 计算给定时刻的状态。
        private static ActivityStatus ComputeStatus(ActivityDefinition def, long now)
        {
            switch (def.Recurrence)
            {
                case ActivityRecurrence.Once:
                    if (now < def.StartEpochMs)
                    {
                        return ActivityStatus.NotStarted;
                    }

                    return now < def.EndEpochMs ? ActivityStatus.Active : ActivityStatus.Ended;

                case ActivityRecurrence.Daily:
                    return InDailyWindow(def, now) ? ActivityStatus.Active : ActivityStatus.NotStarted;

                case ActivityRecurrence.Weekly:
                    return InWeeklyWindow(def, now) ? ActivityStatus.Active : ActivityStatus.NotStarted;

                default:
                    return ActivityStatus.NotStarted;
            }
        }

        // 判断 now 是否落在每日窗口（含跨午夜环绕）内。
        private static bool InDailyWindow(ActivityDefinition def, long now)
        {
            int sod = SecondOfDay(now);
            return SecondInWindow(sod, def.DailyStartSec, def.DailyEndSec);
        }

        // 判断 now 是否落在每周窗口（命中星期 + 每日窗口，含跨午夜环绕）内。
        private static bool InWeeklyWindow(ActivityDefinition def, long now)
        {
            int sod = SecondOfDay(now);
            int start = def.DailyStartSec;
            int end = def.DailyEndSec;
            bool wraps = end <= start;

            if (!wraps)
            {
                // 普通窗口：当天命中星期且 sod ∈ [start,end)。
                return WeekdaySet(def, WeekdayOf(now)) && sod >= start && sod < end;
            }

            // 跨午夜窗口拆为两段，分别归属不同自然日的星期：
            //  段 A：今天 [start,86400) —— 归属“今天”的星期。
            //  段 B：今天 [0,end)       —— 该段实际属于“前一天”开启的窗口，归属“昨天”的星期。
            if (sod >= start)
            {
                return WeekdaySet(def, WeekdayOf(now));
            }

            if (sod < end)
            {
                return WeekdaySet(def, WeekdayOf(now - MsPerDay));
            }

            return false;
        }

        // 通用每日窗口判定（不含星期），支持跨午夜环绕。
        private static bool SecondInWindow(int sod, int start, int end)
        {
            if (end > start)
            {
                return sod >= start && sod < end;
            }

            // end <= start：跨午夜环绕（sod >= start 或 sod < end）。
            return sod >= start || sod < end;
        }

        // 取 now 的当日秒数 [0,86400)。
        private static int SecondOfDay(long now)
        {
            long sod = FloorMod(now, MsPerDay) / MsPerSecond;
            return (int)sod;
        }

        // 取 now 的星期（0=周日 .. 6=周六），基于 epoch 天数推算，处理负时间。
        private static int WeekdayOf(long now)
        {
            long days = FloorDiv(now, MsPerDay);
            int wd = (int)FloorMod(days + Epoch0Weekday, DaysPerWeek);
            return wd;
        }

        // 判断某星期是否在掩码内。
        private static bool WeekdaySet(ActivityDefinition def, int weekday)
        {
            return (def.WeekdayMask & (1 << weekday)) != 0;
        }

        // 计算下一次开始的 epoch 毫秒；若未来不再有开始返回 -1。now 已确保非 Active。
        private static long NextStartEpochMs(ActivityDefinition def, long now)
        {
            switch (def.Recurrence)
            {
                case ActivityRecurrence.Once:
                    // 仅当尚未开始时才有未来开始；已结束则无。
                    return now < def.StartEpochMs ? def.StartEpochMs : -1L;

                case ActivityRecurrence.Daily:
                    return NextDailyStart(def, now, requireWeekday: false);

                case ActivityRecurrence.Weekly:
                    return NextDailyStart(def, now, requireWeekday: true);

                default:
                    return -1L;
            }
        }

        // 计算 Daily/Weekly 下一次窗口开始时刻（now 已非 Active）。
        // 自“今天的窗口起点”开始，逐日向后扫描至命中（Weekly 需星期命中）。
        private static long NextDailyStart(ActivityDefinition def, long now, bool requireWeekday)
        {
            long dayStartMs = FloorDiv(now, MsPerDay) * MsPerDay; // 当日 UTC 零点。
            long startOffsetMs = def.DailyStartSec * MsPerSecond;

            // 至多扫描 8 天即可覆盖一周内任一命中日（含“今天稍后”的情形）。
            for (int i = 0; i <= DaysPerWeek; i++)
            {
                long candidate = dayStartMs + i * MsPerDay + startOffsetMs;
                if (candidate <= now)
                {
                    continue; // 该候选已过或恰为当前（当前非 Active 时不取等点）。
                }

                if (!requireWeekday || WeekdaySet(def, WeekdayOf(candidate)))
                {
                    return candidate;
                }
            }

            return -1L;
        }

        // 计算当前窗口的结束 epoch 毫秒。仅在 def 于 now 处 Active 时调用。
        private static long CurrentWindowEndEpochMs(ActivityDefinition def, long now)
        {
            if (def.Recurrence == ActivityRecurrence.Once)
            {
                return def.EndEpochMs;
            }

            // Daily/Weekly：定位包含 now 的窗口的结束时刻（绝对 epoch 毫秒）。
            int sod = SecondOfDay(now);
            long dayStartMs = FloorDiv(now, MsPerDay) * MsPerDay; // 当日 UTC 零点。
            int start = def.DailyStartSec;
            int end = def.DailyEndSec;
            long endOffsetMs = end * MsPerSecond;

            if (end > start)
            {
                // 普通窗口：结束于“今天的 end”。
                return dayStartMs + endOffsetMs;
            }

            // 跨午夜窗口：
            //  若 sod >= start，则窗口在“明天的 end”结束。
            //  若 sod < end，则窗口在“今天的 end”结束（属于昨天开启的窗口）。
            if (sod >= start)
            {
                return dayStartMs + MsPerDay + endOffsetMs;
            }

            return dayStartMs + endOffsetMs;
        }

        // 向下取整除法（朝负无穷），用于在负 epoch 下正确取“天”。
        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0)))
            {
                q--;
            }

            return q;
        }

        // 向下取整取模，结果与除数同号，落在 [0,b)（b>0）。
        private static long FloorMod(long a, long b)
        {
            long r = a % b;
            if (r != 0 && ((r < 0) != (b < 0)))
            {
                r += b;
            }

            return r;
        }
    }
}
