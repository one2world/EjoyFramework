//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Purchase
{
    /// <summary>
    /// 商品类型。
    /// </summary>
    public enum PurchaseProductType
    {
        /// <summary>
        /// 消耗型（可重复购买，如金币、体力）。
        /// </summary>
        Consumable,

        /// <summary>
        /// 非消耗型（一次购买永久拥有，如去广告、解锁关卡）。
        /// </summary>
        NonConsumable,

        /// <summary>
        /// 订阅型（周期性付费，如月卡、会员）。
        /// </summary>
        Subscription,
    }

    /// <summary>
    /// 购买结果码。
    /// </summary>
    public enum PurchaseResultCode
    {
        /// <summary>
        /// 购买成功（若设置了校验器，表示已通过校验）。
        /// </summary>
        Success,

        /// <summary>
        /// 购买挂起（如待审核、等待家长同意等）。
        /// </summary>
        Pending,

        /// <summary>
        /// 用户取消。
        /// </summary>
        Cancelled,

        /// <summary>
        /// 购买失败（平台返回错误、网络异常等）。
        /// </summary>
        Failed,

        /// <summary>
        /// 已拥有（非消耗型/订阅型重复购买）。
        /// </summary>
        AlreadyOwned,

        /// <summary>
        /// 收据校验未通过（疑似伪造/篡改）。
        /// </summary>
        ReceiptInvalid,

        /// <summary>
        /// 未初始化（尚未注入真实后端或后端未初始化即发起购买）。
        /// </summary>
        NotInitialized,
    }

    /// <summary>
    /// 商品描述。由业务在初始化前构造并传入，价格/本地化文案在 <see cref="IPurchaseBackend.Initialize"/>
    /// 拉取到商店元数据后可被回填（由后端实现决定）。
    /// </summary>
    public sealed class PurchaseProduct
    {
        /// <summary>
        /// 商品唯一标识（与商店后台配置一致）。
        /// </summary>
        public string ProductId;

        /// <summary>
        /// 商品类型。
        /// </summary>
        public PurchaseProductType Type;

        /// <summary>
        /// 本地化标题（来自商店）。
        /// </summary>
        public string LocalizedTitle;

        /// <summary>
        /// 本地化描述（来自商店）。
        /// </summary>
        public string LocalizedDescription;

        /// <summary>
        /// 本地化价格字符串（含货币符号，如 "¥6.00"，用于直接展示）。
        /// </summary>
        public string LocalizedPriceString;

        /// <summary>
        /// 价格数值（用于埋点/统计，非展示）。
        /// </summary>
        public decimal Price;

        /// <summary>
        /// 货币代码（ISO 4217，如 "CNY"、"USD"）。
        /// </summary>
        public string CurrencyCode;
    }

    /// <summary>
    /// 购买结果。
    /// </summary>
    public struct PurchaseResult
    {
        /// <summary>
        /// 对应商品标识。
        /// </summary>
        public string ProductId;

        /// <summary>
        /// 结果码。
        /// </summary>
        public PurchaseResultCode Code;

        /// <summary>
        /// 平台收据（用于服务端校验/补单；失败或取消时可为空）。
        /// </summary>
        public string Receipt;

        /// <summary>
        /// 交易标识（平台侧订单号）。
        /// </summary>
        public string TransactionId;

        /// <summary>
        /// 错误信息（成功时为空）。
        /// </summary>
        public string Error;
    }
}
