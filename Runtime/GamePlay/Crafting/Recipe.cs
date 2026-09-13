//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Crafting
{
    /// <summary>
    /// 一条不可变的制作配方：消耗若干输入物料，产出若干物料，可选耗时与工作台要求。
    /// </summary>
    /// <remarks>
    /// 配方一经 <see cref="Builder.Build"/> 生成即不可变，可在多个仓储/会话间安全共享。
    /// 通过 <see cref="Create"/> 获取流式构造器，例如：
    /// <c>Recipe.Create("plank").AddInput("log", 1).AddOutput("plank", 4).WithTime(2f).AtStation("sawmill").Build()</c>。
    /// </remarks>
    public sealed class Recipe
    {
        private readonly string m_Id;
        private readonly IReadOnlyList<ItemAmount> m_Inputs;
        private readonly IReadOnlyList<ItemAmount> m_Outputs;
        private readonly float m_CraftTimeSec;
        private readonly string m_Station;
        private readonly object m_Payload;

        private Recipe(
            string id,
            IReadOnlyList<ItemAmount> inputs,
            IReadOnlyList<ItemAmount> outputs,
            float craftTimeSec,
            string station,
            object payload)
        {
            m_Id = id;
            m_Inputs = inputs;
            m_Outputs = outputs;
            m_CraftTimeSec = craftTimeSec;
            m_Station = station;
            m_Payload = payload;
        }

        /// <summary>
        /// 配方唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 输入物料列表（只读，可能为空集合）。
        /// </summary>
        public IReadOnlyList<ItemAmount> Inputs
        {
            get { return m_Inputs; }
        }

        /// <summary>
        /// 产出物料列表（只读，至少包含一项）。
        /// </summary>
        public IReadOnlyList<ItemAmount> Outputs
        {
            get { return m_Outputs; }
        }

        /// <summary>
        /// 制作耗时（秒）。<c>0</c> 表示瞬时完成。
        /// </summary>
        public float CraftTimeSec
        {
            get { return m_CraftTimeSec; }
        }

        /// <summary>
        /// 所需工作台标识。<c>null</c> 表示无需工作台，任意场合均可制作。
        /// </summary>
        public string Station
        {
            get { return m_Station; }
        }

        /// <summary>
        /// 业务自定义负载，由上层逻辑解释（如图标、解锁条件等）。可为 null。
        /// </summary>
        public object Payload
        {
            get { return m_Payload; }
        }

        /// <summary>
        /// 创建一个以 <paramref name="id"/> 为标识的配方构造器。
        /// </summary>
        /// <param name="id">配方唯一标识，不可为 null 或空。</param>
        /// <returns>可链式调用的 <see cref="Builder"/>。</returns>
        /// <exception cref="ArgumentException"><paramref name="id"/> 为 null 或空时抛出。</exception>
        public static Builder Create(string id)
        {
            return new Builder(id);
        }

        /// <summary>
        /// <see cref="Recipe"/> 的流式构造器。逐步声明输入、产出、耗时与工作台后调用 <see cref="Build"/> 生成不可变配方。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private readonly List<ItemAmount> m_Inputs;
            private readonly List<ItemAmount> m_Outputs;
            private float m_CraftTimeSec;
            private string m_Station;
            private object m_Payload;

            /// <summary>
            /// 构造一个配方构造器。
            /// </summary>
            /// <param name="id">配方唯一标识，不可为 null 或空。</param>
            /// <exception cref="ArgumentException"><paramref name="id"/> 为 null 或空时抛出。</exception>
            public Builder(string id)
            {
                if (string.IsNullOrEmpty(id))
                {
                    throw new ArgumentException("Recipe 的标识不能为空。", nameof(id));
                }

                m_Id = id;
                m_Inputs = new List<ItemAmount>();
                m_Outputs = new List<ItemAmount>();
                m_CraftTimeSec = 0f;
                m_Station = null;
                m_Payload = null;
            }

            /// <summary>
            /// 追加一项输入物料（流式调用）。
            /// </summary>
            /// <param name="itemId">物品唯一标识，不可为 null 或空。</param>
            /// <param name="count">需求数量，必须大于 0。</param>
            /// <returns>当前构造器自身。</returns>
            public Builder AddInput(string itemId, int count)
            {
                m_Inputs.Add(new ItemAmount(itemId, count));
                return this;
            }

            /// <summary>
            /// 追加一项产出物料（流式调用）。
            /// </summary>
            /// <param name="itemId">物品唯一标识，不可为 null 或空。</param>
            /// <param name="count">产出数量，必须大于 0。</param>
            /// <returns>当前构造器自身。</returns>
            public Builder AddOutput(string itemId, int count)
            {
                m_Outputs.Add(new ItemAmount(itemId, count));
                return this;
            }

            /// <summary>
            /// 设置制作耗时（秒，流式调用）。
            /// </summary>
            /// <param name="seconds">耗时秒数，不可为负；<c>0</c> 表示瞬时。</param>
            /// <returns>当前构造器自身。</returns>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="seconds"/> 为负时抛出。</exception>
            public Builder WithTime(float seconds)
            {
                if (seconds < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Recipe 的制作耗时不能为负。");
                }

                m_CraftTimeSec = seconds;
                return this;
            }

            /// <summary>
            /// 设置所需工作台（流式调用）。传入 null 表示无需工作台。
            /// </summary>
            /// <param name="station">工作台标识，可为 null。</param>
            /// <returns>当前构造器自身。</returns>
            public Builder AtStation(string station)
            {
                m_Station = station;
                return this;
            }

            /// <summary>
            /// 设置业务自定义负载（流式调用）。
            /// </summary>
            /// <param name="payload">任意负载对象，可为 null。</param>
            /// <returns>当前构造器自身。</returns>
            public Builder WithPayload(object payload)
            {
                m_Payload = payload;
                return this;
            }

            /// <summary>
            /// 生成不可变的 <see cref="Recipe"/>。
            /// </summary>
            /// <returns>构造完成的配方。</returns>
            /// <exception cref="InvalidOperationException">未声明任何产出物料时抛出。</exception>
            public Recipe Build()
            {
                if (m_Outputs.Count == 0)
                {
                    throw new InvalidOperationException(
                        string.Format("配方「{0}」必须至少声明一项产出物料。", m_Id));
                }

                // 拷贝出独立的只读快照，避免构造器后续被复用时影响已生成的配方。
                // 输入按 ItemId 合并：若不聚合，HasAllInputs 会对同一物品的多条输入各自独立校验持有量，
                // 只要持有「单条」需求量即可通过校验，但 ConsumeInputs 会逐条扣除 → 少付材料、凭空多产。
                ItemAmount[] inputs = AggregateInputs(m_Inputs);
                ItemAmount[] outputs = m_Outputs.ToArray();
                return new Recipe(m_Id, inputs, outputs, m_CraftTimeSec, m_Station, m_Payload);
            }

            // 按 ItemId 合并输入数量，保留首次出现顺序。无重复时返回原快照。
            private static ItemAmount[] AggregateInputs(List<ItemAmount> items)
            {
                if (items.Count <= 1)
                {
                    return items.ToArray();
                }

                List<string> order = new List<string>(items.Count);
                Dictionary<string, long> totals = new Dictionary<string, long>(items.Count, StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    ItemAmount it = items[i];
                    if (!totals.ContainsKey(it.ItemId))
                    {
                        order.Add(it.ItemId);
                        totals[it.ItemId] = 0L;
                    }
                    totals[it.ItemId] += it.Count;
                }

                if (order.Count == items.Count)
                {
                    return items.ToArray(); // 无重复，保持原顺序与数量
                }

                ItemAmount[] result = new ItemAmount[order.Count];
                for (int i = 0; i < order.Count; i++)
                {
                    long total = totals[order[i]];
                    int clamped = total > int.MaxValue ? int.MaxValue : (int)total;
                    result[i] = new ItemAmount(order[i], clamped);
                }

                return result;
            }
        }
    }
}
