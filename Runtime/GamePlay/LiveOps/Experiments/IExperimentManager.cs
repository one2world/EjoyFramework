//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Experiments
{
    /// <summary>
    /// A/B 实验分桶管理器接口：注册实验定义，并对（用户, 实验）做稳定加权分配（stable weighted assignment）。
    ///
    /// 实现细节见 <see cref="ExperimentManager"/>。该接口仅暴露调用方使用的注册、查询、稳定分桶、
    /// QA 强制覆盖与类型化载荷读取等公共能力，供 <c>Framework.GetModule&lt;IExperimentManager&gt;()</c> 解析获取。
    /// </summary>
    public interface IExperimentManager
    {
        /// <summary>
        /// 当前已注册的实验数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 注册一个实验定义。
        /// </summary>
        /// <param name="experiment">实验定义，不可为 null。</param>
        void Define(Experiment experiment);

        /// <summary>
        /// 判断是否已注册指定实验。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <returns>已注册返回 true，否则返回 false。</returns>
        bool Has(string experimentId);

        /// <summary>
        /// 获取实验定义。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <returns>对应实验定义；未注册时返回 null。</returns>
        Experiment Get(string experimentId);

        /// <summary>
        /// 稳定加权分配：返回用户在指定实验中被分配到的变体。
        /// 实验未注册或禁用时返回 null；存在有效强制覆盖时优先返回强制变体；同一 (用户, 实验) 恒返回同一变体。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id（稳定分桶依据，应在跨设备/跨运行间保持一致）。</param>
        /// <returns>分配到的变体；实验缺失或禁用时为 null。</returns>
        Variant GetVariant(string experimentId, string userId);

        /// <summary>
        /// 稳定加权分配并仅返回变体 id。语义同 <see cref="GetVariant"/>。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id。</param>
        /// <returns>分配到的变体 id；无分配（实验缺失或禁用）时为 null。</returns>
        string GetVariantId(string experimentId, string userId);

        /// <summary>
        /// 判断用户在指定实验中是否被分配到给定变体。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id。</param>
        /// <param name="variantId">待比对的变体 id。</param>
        /// <returns>分配结果的变体 id 与 <paramref name="variantId"/> 相等返回 true，否则 false。</returns>
        bool IsInVariant(string experimentId, string userId, string variantId);

        /// <summary>
        /// QA 覆盖：对特定 (用户, 实验) 强制指定变体；其后 <see cref="GetVariant"/> 将返回该变体（优先于哈希分桶）。
        /// </summary>
        /// <param name="experimentId">实验 id，不可为 null。</param>
        /// <param name="userId">用户 id（可为 null，按空串处理以与分桶键一致）。</param>
        /// <param name="variantId">强制指定的变体 id，不可为 null 或空。</param>
        void ForceVariant(string experimentId, string userId, string variantId);

        /// <summary>
        /// 撤销特定 (用户, 实验) 的强制覆盖；其后 <see cref="GetVariant"/> 回落为哈希分桶结果。
        /// </summary>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id。</param>
        /// <returns>存在并被移除返回 true；本无覆盖返回 false。</returns>
        bool ClearForce(string experimentId, string userId);

        /// <summary>
        /// 取用户被分配变体的 <see cref="Variant.Payload"/> 并按 <typeparamref name="T"/> 强转返回。
        /// 当无分配、载荷为 null、或载荷类型不匹配时，返回 <paramref name="fallback"/>。
        /// </summary>
        /// <typeparam name="T">期望的载荷类型。</typeparam>
        /// <param name="experimentId">实验 id。</param>
        /// <param name="userId">用户 id。</param>
        /// <param name="fallback">兜底值，默认为 <typeparamref name="T"/> 的默认值。</param>
        /// <returns>载荷或兜底值。</returns>
        T GetPayload<T>(string experimentId, string userId, T fallback = default);
    }
}
