//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Purchase;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 内购组件。转发到 <see cref="IPurchaseManager"/>。
    /// 真实平台后端（如 Unity Purchasing）与收据校验器由业务在运行期通过
    /// <see cref="SetBackend"/> / <see cref="SetReceiptValidator"/> 注入。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Purchase")]
    public sealed class PurchaseComponent : GameFrameworkComponent
    {
        private IPurchaseManager m_PurchaseManager;

        protected override void Awake()
        {
            base.Awake();
            m_PurchaseManager = Framework.GetModule<IPurchaseManager>();
            if (m_PurchaseManager == null)
            {
                Log.Fatal("Purchase manager is invalid.");
                return;
            }
        }

        /// <summary>
        /// 是否已初始化（已注入后端且后端初始化成功）。
        /// </summary>
        public bool IsInitialized
        {
            get { return m_PurchaseManager.IsInitialized; }
        }

        /// <summary>
        /// 购买完成事件（每笔购买结果均会触发）。
        /// </summary>
        public event Action<PurchaseResult> PurchaseCompleted
        {
            add { m_PurchaseManager.PurchaseCompleted += value; }
            remove { m_PurchaseManager.PurchaseCompleted -= value; }
        }

        /// <summary>
        /// 设置平台后端。传 null 表示清除。
        /// </summary>
        public void SetBackend(IPurchaseBackend backend)
        {
            m_PurchaseManager.SetBackend(backend);
        }

        /// <summary>
        /// 设置收据校验器。传 null 表示清除。
        /// </summary>
        public void SetReceiptValidator(IReceiptValidator validator)
        {
            m_PurchaseManager.SetReceiptValidator(validator);
        }

        /// <summary>
        /// 初始化：登记商品目录并驱动后端初始化。
        /// </summary>
        public void Initialize(IReadOnlyList<PurchaseProduct> products, Action<bool> onInitialized = null)
        {
            m_PurchaseManager.Initialize(products, onInitialized);
        }

        /// <summary>
        /// 发起购买。
        /// </summary>
        public void Purchase(string productId, Action<PurchaseResult> onResult)
        {
            m_PurchaseManager.Purchase(productId, onResult);
        }

        /// <summary>
        /// 恢复购买。
        /// </summary>
        public void RestorePurchases(Action<bool> onDone = null)
        {
            m_PurchaseManager.RestorePurchases(onDone);
        }

        /// <summary>
        /// 取某个商品描述。不存在时返回 null。
        /// </summary>
        public PurchaseProduct GetProduct(string productId)
        {
            return m_PurchaseManager.GetProduct(productId);
        }

        /// <summary>
        /// 取全部已登记商品（只读快照）。
        /// </summary>
        public IReadOnlyList<PurchaseProduct> GetProducts()
        {
            return m_PurchaseManager.GetProducts();
        }
    }
}
