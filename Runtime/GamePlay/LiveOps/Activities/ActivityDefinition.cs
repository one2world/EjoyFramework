//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Activities
{
    /// <summary>
    /// 一个限时活动的定义。不可变值对象，定义后不应再修改。
    /// 通过 <see cref="Once"/>、<see cref="Daily"/>、<see cref="Weekly"/> 三个工厂方法构造。
    /// 所有时间均以 UTC 计算：
    /// <list type="bullet">
    /// <item>Once：使用绝对窗口 <see cref="StartEpochMs"/>/<see cref="EndEpochMs"/>（UTC epoch 毫秒）。</item>
    /// <item>Daily/Weekly：使用一天内的窗口，单位为自 UTC 零点起的秒数，范围 [0,86400)。
    /// 若 <see cref="DailyEndSec"/> 小于等于 <see cref="DailyStartSec"/>，视为跨午夜的环绕窗口。</item>
    /// <item>Weekly：额外以 <see cref="WeekdayMask"/> 位掩码限定生效的星期几，bit0=周日 .. bit6=周六。</item>
    /// </list>
    /// </summary>
    public sealed class ActivityDefinition
    {
        // 一天的秒数。
        private const int SecondsPerDay = 86400;

        // 七个星期位全部置位的掩码（bit0..bit6）。
        private const int FullWeekMask = 0x7F;

        private readonly string m_Id;
        private readonly ActivityRecurrence m_Recurrence;
        private readonly long m_StartEpochMs;
        private readonly long m_EndEpochMs;
        private readonly int m_DailyStartSec;
        private readonly int m_DailyEndSec;
        private readonly int m_WeekdayMask;
        private readonly object m_Payload;

        // 私有构造：通过工厂方法保证各重复类型的不变量。
        private ActivityDefinition(
            string id,
            ActivityRecurrence recurrence,
            long startEpochMs,
            long endEpochMs,
            int dailyStartSec,
            int dailyEndSec,
            int weekdayMask,
            object payload)
        {
            m_Id = id;
            m_Recurrence = recurrence;
            m_StartEpochMs = startEpochMs;
            m_EndEpochMs = endEpochMs;
            m_DailyStartSec = dailyStartSec;
            m_DailyEndSec = dailyEndSec;
            m_WeekdayMask = weekdayMask;
            m_Payload = payload;
        }

        /// <summary>
        /// 活动唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 重复（周期）类型。
        /// </summary>
        public ActivityRecurrence Recurrence
        {
            get { return m_Recurrence; }
        }

        /// <summary>
        /// Once：绝对开始时间（UTC epoch 毫秒，含）。Daily/Weekly 下无意义（为 0）。
        /// </summary>
        public long StartEpochMs
        {
            get { return m_StartEpochMs; }
        }

        /// <summary>
        /// Once：绝对结束时间（UTC epoch 毫秒，不含）。Daily/Weekly 下无意义（为 0）。
        /// </summary>
        public long EndEpochMs
        {
            get { return m_EndEpochMs; }
        }

        /// <summary>
        /// Daily/Weekly：每日窗口起点，自 UTC 零点起的秒数，范围 [0,86400)（含）。
        /// </summary>
        public int DailyStartSec
        {
            get { return m_DailyStartSec; }
        }

        /// <summary>
        /// Daily/Weekly：每日窗口终点，自 UTC 零点起的秒数，范围 [0,86400)（不含）。
        /// 若小于等于 <see cref="DailyStartSec"/>，则窗口跨午夜环绕（生效条件：sod &gt;= start 或 sod &lt; end）。
        /// </summary>
        public int DailyEndSec
        {
            get { return m_DailyEndSec; }
        }

        /// <summary>
        /// Weekly：生效星期几的位掩码，bit0=周日 .. bit6=周六。Daily 下恒为全 1（每天生效）。
        /// </summary>
        public int WeekdayMask
        {
            get { return m_WeekdayMask; }
        }

        /// <summary>
        /// 不透明的游戏侧载荷数据；框架不解释其内容。
        /// </summary>
        public object Payload
        {
            get { return m_Payload; }
        }

        /// <summary>
        /// 构造一个一次性活动：在 [startEpochMs, endEpochMs) 的绝对 UTC 窗口内开放。
        /// </summary>
        /// <param name="id">活动唯一标识，不可为空。</param>
        /// <param name="startEpochMs">绝对开始时间（UTC epoch 毫秒，含）。</param>
        /// <param name="endEpochMs">绝对结束时间（UTC epoch 毫秒，不含），必须大于 startEpochMs。</param>
        /// <param name="payload">不透明载荷数据，可为空。</param>
        /// <returns>不可变的活动定义。</returns>
        /// <exception cref="ArgumentException">id 为空，或 endEpochMs 不大于 startEpochMs。</exception>
        public static ActivityDefinition Once(string id, long startEpochMs, long endEpochMs, object payload = null)
        {
            RequireId(id);

            if (endEpochMs <= startEpochMs)
            {
                throw new ArgumentException(
                    $"一次性活动 {id} 的结束时间必须大于开始时间。", nameof(endEpochMs));
            }

            return new ActivityDefinition(
                id, ActivityRecurrence.Once, startEpochMs, endEpochMs, 0, 0, FullWeekMask, payload);
        }

        /// <summary>
        /// 构造一个每日活动：在每天（UTC）的 [startSec, endSec) 时间窗内开放。
        /// 若 endSec 小于等于 startSec，则窗口跨午夜环绕。
        /// </summary>
        /// <param name="id">活动唯一标识，不可为空。</param>
        /// <param name="startSec">每日窗口起点，自 UTC 零点起秒数，范围 [0,86400)。</param>
        /// <param name="endSec">每日窗口终点，自 UTC 零点起秒数，范围 [0,86400)。</param>
        /// <param name="payload">不透明载荷数据，可为空。</param>
        /// <returns>不可变的活动定义。</returns>
        /// <exception cref="ArgumentException">id 为空，或秒数不在 [0,86400) 范围内。</exception>
        public static ActivityDefinition Daily(string id, int startSec, int endSec, object payload = null)
        {
            RequireId(id);
            RequireSecondOfDay(startSec, nameof(startSec));
            RequireSecondOfDay(endSec, nameof(endSec));

            return new ActivityDefinition(
                id, ActivityRecurrence.Daily, 0, 0, startSec, endSec, FullWeekMask, payload);
        }

        /// <summary>
        /// 构造一个每周活动：仅在 weekdayMask 命中的星期几（UTC），于每天的 [startSec, endSec) 时间窗内开放。
        /// 若 endSec 小于等于 startSec，则每日窗口跨午夜环绕（环绕计入“起始那天”的星期掩码）。
        /// </summary>
        /// <param name="id">活动唯一标识，不可为空。</param>
        /// <param name="startSec">每日窗口起点，自 UTC 零点起秒数，范围 [0,86400)。</param>
        /// <param name="endSec">每日窗口终点，自 UTC 零点起秒数，范围 [0,86400)。</param>
        /// <param name="weekdayMask">星期位掩码，bit0=周日 .. bit6=周六。须命中至少一位有效星期。</param>
        /// <param name="payload">不透明载荷数据，可为空。</param>
        /// <returns>不可变的活动定义。</returns>
        /// <exception cref="ArgumentException">id 为空、秒数越界，或星期掩码未命中任何有效星期。</exception>
        public static ActivityDefinition Weekly(
            string id, int startSec, int endSec, int weekdayMask, object payload = null)
        {
            RequireId(id);
            RequireSecondOfDay(startSec, nameof(startSec));
            RequireSecondOfDay(endSec, nameof(endSec));

            if ((weekdayMask & FullWeekMask) == 0)
            {
                throw new ArgumentException(
                    $"每周活动 {id} 的星期掩码必须至少命中一个有效星期（bit0..bit6）。", nameof(weekdayMask));
            }

            return new ActivityDefinition(
                id, ActivityRecurrence.Weekly, 0, 0, startSec, endSec, weekdayMask & FullWeekMask, payload);
        }

        // 校验 id 非空。
        private static void RequireId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("活动定义的 id 不能为空。", nameof(id));
            }
        }

        // 校验秒数落在合法的一天范围 [0,86400)。
        private static void RequireSecondOfDay(int value, string paramName)
        {
            if (value < 0 || value >= SecondsPerDay)
            {
                throw new ArgumentException(
                    $"每日时间窗的秒数必须落在 [0,{SecondsPerDay}) 范围内，实际为 {value}。", paramName);
            }
        }
    }
}
