//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Timer
{
    /// <summary>
    /// 定时器管理器（完整实现）。
    /// 驱动：在 Update(elapseSeconds, realElapseSeconds) 中推进所有定时器——
    ///   缩放定时器用 elapseSeconds（受 Time.timeScale 影响），
    ///   非缩放定时器用 realElapseSeconds（真实时间），
    ///   帧定时器每次 Update 递减 1 帧。
    /// 重入安全：定时器回调里可能新增/移除其它定时器（甚至自己），因此遍历时先把当前条目拷进可复用的快照列表，
    ///   再遍历快照，回调对 m_Timers 字典的增删不会破坏本帧迭代——镜像 NetworkManager/EntityManager 的快照模式。
    /// </summary>
    internal sealed class TimerManager : FrameworkModule, ITimerManager
    {
        private readonly Dictionary<int, TimerEntry> m_Timers = new Dictionary<int, TimerEntry>();
        // 复用的快照缓冲：Update 遍历快照而非字典本身，允许回调里增删定时器而不抛 InvalidOperationException。
        private TimerEntry[] m_Snapshot = Array.Empty<TimerEntry>();
        private int m_Serial;
        private bool m_Updating;

        // Priority 70：略低于 Coroutine（100），保证定时器在协程驱动之后、常规业务模块之前推进。
        public override int Priority { get { return 70; } }

        public int ActiveTimerCount { get { return m_Timers.Count; } }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_Timers.Count == 0) return;

            int count = Snapshot();
            m_Updating = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    TimerEntry entry = m_Snapshot[i];
                    if (entry == null || entry.Removed || entry.Paused) continue;
                    // 回调里可能已把它移除（RemoveTimer / RemoveAllTimers），跳过陈旧引用。
                    if (!m_Timers.ContainsKey(entry.Id)) continue;

                    TickEntry(entry, elapseSeconds, realElapseSeconds);
                }
            }
            finally
            {
                m_Updating = false;
                // 清空快照引用，避免悬挂持有已结束的回调委托。
                for (int i = 0; i < count; i++) m_Snapshot[i] = null;
            }
        }

        public override void Shutdown()
        {
            m_Timers.Clear();
            m_Snapshot = Array.Empty<TimerEntry>();
            m_Serial = 0;
            m_Updating = false;
        }

        // ===== 增加定时器 =====

        public int AddTimer(float delaySeconds, Action onComplete, bool useUnscaledTime = false)
        {
            Framework.EnsureMainThread(nameof(AddTimer));
            if (onComplete == null)
            {
                FrameworkLog.Warning("AddTimer: onComplete is null.");
                return 0;
            }

            int id = ++m_Serial;
            m_Timers.Add(id, new TimerEntry
            {
                Id = id,
                Kind = TimerKind.OneShot,
                Interval = delaySeconds < 0f ? 0f : delaySeconds,
                Remaining = delaySeconds < 0f ? 0f : delaySeconds,
                RepeatCount = 1,
                UseUnscaledTime = useUnscaledTime,
                Callback = onComplete,
            });
            return id;
        }

        public int AddRepeatingTimer(float intervalSeconds, Action onTick, int repeatCount = -1, bool useUnscaledTime = false)
        {
            Framework.EnsureMainThread(nameof(AddRepeatingTimer));
            if (onTick == null)
            {
                FrameworkLog.Warning("AddRepeatingTimer: onTick is null.");
                return 0;
            }
            if (intervalSeconds <= 0f)
            {
                FrameworkLog.Warning("AddRepeatingTimer: intervalSeconds must be > 0 (got {0}).", intervalSeconds);
                return 0;
            }
            if (repeatCount == 0)
            {
                FrameworkLog.Warning("AddRepeatingTimer: repeatCount of 0 never fires.");
                return 0;
            }

            int id = ++m_Serial;
            m_Timers.Add(id, new TimerEntry
            {
                Id = id,
                Kind = TimerKind.Repeating,
                Interval = intervalSeconds,
                Remaining = intervalSeconds,
                RepeatCount = repeatCount,
                UseUnscaledTime = useUnscaledTime,
                Callback = onTick,
            });
            return id;
        }

        public int AddFrameTimer(int frames, Action onComplete)
        {
            Framework.EnsureMainThread(nameof(AddFrameTimer));
            if (onComplete == null)
            {
                FrameworkLog.Warning("AddFrameTimer: onComplete is null.");
                return 0;
            }

            int id = ++m_Serial;
            m_Timers.Add(id, new TimerEntry
            {
                Id = id,
                Kind = TimerKind.Frame,
                RepeatCount = 1,
                FramesRemaining = frames < 0 ? 0 : frames,
                Callback = onComplete,
            });
            return id;
        }

        // ===== 取消 / 暂停 / 查询 =====

        public bool RemoveTimer(int timerId)
        {
            Framework.EnsureMainThread(nameof(RemoveTimer));
            TimerEntry entry;
            if (!m_Timers.TryGetValue(timerId, out entry)) return false;
            // 标记 Removed 让正在进行的本帧快照遍历跳过它；同时从字典移除。
            entry.Removed = true;
            m_Timers.Remove(timerId);
            return true;
        }

        public void PauseTimer(int timerId)
        {
            Framework.EnsureMainThread(nameof(PauseTimer));
            TimerEntry entry;
            if (m_Timers.TryGetValue(timerId, out entry)) entry.Paused = true;
        }

        public void ResumeTimer(int timerId)
        {
            Framework.EnsureMainThread(nameof(ResumeTimer));
            TimerEntry entry;
            if (m_Timers.TryGetValue(timerId, out entry)) entry.Paused = false;
        }

        public bool IsTimerActive(int timerId)
        {
            return m_Timers.ContainsKey(timerId);
        }

        public void RemoveAllTimers()
        {
            Framework.EnsureMainThread(nameof(RemoveAllTimers));
            // 标记所有条目 Removed，使正在进行的本帧快照遍历安全跳过它们。
            foreach (var kv in m_Timers) kv.Value.Removed = true;
            m_Timers.Clear();
        }

        // ===== 内部推进 =====

        private void TickEntry(TimerEntry entry, float elapseSeconds, float realElapseSeconds)
        {
            if (entry.Kind == TimerKind.Frame)
            {
                entry.FramesRemaining -= 1;
                if (entry.FramesRemaining > 0) return;
                FireAndAdvance(entry);
                return;
            }

            float delta = entry.UseUnscaledTime ? realElapseSeconds : elapseSeconds;
            entry.Remaining -= delta;
            // while 防止单帧 delta 跨越多个间隔时漏触发（高频小间隔重复定时器）。
            while (entry.Remaining <= 0f)
            {
                FireAndAdvance(entry);
                // 回调可能移除了自己，或它已结束（OneShot / 次数用尽）。
                if (entry.Removed || !m_Timers.ContainsKey(entry.Id)) return;
                if (entry.Kind == TimerKind.OneShot) return;
                entry.Remaining += entry.Interval;
            }
        }

        // 触发回调并推进剩余次数；次数用尽时从字典移除。
        private void FireAndAdvance(TimerEntry entry)
        {
            Action cb = entry.Callback;
            if (cb != null)
            {
                try { cb(); }
                catch (Exception ex) { FrameworkLog.Error("Timer {0} callback threw: {1}", entry.Id, ex); }
            }

            // 回调内可能已移除该定时器（RemoveTimer/RemoveAllTimers），不要再推进或重复移除。
            if (entry.Removed || !m_Timers.ContainsKey(entry.Id)) return;

            if (entry.RepeatCount > 0)
            {
                entry.RepeatCount -= 1;
                if (entry.RepeatCount <= 0)
                {
                    entry.Removed = true;
                    m_Timers.Remove(entry.Id);
                }
            }
            // RepeatCount < 0 表示无限重复，不递减、不移除。
        }

        // 把当前定时器拷进复用缓冲；返回数量。遍历快照避免迭代期字典被回调改动。
        private int Snapshot()
        {
            int n = m_Timers.Count;
            if (m_Snapshot.Length < n) m_Snapshot = new TimerEntry[Math.Max(8, n * 2)];
            int i = 0;
            foreach (var kv in m_Timers) m_Snapshot[i++] = kv.Value;
            return i;
        }

        // ===== 内部数据 =====

        private enum TimerKind
        {
            OneShot,
            Repeating,
            Frame,
        }

        private sealed class TimerEntry
        {
            public int Id;
            public TimerKind Kind;
            public float Interval;          // 缩放/非缩放定时器的触发间隔。
            public float Remaining;         // 距下次触发的剩余秒数。
            public int FramesRemaining;     // 帧定时器：距触发的剩余帧数。
            public int RepeatCount;         // 剩余触发次数（<0 = 无限）。
            public bool UseUnscaledTime;    // true 用真实时间推进。
            public bool Paused;             // 暂停态不推进、不触发。
            public bool Removed;            // 已取消标记，供本帧快照遍历跳过。
            public Action Callback;
        }
    }
}
