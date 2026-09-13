//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 有序 / 并行组合的时间线。
    ///
    /// 模型：维护一组 (startTime, tween) 条目与一个游标 m_Cursor（当前时间线末端）。
    /// - <see cref="Append"/>：将子 Tween 放在游标处，游标推进该子项时长；
    /// - <see cref="Join"/>：将子 Tween 放在 "上一个被 Append 的元素" 的起始时间处（与其并行），
    ///   并把游标推进到二者较大的结束时间；
    /// - <see cref="AppendInterval"/>：在游标处插入一段空白延时；
    /// - <see cref="AppendCallback"/>：在游标处插入一个零时长回调槽。
    ///
    /// Sequence 自身是一个 <see cref="Tween"/>，其 Duration 等于整条时间线长度（所有子项结束时间的最大值）。
    /// 复用基类的延迟 / 循环 / 完成机制：基类按时间线长度推进归一化进度，Sequence 在
    /// <see cref="ApplyProgress"/> 中把进度映射回本地时间，再据此驱动各子项。
    /// 默认使用 Linear，保证时间线不被缓动曲线扭曲（仍可显式 SetEase 改变整体节奏）。
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class Sequence : Tween
    {
        /// <summary>
        /// 时间线上的一个条目。Tween 为空表示这是一个纯延时（AppendInterval）。
        /// Callback 非空表示这是一个零时长回调槽（AppendCallback）。
        /// </summary>
        private struct Entry
        {
            public float StartTime;
            public float Duration;
            public Tween Tween;
            public Action Callback;
            public float Advanced;     // 该子 Tween 本序列内已推进的本地时间（用于计算每帧增量）。
            public bool CallbackFired; // 回调槽是否已触发。
        }

        private readonly List<Entry> m_Entries = new List<Entry>(16);

        private float m_Cursor;             // 当前时间线末端（下一个 Append 的起点）。
        private float m_LastAppendStart;    // 上一个 Append 元素的起始时间（供 Join 使用）。
        private bool m_HasAppended;         // 是否已有过 Append（决定 Join 的落点）。
        private float m_LocalTime;          // 当前已驱动到的本地时间。

        /// <summary>
        /// 构造一条空时间线（时长为 0，可逐步追加子项）。
        /// </summary>
        public Sequence()
        {
            Initialize(0f);
        }

        /// <summary>
        /// 在时间线末端追加一个子 Tween，使其在前序内容结束后开始。
        /// </summary>
        /// <param name="tween">子 Tween，不可为空。</param>
        /// <returns>自身，便于链式调用。</returns>
        /// <exception cref="ArgumentNullException">tween 为空时抛出。</exception>
        public Sequence Append(Tween tween)
        {
            if (tween == null)
            {
                throw new ArgumentNullException(nameof(tween));
            }

            float start = m_Cursor;
            float dur = tween.Duration;
            AddEntry(new Entry { StartTime = start, Duration = dur, Tween = tween });

            m_LastAppendStart = start;
            m_HasAppended = true;
            m_Cursor = start + dur;
            RecalculateDuration();
            return this;
        }

        /// <summary>
        /// 将子 Tween 并入 "上一个被 Append 的元素" 的起始时间处，使二者并行。
        /// 若此前没有任何 Append，则等同于在时间线起点（0）处放置。
        /// </summary>
        /// <param name="tween">子 Tween，不可为空。</param>
        /// <returns>自身，便于链式调用。</returns>
        /// <exception cref="ArgumentNullException">tween 为空时抛出。</exception>
        public Sequence Join(Tween tween)
        {
            if (tween == null)
            {
                throw new ArgumentNullException(nameof(tween));
            }

            float start = m_HasAppended ? m_LastAppendStart : 0f;
            float dur = tween.Duration;
            AddEntry(new Entry { StartTime = start, Duration = dur, Tween = tween });

            float end = start + dur;
            if (end > m_Cursor)
            {
                m_Cursor = end;
            }
            RecalculateDuration();
            return this;
        }

        /// <summary>
        /// 在时间线末端追加一段空白延时。
        /// </summary>
        /// <param name="seconds">延时秒数，负值按 0 处理。</param>
        /// <returns>自身，便于链式调用。</returns>
        public Sequence AppendInterval(float seconds)
        {
            if (seconds < 0f)
            {
                seconds = 0f;
            }

            float start = m_Cursor;
            AddEntry(new Entry { StartTime = start, Duration = seconds, Tween = null });

            m_LastAppendStart = start;
            m_HasAppended = true;
            m_Cursor = start + seconds;
            RecalculateDuration();
            return this;
        }

        /// <summary>
        /// 在时间线末端插入一个零时长回调槽，当时间线推进越过该点时触发一次。
        /// </summary>
        /// <param name="callback">回调，不可为空。</param>
        /// <returns>自身，便于链式调用。</returns>
        /// <exception cref="ArgumentNullException">callback 为空时抛出。</exception>
        public Sequence AppendCallback(Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            float start = m_Cursor;
            AddEntry(new Entry { StartTime = start, Duration = 0f, Tween = null, Callback = callback });

            m_LastAppendStart = start;
            m_HasAppended = true;
            // 零时长不推进游标。
            RecalculateDuration();
            return this;
        }

        /// <summary>
        /// 将本地时间推进到 targetLocalTime，并据此驱动各子项与回调。
        /// 由基类按归一化进度调用：targetLocalTime = easedProgress * Duration。
        /// </summary>
        protected override void ApplyProgress(float easedProgress)
        {
            float total = Duration;
            float targetLocalTime = total <= 0f ? 0f : easedProgress * total;
            DriveTo(targetLocalTime);
        }

        /// <summary>
        /// Kill(complete:true) 时把整条时间线快照到末端。
        /// </summary>
        protected override void ApplySnapToEnd()
        {
            DriveTo(Duration);
        }

        /// <summary>
        /// 序列自身循环时复位整条时间线：本地时间归零、清空各条目的已推进量与回调触发标记，并 Rewind
        /// 每个子 Tween，使下一轮循环能从头重新驱动。否则 DriveTo 的单调时间线会把第二轮目标时间钳回
        /// Duration（targetLocalTime &lt; m_LocalTime 时被钳住），导致 SetLoops 对 Sequence 静默失效。
        /// </summary>
        protected override void OnLoopRestart()
        {
            m_LocalTime = 0f;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry entry = m_Entries[i];
                entry.Advanced = 0f;
                entry.CallbackFired = false;
                m_Entries[i] = entry;
                entry.Tween?.RewindForReplay();
            }
        }

        /// <summary>
        /// 将所有子项与回调推进到指定的本地时间点。
        /// 对每个子 Tween：计算它在本时间点应达到的本地进度，与已推进量相减得到本帧增量，
        /// 再调用其 Tick；这样子项自身的 OnUpdate / OnComplete 仍按其语义触发。
        /// </summary>
        private void DriveTo(float targetLocalTime)
        {
            if (targetLocalTime < m_LocalTime)
            {
                targetLocalTime = m_LocalTime; // 时间线只前进，不回退。
            }

            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry entry = m_Entries[i];

                // 纯回调槽：越过其起点即触发一次。
                if (entry.Tween == null && entry.Callback != null)
                {
                    if (!entry.CallbackFired && targetLocalTime >= entry.StartTime)
                    {
                        entry.CallbackFired = true;
                        m_Entries[i] = entry;
                        entry.Callback();
                    }
                    continue;
                }

                // 纯延时槽：无需驱动。
                if (entry.Tween == null)
                {
                    continue;
                }

                // 该子 Tween 在本时间点应推进到的本地量（裁剪到 [0, duration]）。
                float local = targetLocalTime - entry.StartTime;
                if (local < 0f)
                {
                    continue; // 尚未到达该子项起点。
                }
                if (local > entry.Duration)
                {
                    local = entry.Duration;
                }

                float delta = local - entry.Advanced;
                if (delta > 0f || (entry.Duration <= 0f && entry.Advanced <= 0f && targetLocalTime >= entry.StartTime))
                {
                    entry.Tween.Tick(delta);
                    entry.Advanced = local;
                    m_Entries[i] = entry;
                }
            }

            m_LocalTime = targetLocalTime;
        }

        private void AddEntry(Entry entry)
        {
            entry.Advanced = 0f;
            entry.CallbackFired = false;
            m_Entries.Add(entry);
        }

        /// <summary>
        /// 重新计算时间线总时长（所有条目结束时间的最大值）。
        /// </summary>
        private void RecalculateDuration()
        {
            float max = 0f;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                float end = m_Entries[i].StartTime + m_Entries[i].Duration;
                if (end > max)
                {
                    max = end;
                }
            }
            Initialize(max);
        }
    }
}
