//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Purchase
{
    /// <summary>
    /// 内购管理器接口。
    /// 框架提供统一的购买出口、商品目录与可选的服务端收据校验流程；
    /// 具体平台对接由业务注入的 <see cref="IPurchaseBackend"/> 实现。
    /// 未注入真实后端时，默认后端对任何购买返回 <see cref="PurchaseResultCode.NotInitialized"/>，
    /// 明确失败而非静默"成功"，避免误发权益。
    /// </summary>
    public interface IPurchaseManager
    {
        /// <summary>
        /// 是否已初始化（已注入后端且后端初始化成功）。
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 设置平台后端。传 null 表示清除（回到默认 NotInitialized 形态）。
        /// </summary>
        void SetBackend(IPurchaseBackend backend);

        /// <summary>
        /// 设置收据校验器。传 null 表示清除（不做服务端校验，信任后端结果）。
        /// </summary>
        void SetReceiptValidator(IReceiptValidator validator);

        /// <summary>
        /// 初始化：登记商品目录并驱动后端初始化。
        /// </summary>
        /// <param name="products">商品列表。</param>
        /// <param name="onInitialized">初始化完成回调（可空）。</param>
        void Initialize(IReadOnlyList<PurchaseProduct> products, Action<bool> onInitialized = null);

        /// <summary>
        /// 发起购买。流程：未初始化 → 直接回报 NotInitialized；
        /// 否则交由后端购买，成功后若设置了校验器则先校验收据
        /// （无效回报 ReceiptInvalid，有效回报 Success），最终回调并触发 <see cref="PurchaseCompleted"/>。
        /// </summary>
        /// <param name="productId">商品标识。</param>
        /// <param name="onResult">购买结果回调。</param>
        void Purchase(string productId, Action<PurchaseResult> onResult);

        /// <summary>
        /// 恢复购买。
        /// </summary>
        /// <param name="onDone">恢复完成回调（可空）。</param>
        void RestorePurchases(Action<bool> onDone = null);

        /// <summary>
        /// 取某个商品描述。不存在时返回 null。
        /// </summary>
        PurchaseProduct GetProduct(string productId);

        /// <summary>
        /// 取全部已登记商品（只读快照）。
        /// </summary>
        IReadOnlyList<PurchaseProduct> GetProducts();

        /// <summary>
        /// 购买完成事件（每笔购买结果均会触发，无论成功失败）。
        /// </summary>
        event Action<PurchaseResult> PurchaseCompleted;
    }
}
