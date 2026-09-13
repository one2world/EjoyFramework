//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Sequencing
{
    /// <summary>
    /// 时间线导演（Director）：驱动一个播放头（playhead）在多条 <see cref="SequenceTrack"/> 上推进，
    /// 当播放头越过某个 <see cref="SequenceMarker"/> 时按时间顺序触发 <see cref="OnMarker"/>。
    /// 用于过场动画、技能 VFX 时序、引导编排等"在固定时间点触发事件"的场景。
    ///
    /// 纯逻辑、引擎无关：不依赖 UnityEngine，仅依赖 System.*，时间由外部 <see cref="Tick"/> 驱动。
    /// 单线程使用，非线程安全。
    ///
    /// 触发语义（关键约定）：
    /// - <see cref="Tick"/> 仅在 <see cref="IsPlaying"/> 为真时推进；推进量为 deltaTime * <see cref="Speed"/>。
    /// - 一次 Tick 把播放头从 previousTime 推进到 newTime，触发"区间内"的所有标记：
    ///   默认采用半开区间 (previousTime, newTime]（即 previousTime &lt; t &lt;= newTime）。
    /// - <b>t = 0 的标记</b>：由于播放头从 0 起步，半开区间 (0, newTime] 不含 0，会漏掉 t=0 的标记。
    ///   本实现的明确选择是：<b>在序列开始后的"第一次推进越过 0"时，包含 t=0 的标记</b>——
    ///   即仅对开始后的首个推进帧使用闭下界 [0, newTime]（0 &lt;= t &lt;= newTime），之后恢复为半开区间。
    ///   因此 t=0 的标记会在第一次 <see cref="Tick"/>（dt &gt; 0）时触发，且只触发一次；
    ///   <see cref="Play"/> 本身不触发任何标记（它只是把状态置为播放中）。
    /// - 同一时间点跨多条轨道的多个标记：按时间升序、相同时间按轨道添加顺序（稳定）依次触发。
    ///
    /// 边界 / 循环：
    /// - 到达或越过 <see cref="Duration"/> 时：
    ///   - 若 <see cref="Loop"/> 为真，则环绕（newTime - Duration）并继续，先触发尾段（到 Duration）再触发新一圈从 0 起的标记；
    ///     <b>不</b>触发 <see cref="OnComplete"/>。单次 Tick 仅处理一次环绕；若单帧 deltaTime 超过一整圈时长，超出整圈的部分会被跳过（见实现注释）。
    ///   - 若不循环，则把播放头钳制到 Duration、停止播放，并触发一次 <see cref="OnComplete"/>。
    /// - <see cref="Seek"/>：直接重定位播放头，<b>不触发任何标记</b>（仅用于跳转 / 回退预览）。
    /// - <see cref="Stop"/>：暂停并把 <see cref="Time"/> 重置为 0。
    /// </summary>
    public sealed class SequenceDirector
    {
        private readonly List<SequenceTrack> m_Tracks = new List<SequenceTrack>(4);
        private readonly HashSet<string> m_TrackIds = new HashSet<string>(StringComparer.Ordinal);

        // 复用的标记收集缓冲，避免每次 Tick 产生 GC。
        private readonly List<SequenceMarker> m_FireBuffer = new List<SequenceMarker>(32);

        private double m_Time;
        private bool m_IsPlaying;
        private float m_Speed = 1f;

        // 显式时长覆盖：&lt;0 表示未设置，回退为"所有轨道最大标记时间"。
        // 用于表达"时长长于最后一个标记"的时间线（例如 1 秒循环但标记只到 0.5）。
        private double m_ExplicitDuration = -1d;

        // 标识"序列开始后是否尚未推进过任何一帧"。
        // 用于实现 t=0 标记在首个推进帧用闭下界 [0, newTime] 的约定。
        // 初值为 true：构造后或 Stop 重置后的首个推进帧会纳入 t=0 标记。
        private bool m_PendingStartTick = true;

        /// <summary>
        /// 构造一个空的导演（无轨道、未播放、播放头位于 0）。
        /// </summary>
        public SequenceDirector()
        {
        }

        /// <summary>
        /// 当播放头越过某个标记时触发。参数为 (导演自身, 被越过的标记)。
        /// </summary>
        public event Action<SequenceDirector, SequenceMarker> OnMarker;

        /// <summary>
        /// 非循环播放到达末尾（Duration）时触发一次。循环模式下不触发。
        /// </summary>
        public event Action<SequenceDirector> OnComplete;

        /// <summary>
        /// 全序列时长（秒）。默认动态计算为"所有轨道所有标记时间的最大值"（无标记时为 0），
        /// 因此在 <see cref="AddTrack"/> 之后再向轨道追加标记也会反映出来。
        /// 也可<b>显式赋值</b>以表达时长长于最后一个标记的时间线（如 1 秒循环但标记只到 0.5）；
        /// 赋负值则恢复为自动计算。
        /// </summary>
        public double Duration
        {
            get
            {
                if (m_ExplicitDuration >= 0d)
                {
                    return m_ExplicitDuration;
                }

                double max = 0d;
                for (int i = 0; i < m_Tracks.Count; i++)
                {
                    double d = m_Tracks[i].Duration;
                    if (d > max)
                    {
                        max = d;
                    }
                }

                return max;
            }
            set
            {
                m_ExplicitDuration = value < 0d ? -1d : value;
            }
        }

        /// <summary>
        /// 当前播放头时间（秒）。
        /// </summary>
        public double Time => m_Time;

        /// <summary>
        /// 是否正在播放。
        /// </summary>
        public bool IsPlaying => m_IsPlaying;

        /// <summary>
        /// 是否循环播放。循环时到达末尾会环绕继续且不触发 <see cref="OnComplete"/>。
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// 播放速率（默认 1）。<see cref="Tick"/> 的实际推进量为 deltaTime * Speed。
        /// </summary>
        public float Speed
        {
            get => m_Speed;
            set => m_Speed = value;
        }

        /// <summary>
        /// 添加一条轨道。轨道 Id 必须唯一。
        /// </summary>
        /// <param name="track">轨道，不可为空。</param>
        /// <exception cref="ArgumentNullException">track 为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">轨道 Id 与已添加的轨道重复时抛出。</exception>
        public void AddTrack(SequenceTrack track)
        {
            if (track == null)
            {
                throw new ArgumentNullException(nameof(track));
            }

            if (!m_TrackIds.Add(track.Id))
            {
                throw new InvalidOperationException($"已存在 Id 为 \"{track.Id}\" 的轨道，不能重复添加。");
            }

            m_Tracks.Add(track);
        }

        /// <summary>
        /// 开始 / 继续播放。仅置位状态，不推进时间，也不触发任何标记。
        /// </summary>
        public void Play()
        {
            m_IsPlaying = true;
        }

        /// <summary>
        /// 暂停播放。保留当前播放头位置。
        /// </summary>
        public void Pause()
        {
            m_IsPlaying = false;
        }

        /// <summary>
        /// 停止播放：暂停并把播放头重置到 0。重置后下一次 <see cref="Tick"/> 会再次按
        /// t=0 约定处理（即 t=0 标记会在重新开始后的首个推进帧触发）。
        /// </summary>
        public void Stop()
        {
            m_IsPlaying = false;
            m_Time = 0d;
            m_PendingStartTick = true;
        }

        /// <summary>
        /// 直接把播放头跳转到指定时间，<b>不触发任何标记</b>（用于跳转 / 预览定位）。
        /// 跳转值会被钳制到 [0, Duration]。跳转后位于该点的标记不会被补发；
        /// 后续 <see cref="Tick"/> 只触发严格大于该点的标记。
        /// </summary>
        /// <param name="time">目标时间（秒）。</param>
        public void Seek(double time)
        {
            double duration = Duration;
            if (time < 0d)
            {
                time = 0d;
            }
            else if (time > duration)
            {
                time = duration;
            }

            m_Time = time;
            // 跳转后不再视作"开始后的首帧"，避免误把目标点处的 t 标记当作 t=0 补发。
            m_PendingStartTick = false;
        }

        /// <summary>
        /// 推进播放头并触发期间越过的标记。仅在 <see cref="IsPlaying"/> 时生效。
        /// </summary>
        /// <param name="deltaTime">原始时间增量（秒）；实际推进为 deltaTime * <see cref="Speed"/>。负值或非正值按不推进处理。</param>
        public void Tick(double deltaTime)
        {
            if (!m_IsPlaying)
            {
                return;
            }

            double advance = deltaTime * m_Speed;
            if (advance <= 0d)
            {
                return;
            }

            double previous = m_Time;
            double duration = Duration;
            // 是否对本帧使用闭下界（仅"开始后首个推进帧"为真，用于纳入 t=0 标记）。
            bool includeLowerBound = m_PendingStartTick;
            m_PendingStartTick = false;

            // 无任何标记 / 时长为 0：仍需处理播放头推进与完成 / 循环语义。
            if (duration <= 0d)
            {
                HandleZeroDuration(includeLowerBound);
                return;
            }

            double newTime = previous + advance;

            if (newTime < duration)
            {
                // 普通推进，未到末尾。
                CollectAndFire(previous, newTime, includeLowerBound);
                m_Time = newTime;
                return;
            }

            // 到达或越过末尾。
            if (!Loop)
            {
                // 非循环：触发到 Duration 为止的尾段标记，钳制、停止并完成。
                CollectAndFire(previous, duration, includeLowerBound);
                m_Time = duration;
                m_IsPlaying = false;
                OnComplete?.Invoke(this);
                return;
            }

            // 循环：先触发本圈尾段 (previous, Duration]，再环绕处理新一圈头段。
            // 仅处理单次环绕；若 advance 超过一整圈，超出部分被跳过。
            CollectAndFire(previous, duration, includeLowerBound);

            double wrapped = newTime - duration;
            // 防御：极大 deltaTime 仅保留一圈内的余量，避免落在 Duration 之外。
            if (wrapped >= duration)
            {
                wrapped %= duration;
            }

            // 新一圈从 0 起：这一段同样需要纳入 t=0 标记，故使用闭下界。
            CollectAndFire(0d, wrapped, includeLowerBound: true);
            m_Time = wrapped;
        }

        /// <summary>
        /// 时长为 0（无标记或所有标记在 0 时刻）时的推进处理。
        /// </summary>
        private void HandleZeroDuration(bool includeLowerBound)
        {
            // 时长为 0（仅有 t=0 标记或无标记）：开始后的首个推进帧仍应触发 t=0 标记。
            if (includeLowerBound)
            {
                CollectAndFire(0d, 0d, includeLowerBound: true);
            }

            // 之后按非循环立即完成、循环则原地停留。
            if (!Loop)
            {
                m_Time = 0d;
                m_IsPlaying = false;
                OnComplete?.Invoke(this);
            }
            else
            {
                m_Time = 0d;
            }
        }

        /// <summary>
        /// 收集 (lowerBound, upperBound] 或（首帧 / 环绕头段）[lowerBound, upperBound] 内的所有标记，
        /// 跨所有轨道合并、按时间升序（相同时间按轨道添加顺序稳定）触发 <see cref="OnMarker"/>。
        /// </summary>
        private void CollectAndFire(double lowerBound, double upperBound, bool includeLowerBound)
        {
            if (upperBound < lowerBound)
            {
                return;
            }

            m_FireBuffer.Clear();

            // 跨轨道收集；轨道内标记已按时间升序，外层按"轨道添加顺序"遍历，
            // 配合稳定排序即可得到"时间升序 + 相同时间按轨道顺序"的稳定次序。
            for (int t = 0; t < m_Tracks.Count; t++)
            {
                IReadOnlyList<SequenceMarker> markers = m_Tracks[t].Markers;
                for (int i = 0; i < markers.Count; i++)
                {
                    double mt = markers[i].Time;
                    bool lowerOk = includeLowerBound ? mt >= lowerBound : mt > lowerBound;
                    if (lowerOk && mt <= upperBound)
                    {
                        m_FireBuffer.Add(markers[i]);
                    }
                    else if (mt > upperBound)
                    {
                        // 轨道内已升序，后续只会更大，可提前跳出该轨道。
                        break;
                    }
                }
            }

            if (m_FireBuffer.Count == 0)
            {
                return;
            }

            StableSortByTime(m_FireBuffer);

            // 快照计数：触发回调期间不修改缓冲（缓冲为导演私有，回调不应访问它）。
            int count = m_FireBuffer.Count;
            for (int i = 0; i < count; i++)
            {
                OnMarker?.Invoke(this, m_FireBuffer[i]);
            }
        }

        /// <summary>
        /// 按 <see cref="SequenceMarker.Time"/> 升序稳定排序（插入排序，n 很小，稳定且无 GC）。
        /// 稳定性保证：相同时间的标记维持收集时的相对顺序（即轨道添加顺序）。
        /// </summary>
        private static void StableSortByTime(List<SequenceMarker> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                SequenceMarker key = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].Time > key.Time)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = key;
            }
        }
    }
}
