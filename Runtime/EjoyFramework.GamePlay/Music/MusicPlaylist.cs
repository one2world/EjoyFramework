//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Music
{
    /// <summary>
    /// 播放列表推进模式。
    /// </summary>
    public enum PlaylistMode
    {
        /// <summary>顺序播放，播放到末尾后停止（不回绕）。</summary>
        Sequential,

        /// <summary>循环播放，播放到末尾后回到开头。</summary>
        Loop,

        /// <summary>随机播放，每次随机选取下一首（曲目数 &gt; 1 时避免紧邻重复同一首）。</summary>
        Shuffle,
    }

    /// <summary>
    /// 音乐播放列表（纯逻辑，引擎无关）。持有一组有序的曲目标识（不透明字符串，如资源名 / Key）。
    /// 按 <see cref="Mode"/> 推进：
    /// <list type="bullet">
    /// <item><description>Sequential：到末尾后停止，<see cref="Current"/> 返回 null，<see cref="Next"/> 也返回 null。</description></item>
    /// <item><description>Loop：到末尾后回绕到开头。</description></item>
    /// <item><description>Shuffle：随机选取下一首；曲目数 &gt; 1 时避免与当前曲目紧邻重复。</description></item>
    /// </list>
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class MusicPlaylist
    {
        private readonly List<string> m_Tracks = new List<string>();

        // 当前曲目索引；-1 表示尚未开始或已越过末尾（Sequential 终止态）。
        private int m_CurrentIndex = -1;

        // Shuffle 内部随机源（未注入 randomPick 时使用）。
        private Random m_Random;

        /// <summary>
        /// 构造播放列表。
        /// </summary>
        /// <param name="mode">推进模式，默认 <see cref="PlaylistMode.Loop"/>。</param>
        public MusicPlaylist(PlaylistMode mode = PlaylistMode.Loop)
        {
            Mode = mode;
        }

        /// <summary>
        /// 推进模式，可在运行期切换。
        /// </summary>
        public PlaylistMode Mode { get; set; }

        /// <summary>
        /// 曲目数量。
        /// </summary>
        public int Count
        {
            get { return m_Tracks.Count; }
        }

        /// <summary>
        /// 当前曲目标识。尚未开始或已越过末尾（Sequential）时返回 null。
        /// </summary>
        public string Current
        {
            get
            {
                if (m_CurrentIndex < 0 || m_CurrentIndex >= m_Tracks.Count)
                {
                    return null;
                }

                return m_Tracks[m_CurrentIndex];
            }
        }

        /// <summary>
        /// 追加一个曲目标识（空字符串将被忽略）。
        /// </summary>
        /// <param name="trackId">曲目标识。</param>
        public void Add(string trackId)
        {
            if (string.IsNullOrEmpty(trackId))
            {
                return;
            }

            m_Tracks.Add(trackId);
        }

        /// <summary>
        /// 批量追加曲目标识（null 集合或其中的空项被忽略）。
        /// </summary>
        /// <param name="trackIds">曲目标识序列。</param>
        public void AddRange(IEnumerable<string> trackIds)
        {
            if (trackIds == null)
            {
                return;
            }

            foreach (string id in trackIds)
            {
                Add(id);
            }
        }

        /// <summary>
        /// 清空所有曲目并复位游标。
        /// </summary>
        public void Clear()
        {
            m_Tracks.Clear();
            m_CurrentIndex = -1;
        }

        /// <summary>
        /// 复位到列表开头之前的状态（下一次 <see cref="Next"/> 将从第一首开始）。
        /// </summary>
        public void Reset()
        {
            m_CurrentIndex = -1;
        }

        /// <summary>
        /// 推进到下一首并返回其标识。
        /// <para>Sequential：到末尾后返回 null 并保持终止态。</para>
        /// <para>Loop：到末尾后回绕。</para>
        /// <para>Shuffle：随机选取（曲目数 &gt; 1 时避免与当前曲目相同）。</para>
        /// </summary>
        /// <param name="randomPick">
        /// 可注入的随机选择器：入参为 maxExclusive，返回 [0, maxExclusive) 的索引。
        /// 用于确定性的 Shuffle 测试；为 null 时使用内部 <see cref="System.Random"/>。
        /// 仅 Shuffle 模式使用此参数。
        /// </param>
        /// <returns>下一首曲目标识；无可播放曲目或 Sequential 越界时返回 null。</returns>
        public string Next(Func<int, int> randomPick = null)
        {
            int count = m_Tracks.Count;
            if (count == 0)
            {
                m_CurrentIndex = -1;
                return null;
            }

            switch (Mode)
            {
                case PlaylistMode.Sequential:
                    return AdvanceSequential(count);

                case PlaylistMode.Loop:
                    return AdvanceLoop(count);

                case PlaylistMode.Shuffle:
                    return AdvanceShuffle(count, randomPick);

                default:
                    return AdvanceLoop(count);
            }
        }

        private string AdvanceSequential(int count)
        {
            // 已处于终止态（越界）则保持 null。
            if (m_CurrentIndex >= count)
            {
                m_CurrentIndex = count; // 归一化终止态
                return null;
            }

            m_CurrentIndex++;
            if (m_CurrentIndex >= count)
            {
                m_CurrentIndex = count; // 终止态：Current 返回 null
                return null;
            }

            return m_Tracks[m_CurrentIndex];
        }

        private string AdvanceLoop(int count)
        {
            m_CurrentIndex++;
            if (m_CurrentIndex >= count || m_CurrentIndex < 0)
            {
                m_CurrentIndex = 0;
            }

            return m_Tracks[m_CurrentIndex];
        }

        private string AdvanceShuffle(int count, Func<int, int> randomPick)
        {
            if (count == 1)
            {
                m_CurrentIndex = 0;
                return m_Tracks[0];
            }

            int prev = (m_CurrentIndex >= 0 && m_CurrentIndex < count) ? m_CurrentIndex : -1;
            int picked;
            if (prev < 0)
            {
                picked = PickRandomIndex(count, randomPick);
            }
            else
            {
                // 从 count-1 个「非当前曲目」候选里均匀抽取，再把落在 prev 及之后的下标 +1 还原。
                // 这样每个非当前曲目被选中的概率都是 1/(count-1)；旧实现的 (picked+1)%count
                // 会让下标 prev+1 的概率翻倍（被自身抽中 + 被 prev 冲突后顺延），分布有偏。
                int r = PickRandomIndex(count - 1, randomPick);
                picked = r >= prev ? r + 1 : r;
            }

            m_CurrentIndex = picked;
            return m_Tracks[picked];
        }

        private int PickRandomIndex(int count, Func<int, int> randomPick)
        {
            int index;
            if (randomPick != null)
            {
                index = randomPick(count);
            }
            else
            {
                if (m_Random == null)
                {
                    m_Random = new Random();
                }

                index = m_Random.Next(count);
            }

            // 钳制注入器返回的越界值，保证健壮。
            if (index < 0)
            {
                index = 0;
            }
            else if (index >= count)
            {
                index = count - 1;
            }

            return index;
        }
    }
}
