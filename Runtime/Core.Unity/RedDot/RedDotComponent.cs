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
    /// 红点组件。封装 <see cref="IRedDotManager"/> 供 Unity 层调用。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/RedDot")]
    public sealed class RedDotComponent : GameFrameworkComponent
    {
        private IRedDotManager m_RedDotManager;

        protected override void Awake()
        {
            base.Awake();
            m_RedDotManager = Framework.GetModule<IRedDotManager>();
            if (m_RedDotManager == null)
            {
                Log.Fatal("RedDot manager is invalid.");
                return;
            }
        }

        /// <summary>
        /// 设置某个叶子路径的自身计数，并向上传播刷新祖先聚合计数。
        /// </summary>
        public void SetLeafCount(string path, int count)
        {
            m_RedDotManager.SetLeafCount(path, count);
        }

        /// <summary>
        /// 获取节点的有效计数（叶子为自身计数；内部节点为后代叶子计数之和）。
        /// </summary>
        public int GetCount(string path)
        {
            return m_RedDotManager.GetCount(path);
        }

        /// <summary>
        /// 节点是否激活（有效计数 &gt; 0）。
        /// </summary>
        public bool IsActive(string path)
        {
            return m_RedDotManager.IsActive(path);
        }

        /// <summary>
        /// 订阅节点有效计数变化。
        /// </summary>
        public void Subscribe(string path, Action<int> onChanged)
        {
            m_RedDotManager.Subscribe(path, onChanged);
        }

        /// <summary>
        /// 取消订阅节点有效计数变化。
        /// </summary>
        public void Unsubscribe(string path, Action<int> onChanged)
        {
            m_RedDotManager.Unsubscribe(path, onChanged);
        }

        /// <summary>
        /// 清空整棵红点树。
        /// </summary>
        public void Clear()
        {
            m_RedDotManager.Clear();
        }
    }
}
