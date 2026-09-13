//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Purchase
{
    /// <summary>
    /// 内购管理器实现。
    ///
    /// 默认后端 = 私有 <see cref="NullPurchaseBackend"/>：Initialize 立即回报 false、
    /// Purchase 立即回报 <see cref="PurchaseResultCode.NotInitialized"/>。因此在未注入真实后端时
    /// 购买会"明确失败"，而非静默成功误发权益。
    ///
    /// 购买流程：未初始化 → 直接回报 NotInitialized；否则交由后端购买，成功后若设置了校验器
    /// 则先校验收据（无效 → ReceiptInvalid，有效 → Success），统一回调并触发 PurchaseCompleted。
    /// 后端/校验器抛出的异常被捕获并记日志，绝不让购买路径成为崩溃源。
    ///
    /// 核心层引擎无关：本类仅使用 System.* ，不引用 UnityEngine。
    /// </summary>
    internal sealed class PurchaseManager : FrameworkModule, IPurchaseManager
    {
        private readonly Dictionary<string, PurchaseProduct> m_Products = new Dictionary<string, PurchaseProduct>(StringComparer.Ordinal);
        private readonly List<PurchaseProduct> m_ProductList = new List<PurchaseProduct>();
        private IPurchaseBackend m_Backend;
        private IReceiptValidator m_Validator;

        public PurchaseManager()
        {
            m_Backend = NullPurchaseBackend.Instance;
            m_Validator = null;
        }

        // Priority 0：基础服务模块，无依赖。
        public override int Priority { get { return 0; } }

        public bool IsInitialized
        {
            get
            {
                IPurchaseBackend backend = m_Backend;
                if (backend == null) return false;
                try { return backend.IsInitialized; }
                catch (Exception ex) { FrameworkLog.Error("Purchase backend IsInitialized threw: {0}", ex); return false; }
            }
        }

        public event Action<PurchaseResult> PurchaseCompleted;

        public void SetBackend(IPurchaseBackend backend)
        {
            Framework.EnsureMainThread(nameof(SetBackend));
            // 清空后回到 NotInitialized 形态，而非裸 null（避免后续判空分支遗漏导致静默成功）。
            m_Backend = backend ?? NullPurchaseBackend.Instance;
        }

        public void SetReceiptValidator(IReceiptValidator validator)
        {
            Framework.EnsureMainThread(nameof(SetReceiptValidator));
            m_Validator = validator;
        }

        public void Initialize(IReadOnlyList<PurchaseProduct> products, Action<bool> onInitialized = null)
        {
            Framework.EnsureMainThread(nameof(Initialize));

            // 登记商品目录。
            m_Products.Clear();
            m_ProductList.Clear();
            if (products != null)
            {
                for (int i = 0; i < products.Count; i++)
                {
                    PurchaseProduct p = products[i];
                    if (p == null || string.IsNullOrEmpty(p.ProductId))
                    {
                        FrameworkLog.Warning("Purchase.Initialize: skip product with empty id at index {0}.", i);
                        continue;
                    }
                    if (m_Products.ContainsKey(p.ProductId))
                    {
                        FrameworkLog.Warning("Purchase.Initialize: duplicate product id '{0}', keep first.", p.ProductId);
                        continue;
                    }
                    m_Products.Add(p.ProductId, p);
                    m_ProductList.Add(p);
                }
            }

            IPurchaseBackend backend = m_Backend;
            try
            {
                backend.Initialize(m_ProductList, ok =>
                {
                    if (!ok) FrameworkLog.Warning("Purchase backend initialize reported failure.");
                    if (onInitialized != null)
                    {
                        try { onInitialized(ok); }
                        catch (Exception ex) { FrameworkLog.Error("Purchase onInitialized callback threw: {0}", ex); }
                    }
                });
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Purchase backend Initialize threw: {0}", ex);
                if (onInitialized != null)
                {
                    try { onInitialized(false); }
                    catch (Exception cex) { FrameworkLog.Error("Purchase onInitialized callback threw: {0}", cex); }
                }
            }
        }

        public void Purchase(string productId, Action<PurchaseResult> onResult)
        {
            Framework.EnsureMainThread(nameof(Purchase));

            if (string.IsNullOrEmpty(productId))
            {
                FrameworkLog.Warning("Purchase: product id is invalid.");
                Report(new PurchaseResult { ProductId = productId, Code = PurchaseResultCode.Failed, Error = "Product id is invalid." }, onResult);
                return;
            }

            if (!IsInitialized)
            {
                FrameworkLog.Warning("Purchase('{0}'): not initialized (no real backend or backend not ready).", productId);
                Report(new PurchaseResult { ProductId = productId, Code = PurchaseResultCode.NotInitialized, Error = "Purchase backend is not initialized." }, onResult);
                return;
            }

            IPurchaseBackend backend = m_Backend;
            try
            {
                backend.Purchase(productId, result => OnBackendPurchaseResult(result, onResult));
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Purchase backend Purchase('{0}') threw: {1}", productId, ex);
                Report(new PurchaseResult { ProductId = productId, Code = PurchaseResultCode.Failed, Error = "Backend exception: " + ex.Message }, onResult);
            }
        }

        // 后端购买回调：成功且设置了校验器时插入收据校验，其余直接回报。
        private void OnBackendPurchaseResult(PurchaseResult result, Action<PurchaseResult> onResult)
        {
            IReceiptValidator validator = m_Validator;
            if (result.Code != PurchaseResultCode.Success || validator == null)
            {
                Report(result, onResult);
                return;
            }

            try
            {
                validator.Validate(result, valid =>
                {
                    if (valid)
                    {
                        Report(result, onResult);
                    }
                    else
                    {
                        FrameworkLog.Warning("Purchase('{0}'): receipt validation failed.", result.ProductId);
                        PurchaseResult invalid = result;
                        invalid.Code = PurchaseResultCode.ReceiptInvalid;
                        invalid.Error = "Receipt validation failed.";
                        Report(invalid, onResult);
                    }
                });
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Purchase receipt validator Validate threw: {0}", ex);
                PurchaseResult invalid = result;
                invalid.Code = PurchaseResultCode.ReceiptInvalid;
                invalid.Error = "Receipt validator exception: " + ex.Message;
                Report(invalid, onResult);
            }
        }

        // 统一出口：回调调用方 + 触发 PurchaseCompleted，各自隔离异常。
        private void Report(PurchaseResult result, Action<PurchaseResult> onResult)
        {
            if (onResult != null)
            {
                try { onResult(result); }
                catch (Exception ex) { FrameworkLog.Error("Purchase onResult callback threw: {0}", ex); }
            }

            Action<PurchaseResult> h = PurchaseCompleted;
            if (h != null)
            {
                try { h(result); }
                catch (Exception ex) { FrameworkLog.Error("PurchaseCompleted handler threw: {0}", ex); }
            }
        }

        public void RestorePurchases(Action<bool> onDone = null)
        {
            Framework.EnsureMainThread(nameof(RestorePurchases));

            if (!IsInitialized)
            {
                FrameworkLog.Warning("RestorePurchases: not initialized.");
                if (onDone != null)
                {
                    try { onDone(false); }
                    catch (Exception ex) { FrameworkLog.Error("RestorePurchases onDone callback threw: {0}", ex); }
                }
                return;
            }

            IPurchaseBackend backend = m_Backend;
            try
            {
                backend.RestorePurchases(ok =>
                {
                    if (onDone != null)
                    {
                        try { onDone(ok); }
                        catch (Exception ex) { FrameworkLog.Error("RestorePurchases onDone callback threw: {0}", ex); }
                    }
                });
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Purchase backend RestorePurchases threw: {0}", ex);
                if (onDone != null)
                {
                    try { onDone(false); }
                    catch (Exception cex) { FrameworkLog.Error("RestorePurchases onDone callback threw: {0}", cex); }
                }
            }
        }

        public PurchaseProduct GetProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;
            PurchaseProduct p;
            return m_Products.TryGetValue(productId, out p) ? p : null;
        }

        public IReadOnlyList<PurchaseProduct> GetProducts()
        {
            return m_ProductList;
        }

        public override void Update(float elapseSeconds, float realElapseSeconds) { }

        public override void Shutdown()
        {
            PurchaseCompleted = null;
            m_Backend = NullPurchaseBackend.Instance;
            m_Validator = null;
            m_Products.Clear();
            m_ProductList.Clear();
        }

        /// <summary>
        /// 默认空后端：未注入真实后端时的兜底。Initialize 回报 false、
        /// Purchase 回报 NotInitialized，使"无后端购买"明确失败而非静默成功。
        /// </summary>
        private sealed class NullPurchaseBackend : IPurchaseBackend
        {
            public static readonly NullPurchaseBackend Instance = new NullPurchaseBackend();

            private NullPurchaseBackend() { }

            public bool IsInitialized { get { return false; } }

            public void Initialize(IReadOnlyList<PurchaseProduct> products, Action<bool> onInitialized)
            {
                if (onInitialized != null) onInitialized(false);
            }

            public void Purchase(string productId, Action<PurchaseResult> onResult)
            {
                if (onResult != null)
                {
                    onResult(new PurchaseResult
                    {
                        ProductId = productId,
                        Code = PurchaseResultCode.NotInitialized,
                        Error = "No purchase backend has been set.",
                    });
                }
            }

            public void RestorePurchases(Action<bool> onDone)
            {
                if (onDone != null) onDone(false);
            }
        }
    }
}
