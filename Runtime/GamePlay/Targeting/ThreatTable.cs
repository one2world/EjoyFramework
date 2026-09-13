//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Targeting
{
    /// <summary>
    /// 仇恨表。以实体 Id 为键累积“仇恨/威胁”值，用于 AI 选取攻击目标（动作/MOBA 的拉怪、嘲讽、衰减等）。
    /// </summary>
    /// <remarks>
    /// 任一会改变内容的操作（增加、设置、移除、衰减、清空）之后，都会重新计算“仇恨最高者”；
    /// 当该最高者发生变化时触发 <see cref="OnTopChanged"/>。最高者的判定在平局时取较小 Id，保证确定。
    /// <see cref="Decay"/> 在遍历中删除条目，遵循 Mono/IL2CPP 的安全做法：先把键快照到临时列表，再迭代修改字典。
    /// </remarks>
    public sealed class ThreatTable
    {
        /// <summary>
        /// 衰减/清理时视为“归零”的阈值。低于该值的条目会被剔除。
        /// </summary>
        private const float NearZeroThreshold = 1e-4f;

        private readonly Dictionary<int, float> m_Threats;

        // 缓存当前最高者，便于在每次变更后判断 Top 是否切换并触发事件。
        private bool m_HasTop;
        private int m_TopId;

        // Decay 删除时复用的键缓冲，避免在遍历字典时修改它，同时减少每次衰减的分配。
        private readonly List<int> m_PruneBuffer;

        /// <summary>
        /// 构造一张空的仇恨表。
        /// </summary>
        public ThreatTable()
        {
            m_Threats = new Dictionary<int, float>();
            m_PruneBuffer = new List<int>();
            m_HasTop = false;
            m_TopId = 0;
        }

        /// <summary>
        /// 当仇恨最高的实体发生变化时触发。参数为本表自身与新的最高者 Id。
        /// </summary>
        /// <remarks>
        /// 当表从“有最高者”变为“空表”时，不触发该事件（无新的最高者可报告）。
        /// </remarks>
        public event Action<ThreatTable, int> OnTopChanged;

        /// <summary>
        /// 当前记录的实体数量。
        /// </summary>
        public int Count
        {
            get { return m_Threats.Count; }
        }

        /// <summary>
        /// 全部仇恨记录（只读）。
        /// </summary>
        public IReadOnlyDictionary<int, float> Threats
        {
            get { return m_Threats; }
        }

        /// <summary>
        /// 为指定实体累加仇恨值（不存在则视为从 0 开始）。
        /// </summary>
        /// <param name="entityId">实体 Id。</param>
        /// <param name="amount">要累加的仇恨量，可为负（表示减少）。</param>
        public void AddThreat(int entityId, float amount)
        {
            float current;
            if (m_Threats.TryGetValue(entityId, out current))
            {
                m_Threats[entityId] = current + amount;
            }
            else
            {
                m_Threats[entityId] = amount;
            }

            RecomputeTop();
        }

        /// <summary>
        /// 直接设置（覆盖）指定实体的仇恨值。
        /// </summary>
        /// <param name="entityId">实体 Id。</param>
        /// <param name="amount">目标仇恨量。</param>
        public void SetThreat(int entityId, float amount)
        {
            m_Threats[entityId] = amount;
            RecomputeTop();
        }

        /// <summary>
        /// 移除指定实体的仇恨记录。
        /// </summary>
        /// <param name="entityId">实体 Id。</param>
        /// <returns>存在并被移除时返回 <c>true</c>，否则 <c>false</c>。</returns>
        public bool Remove(int entityId)
        {
            bool removed = m_Threats.Remove(entityId);
            if (removed)
            {
                RecomputeTop();
            }

            return removed;
        }

        /// <summary>
        /// 获取指定实体的仇恨值；不存在时返回 0。
        /// </summary>
        /// <param name="entityId">实体 Id。</param>
        /// <returns>当前仇恨值，未记录则为 0。</returns>
        public float GetThreat(int entityId)
        {
            float value;
            return m_Threats.TryGetValue(entityId, out value) ? value : 0f;
        }

        /// <summary>
        /// 取仇恨最高的实体。平局时返回较小 Id。
        /// </summary>
        /// <param name="entityId">输出：最高者 Id；表为空时为默认值 0。</param>
        /// <returns>存在记录时返回 <c>true</c>，空表返回 <c>false</c>。</returns>
        public bool TryGetTop(out int entityId)
        {
            if (m_HasTop)
            {
                entityId = m_TopId;
                return true;
            }

            entityId = 0;
            return false;
        }

        /// <summary>
        /// 对所有仇恨值乘以 <paramref name="factor"/> 进行衰减，并剔除衰减后接近 0 的条目。
        /// </summary>
        /// <param name="factor">衰减系数；会被钳制到 <c>[0, 1]</c> 区间。</param>
        public void Decay(float factor)
        {
            if (m_Threats.Count == 0)
            {
                return;
            }

            float clamped = factor;
            if (clamped < 0f)
            {
                clamped = 0f;
            }
            else if (clamped > 1f)
            {
                clamped = 1f;
            }

            // 先快照键，避免“遍历字典的同时修改字典”（Mono/IL2CPP 下会抛 InvalidOperationException）。
            m_PruneBuffer.Clear();
            foreach (KeyValuePair<int, float> pair in m_Threats)
            {
                m_PruneBuffer.Add(pair.Key);
            }

            for (int i = 0; i < m_PruneBuffer.Count; i++)
            {
                int id = m_PruneBuffer[i];
                float scaled = m_Threats[id] * clamped;

                if (scaled <= NearZeroThreshold && scaled >= -NearZeroThreshold)
                {
                    m_Threats.Remove(id);
                }
                else
                {
                    m_Threats[id] = scaled;
                }
            }

            m_PruneBuffer.Clear();
            RecomputeTop();
        }

        /// <summary>
        /// 清空所有仇恨记录。若清空前存在最高者，则视为最高者消失（不触发 <see cref="OnTopChanged"/>）。
        /// </summary>
        public void Clear()
        {
            m_Threats.Clear();
            m_PruneBuffer.Clear();
            // 表已空：标记无最高者，但不触发事件（无新最高者可报告）。
            m_HasTop = false;
            m_TopId = 0;
        }

        /// <summary>
        /// 重新计算当前最高者；若与缓存的最高者不同（且存在新最高者），触发 <see cref="OnTopChanged"/>。
        /// </summary>
        private void RecomputeTop()
        {
            bool hasTop = false;
            int topId = 0;
            float topValue = 0f;

            foreach (KeyValuePair<int, float> pair in m_Threats)
            {
                if (!hasTop
                    || pair.Value > topValue
                    || (pair.Value == topValue && pair.Key < topId))
                {
                    hasTop = true;
                    topId = pair.Key;
                    topValue = pair.Value;
                }
            }

            bool topChanged = hasTop && (!m_HasTop || topId != m_TopId);

            m_HasTop = hasTop;
            m_TopId = hasTop ? topId : 0;

            if (topChanged)
            {
                Action<ThreatTable, int> handler = OnTopChanged;
                if (handler != null)
                {
                    handler(this, m_TopId);
                }
            }
        }
    }
}
