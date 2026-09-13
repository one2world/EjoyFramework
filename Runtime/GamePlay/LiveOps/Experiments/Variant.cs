//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Experiments
{
    /// <summary>
    /// A/B 实验中的一个变体（variant），即用户可能被分配到的一种处理分支。
    ///
    /// 设计要点：
    /// - <see cref="Weight"/> 为相对权重（必须为正）：分桶时按各变体权重在总权重中的占比划分 [0,1) 区间，
    ///   权重越大命中概率越高。权重是相对值，无需归一化（例如 3:1 与 75:25 等价）。
    /// - <see cref="Payload"/> 为该变体携带的不透明配置对象（opaque config），由游戏侧自行解释类型；
    ///   框架不做任何约束，便于承载任意结构（数值、配置对象、序列化数据等）。
    /// - 构造后所有字段不可变（immutable），保证分配结果可被安全地跨线程读取与缓存。
    ///
    /// 纯逻辑、与引擎无关（仅依赖 System.*）。
    /// </summary>
    public sealed class Variant
    {
        /// <summary>
        /// 变体唯一标识，构造后不可变。
        /// </summary>
        private readonly string m_Id;

        /// <summary>
        /// 相对权重（正数），构造后不可变。
        /// </summary>
        private readonly int m_Weight;

        /// <summary>
        /// 不透明配置载荷，构造后不可变（引用语义，框架不复制其内容）。
        /// </summary>
        private readonly object m_Payload;

        /// <summary>
        /// 构造变体。
        /// </summary>
        /// <param name="id">变体唯一标识，不可为 null 或空白。</param>
        /// <param name="weight">相对权重，必须为正数。</param>
        /// <param name="payload">该变体的不透明配置载荷，可为 null。</param>
        /// <exception cref="ArgumentException">当 <paramref name="id"/> 为 null 或空白时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="weight"/> 不是正数时抛出。</exception>
        public Variant(string id, int weight, object payload = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("变体 id 不可为 null 或空。", nameof(id));
            }

            if (weight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "变体权重必须为正数。");
            }

            m_Id = id;
            m_Weight = weight;
            m_Payload = payload;
        }

        /// <summary>
        /// 变体唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 相对权重（正数）。
        /// </summary>
        public int Weight
        {
            get { return m_Weight; }
        }

        /// <summary>
        /// 该变体的不透明配置载荷（可为 null）。
        /// </summary>
        public object Payload
        {
            get { return m_Payload; }
        }
    }
}
