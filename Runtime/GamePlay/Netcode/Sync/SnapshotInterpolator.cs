//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Netcode.Sync
{
    /// <summary>
    /// 快照插值器：在渲染时刻对 <see cref="SnapshotBuffer"/> 中的实体状态做线性插值。
    /// <para>
    /// 渲染时间通常取 <c>渲染服务器时间 = 最新服务器时间 - 插值延迟</c>，
    /// 延迟（<see cref="InterpolationDelaySec"/>）用于让缓冲积累足够的未来快照以平滑抖动。
    /// 本插值器只做插值、不做外推：超出缓冲窗口时由缓冲层钳制到端点。
    /// </para>
    /// </summary>
    public sealed class SnapshotInterpolator
    {
        private readonly SnapshotBuffer m_Buffer;
        private double m_InterpolationDelaySec;

        // 复用的去重集合，避免每次 Sample 为集合分配。
        private readonly HashSet<int> m_Seen = new HashSet<int>();
        // non-alloc Sample 重载按结果槽位复用状态向量；容量按历史峰值保留，不按 EntityId 永久增长。
        private readonly List<float[]> m_ValueBuffers = new List<float[]>();

        /// <summary>
        /// 构造插值器。
        /// </summary>
        /// <param name="buffer">数据来源抖动缓冲，不可为 null。</param>
        /// <param name="interpolationDelaySec">插值延迟（秒），默认 0.1（100ms）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> 为 null 时抛出。</exception>
        public SnapshotInterpolator(SnapshotBuffer buffer, double interpolationDelaySec = 0.1)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            m_Buffer = buffer;
            m_InterpolationDelaySec = interpolationDelaySec;
        }

        /// <summary>
        /// 插值延迟（秒）。增大可更好地吸收抖动但增加观感延迟。
        /// </summary>
        public double InterpolationDelaySec
        {
            get { return m_InterpolationDelaySec; }
            set { m_InterpolationDelaySec = value; }
        }

        /// <summary>
        /// 在给定服务器时间上采样所有实体的插值状态。
        /// <para>
        /// 实际渲染时间为 <c>clientNowMappedToServerTimeSec - InterpolationDelaySec</c>。
        /// 取包夹该渲染时间的相邻两个快照 (from, to)：
        /// 同时存在于两者的实体，按 t 对其 <see cref="EntitySnapshot.Values"/> 逐分量线性插值；
        /// 仅出现在某一侧的实体，直接采用该侧的值（不做淡入淡出）。
        /// 每次调用返回一个全新的列表。缓冲为空时返回空列表。
        /// </para>
        /// </summary>
        /// <param name="clientNowMappedToServerTimeSec">客户端当前时刻映射到服务器时间轴上的秒。</param>
        /// <returns>渲染时刻的实体插值状态集合（新列表）。</returns>
        public IReadOnlyList<EntitySnapshot> Sample(double clientNowMappedToServerTimeSec)
        {
            var result = new List<EntitySnapshot>();
            SampleInto(clientNowMappedToServerTimeSec, result, reuseValueBuffers: false);
            return result;
        }

        /// <summary>
        /// 非分配重载：把渲染时刻的实体插值状态填入调用方提供的 <paramref name="into"/>（先清空）。
        /// 复用内部去重集合，避免每帧为集合分配；列表由调用方复用。各实体的
        /// <see cref="EntitySnapshot.Values"/> 仍按需新建（结果数组交由调用方读取，不应跨下一次采样长期持有）。
        /// </summary>
        /// <param name="clientNowMappedToServerTimeSec">客户端当前时刻映射到服务器时间轴上的秒。</param>
        /// <param name="into">装载结果的列表，调用前会被清空；不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="into"/> 为 null 时抛出。</exception>
        public void Sample(double clientNowMappedToServerTimeSec, List<EntitySnapshot> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            SampleInto(clientNowMappedToServerTimeSec, into, reuseValueBuffers: true);
        }

        // 核心采样：把渲染时刻的插值结果追加进 result（不清空 result，由调用方负责）。
        private void SampleInto(double clientNowMappedToServerTimeSec, List<EntitySnapshot> result, bool reuseValueBuffers)
        {
            double renderTime = clientNowMappedToServerTimeSec - m_InterpolationDelaySec;

            WorldSnapshot from;
            WorldSnapshot to;
            float t;
            if (!m_Buffer.GetInterpolationPair(renderTime, out from, out to, out t))
            {
                return;
            }

            // 收集两侧出现过的所有实体 id（避免遗漏只在一侧的实体）。复用集合，先清空。
            m_Seen.Clear();

            IReadOnlyList<EntitySnapshot> fromEntities = from.Entities;
            for (int i = 0; i < fromEntities.Count; i++)
            {
                EntitySnapshot a = fromEntities[i];
                m_Seen.Add(a.EntityId);

                EntitySnapshot b;
                if (to.TryGetEntity(a.EntityId, out b))
                {
                    // 两侧都有 —— 逐分量插值。
                    float[] values = reuseValueBuffers
                        ? GetValueBuffer(result.Count, MaxLength(a.Values, b.Values))
                        : new float[MaxLength(a.Values, b.Values)];
                    LerpValuesInto(a.Values, b.Values, t, values);
                    result.Add(new EntitySnapshot(a.EntityId, values));
                }
                else
                {
                    // 仅在 from —— 采用 from 的值（拷贝一份避免外部共享内部数组）。
                    float[] values = reuseValueBuffers
                        ? GetValueBuffer(result.Count, LengthOf(a.Values))
                        : CreateValueBuffer(LengthOf(a.Values));
                    CopyValuesInto(a.Values, values);
                    result.Add(new EntitySnapshot(a.EntityId, values));
                }
            }

            // 仅在 to 出现的实体。
            IReadOnlyList<EntitySnapshot> toEntities = to.Entities;
            for (int i = 0; i < toEntities.Count; i++)
            {
                EntitySnapshot b = toEntities[i];
                if (m_Seen.Contains(b.EntityId))
                {
                    continue;
                }

                float[] values = reuseValueBuffers
                    ? GetValueBuffer(result.Count, LengthOf(b.Values))
                    : CreateValueBuffer(LengthOf(b.Values));
                CopyValuesInto(b.Values, values);
                result.Add(new EntitySnapshot(b.EntityId, values));
            }
        }

        /// <summary>
        /// 逐分量线性插值两个浮点数组：<c>a + (b - a) * t</c>。
        /// <para>
        /// 当两数组长度不同时，公共前缀部分做插值，较长数组的尾部原样保留（不插值），
        /// 以保证状态向量维度变化时不丢失信息。结果长度取两者较大值。
        /// 任一数组为 null 时按长度为 0 处理（直接复用另一侧）。
        /// </para>
        /// </summary>
        /// <param name="a">起点向量。</param>
        /// <param name="b">终点向量。</param>
        /// <param name="t">插值因子，调用方应已钳制于 [0,1]。</param>
        /// <returns>插值后的新数组。</returns>
        public static float[] LerpValues(float[] a, float[] b, float t)
        {
            var result = CreateValueBuffer(MaxLength(a, b));
            LerpValuesInto(a, b, t, result);
            return result;
        }

        private static void LerpValuesInto(float[] a, float[] b, float t, float[] result)
        {
            int lenA = LengthOf(a);
            int lenB = LengthOf(b);
            int common = lenA < lenB ? lenA : lenB;

            for (int i = 0; i < common; i++)
            {
                float av = a[i];
                float bv = b[i];
                result[i] = av + (bv - av) * t;
            }

            // 较长一侧的尾部原样保留。
            if (lenA > lenB)
            {
                for (int i = common; i < lenA; i++)
                {
                    result[i] = a[i];
                }
            }
            else if (lenB > lenA)
            {
                for (int i = common; i < lenB; i++)
                {
                    result[i] = b[i];
                }
            }

        }

        private float[] GetValueBuffer(int slot, int length)
        {
            while (m_ValueBuffers.Count <= slot)
            {
                m_ValueBuffers.Add(null);
            }

            float[] buffer = m_ValueBuffers[slot];
            if (buffer == null || buffer.Length != length)
            {
                buffer = CreateValueBuffer(length);
                m_ValueBuffers[slot] = buffer;
            }
            return buffer;
        }

        private static void CopyValuesInto(float[] source, float[] destination)
        {
            if (source != null && source.Length > 0)
            {
                Array.Copy(source, destination, source.Length);
            }
        }

        private static int LengthOf(float[] values)
        {
            return values != null ? values.Length : 0;
        }

        private static int MaxLength(float[] a, float[] b)
        {
            int lenA = LengthOf(a);
            int lenB = LengthOf(b);
            return lenA > lenB ? lenA : lenB;
        }

        private static float[] CreateValueBuffer(int length)
        {
            return length == 0 ? Array.Empty<float>() : new float[length];
        }
    }
}
