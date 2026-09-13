//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Experiments
{
    /// <summary>
    /// A/B 实验分桶管理器：注册实验定义，并对（用户, 实验）做稳定加权分配（stable weighted assignment）。
    ///
    /// 分桶算法（稳定且平台确定）：
    /// 1. 取 <c>p = StableHash.Normalized(userId, experimentId)</c>，落在 [0,1)；该值仅由 (userId, experimentId) 决定，
    ///    与进程、设备、运行次数无关——故同一 (用户, 实验) 恒落同一变体（这正是 A/B 分桶要求的稳定性）。
    /// 2. 把 p 放大到总权重区间：<c>target = p * totalWeight</c>。
    /// 3. 按变体定义顺序累加权重，返回首个使 <c>target &lt; 累积权重</c> 的变体——即 p 落入的权重带（cumulative weight band）。
    ///    在大量不同 userId 上，各变体命中比例逼近其权重占比。
    ///
    /// QA 覆盖：<see cref="ForceVariant"/> 可对特定 (用户, 实验) 强制指定变体，优先级高于哈希分桶，
    /// 便于测试与问题复现；<see cref="ClearForce"/> 撤销后恢复哈希结果。
    ///
    /// 单线程使用，非线程安全。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// 作为框架模块，可通过 <c>Framework.GetModule&lt;IExperimentManager&gt;()</c> 解析获取；
    /// 无每帧工作，<see cref="Update"/> 为空实现。
    /// </summary>
    public sealed class ExperimentManager : FrameworkModule, IExperimentManager
    {
        /// <summary>
        /// 实验 id -> 实验定义。id 使用序数比较（Ordinal），避免文化相关的大小写折叠造成的歧义。
        /// </summary>
        private readonly Dictionary<string, Experiment> m_Experiments;

        /// <summary>
        /// QA 强制覆盖：(实验 id, 用户 id) -> 强制变体 id。优先级高于哈希分桶。
        /// </summary>
        private readonly Dictionary<ForceKey, string> m_Forced;

        /// <summary>
        /// 构造空管理器。
        /// </summary>
        public ExperimentManager()
        {
            m_Experiments = new Dictionary<string, Experiment>(StringComparer.Ordinal);
            m_Forced = new Dictionary<ForceKey, string>();
        }

        /// <summary>
        /// 模块轮询优先级。无每帧工作，与核心默认模块一致取 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 当前已注册的实验数量。
        /// </summary>
        public int Count
        {
            get { return m_Experiments.Count; }
        }

        /// <summary>
        /// 注册一个实验定义。
        /// </summary>
        /// <param name="experiment">实验定义，不可为 null。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="experiment"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">当同名实验 id 已注册时抛出。</exception>
        public void Define(Experiment experiment)
        {
            if (experiment == null)
            {
                throw new ArgumentNullException(nameof(experiment));
            }

            if (m_Experiments.ContainsKey(experiment.Id))
            {
                throw new ArgumentException($"实验 id 已存在：'{experiment.Id}'。", nameof(experiment));
            }

            m_Experiments.Add(experiment.Id, experiment);
        }

        /// <summary>
        /// 判断是否已注册指定实验。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <returns>已注册返回 true，否则返回 false。</returns>
        public bool Has(string experimentId)
        {
            return experimentId != null && m_Experiments.ContainsKey(experimentId);
        }

        /// <summary>
        /// 获取实验定义。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <returns>对应实验定义；未注册时返回 null。</returns>
        public Experiment Get(string experimentId)
        {
            if (experimentId != null && m_Experiments.TryGetValue(experimentId, out Experiment experiment))
            {
                return experiment;
            }

            return null;
        }

        /// <summary>
        /// 稳定加权分配：返回用户在指定实验中被分配到的变体。
        /// 若实验未注册或处于禁用（<see cref="Experiment.Enabled"/> == false）状态，返回 null。
        /// 若该 (用户, 实验) 存在有效的强制覆盖（<see cref="ForceVariant"/>），优先返回强制变体。
        /// 同一 (用户, 实验) 多次调用恒返回同一变体。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id（稳定分桶依据，应在跨设备/跨运行间保持一致）。</param>
        /// <returns>分配到的变体；实验缺失或禁用时为 null。</returns>
        public Variant GetVariant(string experimentId, string userId)
        {
            Experiment experiment = Get(experimentId);
            if (experiment == null || !experiment.Enabled)
            {
                return null;
            }

            // QA 强制覆盖优先：仅当强制的变体 id 仍存在于实验中时生效（防止陈旧覆盖指向已移除变体）。
            if (m_Forced.TryGetValue(new ForceKey(experimentId, userId), out string forcedId))
            {
                Variant forced = FindVariant(experiment, forcedId);
                if (forced != null)
                {
                    return forced;
                }
            }

            return Assign(experiment, userId);
        }

        /// <summary>
        /// 稳定加权分配并仅返回变体 id。语义同 <see cref="GetVariant"/>。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id。</param>
        /// <returns>分配到的变体 id；无分配（实验缺失或禁用）时为 null。</returns>
        public string GetVariantId(string experimentId, string userId)
        {
            Variant variant = GetVariant(experimentId, userId);
            return variant?.Id;
        }

        /// <summary>
        /// 判断用户在指定实验中是否被分配到给定变体。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id。</param>
        /// <param name="variantId">待比对的变体 id。</param>
        /// <returns>分配结果的变体 id 与 <paramref name="variantId"/> 相等返回 true，否则 false。</returns>
        public bool IsInVariant(string experimentId, string userId, string variantId)
        {
            string assigned = GetVariantId(experimentId, userId);
            return assigned != null && string.Equals(assigned, variantId, StringComparison.Ordinal);
        }

        /// <summary>
        /// QA 覆盖：对特定 (用户, 实验) 强制指定变体；其后 <see cref="GetVariant"/> 将返回该变体（优先于哈希分桶）。
        /// 注意：仅记录覆盖意图，不在此校验变体是否存在；读取时若变体已不存在则覆盖自动失效、回落哈希分桶。
        /// </summary>
        /// <param name="experimentId">实验 id，不可为 null。</param>
        /// <param name="userId">用户 id（可为 null，按空串处理以与分桶键一致）。</param>
        /// <param name="variantId">强制指定的变体 id，不可为 null 或空。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="experimentId"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">当 <paramref name="variantId"/> 为 null 或空时抛出。</exception>
        public void ForceVariant(string experimentId, string userId, string variantId)
        {
            if (experimentId == null)
            {
                throw new ArgumentNullException(nameof(experimentId));
            }

            if (string.IsNullOrEmpty(variantId))
            {
                throw new ArgumentException("强制变体 id 不可为 null 或空。", nameof(variantId));
            }

            m_Forced[new ForceKey(experimentId, userId)] = variantId;
        }

        /// <summary>
        /// 撤销特定 (用户, 实验) 的强制覆盖；其后 <see cref="GetVariant"/> 回落为哈希分桶结果。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id。</param>
        /// <returns>存在并被移除返回 true；本无覆盖返回 false。</returns>
        public bool ClearForce(string experimentId, string userId)
        {
            if (experimentId == null)
            {
                return false;
            }

            return m_Forced.Remove(new ForceKey(experimentId, userId));
        }

        /// <summary>
        /// 取用户被分配变体的 <see cref="Variant.Payload"/> 并按 <typeparamref name="T"/> 强转返回。
        /// 当无分配、载荷为 null、或载荷类型不匹配时，返回 <paramref name="fallback"/>。
        /// </summary>
        /// <typeparam name="T">期望的载荷类型。</typeparam>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id。</param>
        /// <param name="fallback">兜底值，默认为 <typeparamref name="T"/> 的默认值。</param>
        /// <returns>载荷或兜底值。</returns>
        public T GetPayload<T>(string experimentId, string userId, T fallback = default)
        {
            Variant variant = GetVariant(experimentId, userId);
            if (variant != null && variant.Payload is T typed)
            {
                return typed;
            }

            return fallback;
        }

        /// <summary>
        /// 模块轮询。实验分桶无每帧工作（注册即静态、查询即时计算），此处为空实现。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒）。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理模块运行期状态：清空已注册实验与 QA 强制覆盖。
        /// </summary>
        public override void Shutdown()
        {
            m_Experiments.Clear();
            m_Forced.Clear();
        }

        /// <summary>
        /// 核心稳定加权分配：把 [0,1) 哈希值放大到总权重区间，按定义顺序扫描累积权重带返回命中变体。
        /// </summary>
        private static Variant Assign(Experiment experiment, string userId)
        {
            IReadOnlyList<Variant> variants = experiment.Variants;

            long totalWeight = 0;
            for (int i = 0; i < variants.Count; i++)
            {
                totalWeight += variants[i].Weight;
            }

            // 变体权重均为正，构造时保证至少一个变体，故 totalWeight > 0。
            double p = StableHash.Normalized(userId, experiment.Id);
            double target = p * totalWeight;

            double cumulative = 0;
            for (int i = 0; i < variants.Count; i++)
            {
                cumulative += variants[i].Weight;
                if (target < cumulative)
                {
                    return variants[i];
                }
            }

            // 理论上不可达（p < 1 ⇒ target < totalWeight == 最终 cumulative）；
            // 兜底返回末位变体，规避极端浮点边界。
            return variants[variants.Count - 1];
        }

        /// <summary>
        /// 在实验内按 id 查找变体；未找到返回 null。
        /// </summary>
        private static Variant FindVariant(Experiment experiment, string variantId)
        {
            IReadOnlyList<Variant> variants = experiment.Variants;
            for (int i = 0; i < variants.Count; i++)
            {
                if (string.Equals(variants[i].Id, variantId, StringComparison.Ordinal))
                {
                    return variants[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 强制覆盖字典的复合键：(实验 id, 用户 id)。null 用户按空串归一，保证可哈希且等值稳定。
        /// </summary>
        private readonly struct ForceKey : IEquatable<ForceKey>
        {
            private readonly string m_ExperimentId;
            private readonly string m_UserId;

            public ForceKey(string experimentId, string userId)
            {
                m_ExperimentId = experimentId ?? string.Empty;
                m_UserId = userId ?? string.Empty;
            }

            public bool Equals(ForceKey other)
            {
                return string.Equals(m_ExperimentId, other.m_ExperimentId, StringComparison.Ordinal)
                       && string.Equals(m_UserId, other.m_UserId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ForceKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                // 进程内字典分桶用途，借助稳定哈希组合两段 id；不要求跨进程一致。
                unchecked
                {
                    return (int)(StableHash.Fnv1a(m_ExperimentId) * 397u ^ StableHash.Fnv1a(m_UserId));
                }
            }
        }
    }
}
