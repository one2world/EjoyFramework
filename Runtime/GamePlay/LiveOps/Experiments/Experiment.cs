//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Experiments
{
    /// <summary>
    /// 一个 A/B 实验定义：一组带相对权重的变体（variant），外加启用开关。
    ///
    /// 设计要点：
    /// - 至少包含一个变体；构造时校验变体非空、id 唯一，并立即冻结为只读列表（防止外部篡改导致分桶漂移）。
    /// - <see cref="Enabled"/> 为 false 时，实验逻辑上"未投放"：<see cref="ExperimentManager.GetVariant"/> 直接返回 null，
    ///   便于灰度开关或紧急下线而无需删除定义。
    /// - <see cref="Variants"/> 的顺序即累积权重带（cumulative weight band）的扫描顺序，决定分桶的确定性映射，
    ///   故定义后顺序不可变——这是稳定分桶（同一用户恒落同一变体）的一部分。
    ///
    /// 构造后不可变（immutable）。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// </summary>
    public sealed class Experiment
    {
        /// <summary>
        /// 实验唯一标识，构造后不可变。
        /// </summary>
        private readonly string m_Id;

        /// <summary>
        /// 启用开关，构造后不可变。
        /// </summary>
        private readonly bool m_Enabled;

        /// <summary>
        /// 只读变体列表（构造时拷贝快照并冻结），顺序即累积权重带扫描顺序。
        /// </summary>
        private readonly IReadOnlyList<Variant> m_Variants;

        /// <summary>
        /// 构造实验定义。
        /// </summary>
        /// <param name="id">实验唯一标识，不可为 null 或空白。</param>
        /// <param name="variants">变体集合，至少含一个变体，元素不可为 null 且 id 不可重复。</param>
        /// <param name="enabled">是否启用，默认 true。</param>
        /// <exception cref="ArgumentException">当 <paramref name="id"/> 为空、变体集合为空、含 null 元素或 id 重复时抛出。</exception>
        /// <exception cref="ArgumentNullException">当 <paramref name="variants"/> 为 null 时抛出。</exception>
        public Experiment(string id, IReadOnlyList<Variant> variants, bool enabled = true)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("实验 id 不可为 null 或空。", nameof(id));
            }

            if (variants == null)
            {
                throw new ArgumentNullException(nameof(variants));
            }

            if (variants.Count == 0)
            {
                throw new ArgumentException("实验至少需要一个变体。", nameof(variants));
            }

            // 拷贝快照并校验：避免外部后续修改原集合影响分桶；同时检测 null 元素与重复 id。
            Variant[] snapshot = new Variant[variants.Count];
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < variants.Count; i++)
            {
                Variant variant = variants[i];
                if (variant == null)
                {
                    throw new ArgumentException("变体集合不可包含 null 元素。", nameof(variants));
                }

                if (!seen.Add(variant.Id))
                {
                    throw new ArgumentException($"变体 id 重复：'{variant.Id}'。", nameof(variants));
                }

                snapshot[i] = variant;
            }

            m_Id = id;
            m_Enabled = enabled;
            m_Variants = Array.AsReadOnly(snapshot);
        }

        /// <summary>
        /// 实验唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 实验是否启用；false 表示逻辑上未投放，分桶将返回 null。
        /// </summary>
        public bool Enabled
        {
            get { return m_Enabled; }
        }

        /// <summary>
        /// 只读变体列表，顺序即累积权重带扫描顺序。
        /// </summary>
        public IReadOnlyList<Variant> Variants
        {
            get { return m_Variants; }
        }
    }
}
