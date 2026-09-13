//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.RedDot;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 红点角标 binder。把 <see cref="IRedDotManager"/> 中某路径的激活态映射到一个 Badge GameObject 的显隐。
    ///
    /// 与其它 binder 不同：本 binder 不绑定到 ViewModel 属性，而是直接绑定到红点树的路径。
    /// 因此它不继承 BinderBase（BinderBase 依赖 MvvmView 的 Bind(IBindable) 数据上下文驱动），
    /// 而是自管 OnEnable/OnDisable 生命周期，直接向 RedDotManager 订阅/退订。
    ///
    /// 生命周期：
    ///   OnEnable  → 解析 manager + 订阅路径 + 刷新一次 Badge 显隐
    ///   OnChanged → 收到有效计数变化 → 更新 Badge 显隐
    ///   OnDisable → 退订
    /// </summary>
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/RedDot")]
    public sealed class RedDotBinder : MonoBehaviour
    {
        [SerializeField, Tooltip("红点树路径，'/' 分隔（例如 Mail/System）。")]
        private string m_Path;

        [SerializeField, Tooltip("要显隐的角标 GameObject。激活态显示，非激活态隐藏。")]
        private GameObject m_Badge;

        private IRedDotManager m_RedDotManager;
        private Action<int> m_OnChanged;
        private bool m_Subscribed;

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(m_Path))
            {
                FrameworkLog.Warning("[RedDotBinder] path is empty on '{0}'.", name);
                return;
            }

            if (m_RedDotManager == null) m_RedDotManager = Framework.GetModule<IRedDotManager>();
            if (m_RedDotManager == null)
            {
                FrameworkLog.Error("[RedDotBinder] RedDot manager is invalid.");
                return;
            }

            if (m_OnChanged == null) m_OnChanged = OnCountChanged;
            if (!m_Subscribed)
            {
                m_RedDotManager.Subscribe(m_Path, m_OnChanged);
                m_Subscribed = true;
            }

            // 立即同步当前状态（订阅本身不回调初始值）。
            ApplyActive(m_RedDotManager.IsActive(m_Path));
        }

        private void OnDisable()
        {
            if (m_Subscribed && m_RedDotManager != null)
            {
                m_RedDotManager.Unsubscribe(m_Path, m_OnChanged);
                m_Subscribed = false;
            }
        }

        private void OnCountChanged(int newCount)
        {
            ApplyActive(newCount > 0);
        }

        private void ApplyActive(bool active)
        {
            if (m_Badge != null && m_Badge.activeSelf != active)
            {
                m_Badge.SetActive(active);
            }
        }
    }
}
