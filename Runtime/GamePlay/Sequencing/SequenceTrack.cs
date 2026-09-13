//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Sequencing
{
    /// <summary>
    /// 一条时间轨道（Track）：承载一组按时间排序的 <see cref="SequenceMarker"/>。
    ///
    /// 一个 <see cref="SequenceDirector"/> 可包含多条轨道（例如 "camera"、"audio"、"dialogue"），
    /// 各轨道独立保存自己的标记；导演（Director）在推进播放头时跨所有轨道统一按时间触发。
    ///
    /// 添加规则：
    /// - <see cref="Add"/> 始终保持标记按 <see cref="SequenceMarker.Time"/> 升序排列；
    /// - 对于相同时间点的多个标记，按添加先后保持稳定顺序（稳定插入，便于可预测的触发次序）。
    ///
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class SequenceTrack
    {
        private readonly List<SequenceMarker> m_Markers = new List<SequenceMarker>(16);

        /// <summary>
        /// 构造一条具名轨道。
        /// </summary>
        /// <param name="id">轨道 Id，不可为空或空白。</param>
        /// <exception cref="ArgumentException">id 为空或空白时抛出。</exception>
        public SequenceTrack(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("轨道 Id 不能为空。", nameof(id));
            }

            Id = id;
        }

        /// <summary>
        /// 轨道 Id。
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 该轨道上的所有标记，按时间升序只读视图。
        /// </summary>
        public IReadOnlyList<SequenceMarker> Markers => m_Markers;

        /// <summary>
        /// 轨道时长：最后一个标记的时间；空轨道为 0。
        /// </summary>
        public double Duration => m_Markers.Count == 0 ? 0d : m_Markers[m_Markers.Count - 1].Time;

        /// <summary>
        /// 在轨道上添加一个标记，并保持按时间升序排列（相同时间按添加先后稳定）。
        /// </summary>
        /// <param name="time">相对序列起点的触发时间（秒）。</param>
        /// <param name="key">事件键。</param>
        /// <param name="payload">任意负载，可为 null。</param>
        /// <returns>自身，便于链式调用。</returns>
        public SequenceTrack Add(double time, string key, object payload = null)
        {
            var marker = new SequenceMarker(time, key, payload, Id);

            // 稳定插入：找到第一个 Time 严格大于新标记的位置插入；
            // 这样相同时间的既有标记排在新标记之前，保留添加顺序。
            int index = m_Markers.Count;
            for (int i = 0; i < m_Markers.Count; i++)
            {
                if (m_Markers[i].Time > time)
                {
                    index = i;
                    break;
                }
            }

            m_Markers.Insert(index, marker);
            return this;
        }
    }
}
