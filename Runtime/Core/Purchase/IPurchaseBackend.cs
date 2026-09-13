//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Purchase
{
    /// <summary>
    /// 内购平台后端接口。Unity 层或业务层实现，桥接具体 IAP SDK
    /// （如 Unity Purchasing、各平台原生 StoreKit / BillingClient 等）。
    /// 框架核心引擎无关，故真实实现位于框架之外。
    /// </summary>
    public interface IPurchaseBackend
    {
        /// <summary>
        /// 是否已完成初始化（拉到商店商品列表、可发起购买）。
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 初始化后端：连接商店、拉取 <paramref name="products"/> 的元数据。
        /// 完成后通过 <paramref name="onInitialized"/> 回调 true（成功）/ false（失败）。
        /// </summary>
        /// <param name="products">需要初始化的商品列表。</param>
        /// <param name="onInitialized">初始化完成回调（可空）。</param>
        void Initialize(IReadOnlyList<PurchaseProduct> products, Action<bool> onInitialized);

        /// <summary>
        /// 发起购买。结果通过 <paramref name="onResult"/> 回调。
        /// </summary>
        /// <param name="productId">商品标识。</param>
        /// <param name="onResult">购买结果回调。</param>
        void Purchase(string productId, Action<PurchaseResult> onResult);

        /// <summary>
        /// 恢复购买（非消耗型/订阅型，常用于换机/重装后还原已购权益）。
        /// </summary>
        /// <param name="onDone">恢复完成回调（true 成功 / false 失败，可空）。</param>
        void RestorePurchases(Action<bool> onDone);
    }

    /// <summary>
    /// 收据校验器接口（服务端校验挂钩）。
    /// 业务可实现此接口将平台收据回传服务端验真，防止越权/伪造收据。
    /// 默认未设置（null）时框架信任后端结果，不做二次校验。
    /// </summary>
    public interface IReceiptValidator
    {
        /// <summary>
        /// 校验一笔购买的收据。完成后通过 <paramref name="onValid"/> 回调
        /// true（有效）/ false（无效）。
        /// </summary>
        /// <param name="purchase">待校验的购买结果（含 Receipt / TransactionId）。</param>
        /// <param name="onValid">校验结果回调。</param>
        void Validate(PurchaseResult purchase, Action<bool> onValid);
    }
}
