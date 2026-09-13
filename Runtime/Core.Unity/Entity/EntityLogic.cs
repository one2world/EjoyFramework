//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Entity;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 实体逻辑基类。
    /// </summary>
    public abstract class EntityLogic : MonoBehaviour
    {
        private bool m_Available = false;
        private bool m_Visible = false;
        private bool m_Inited = false;     // 该实例是否已 OnInit（跨池复用保持，避免重复初始化）
        private int m_OriginalLayer = 0;
        private Transform m_OriginalTransform = null;

        // ===== 框架内部生命周期驱动（由 IEntityHelper 调用，保证 Init→(Show→Hide)*→Recycle 不变量）=====

        internal void InternalOnInit(object userData)
        {
            if (m_Inited) return;          // 池复用时不重复 OnInit
            m_Inited = true;
            OnInit(userData);
        }

        internal void InternalOnShow(object userData) { OnShow(userData); }
        internal void InternalOnHide(bool isShutdown, object userData) { OnHide(isShutdown, userData); }
        internal void InternalOnRecycle() { OnRecycle(); m_Inited = false; }
        internal void InternalOnUpdate(float elapseSeconds, float realElapseSeconds) { OnUpdate(elapseSeconds, realElapseSeconds); }
        internal void InternalOnAttached(EntityLogic child, Transform parentTransform, object userData) { OnAttached(child, parentTransform, userData); }
        internal void InternalOnDetached(EntityLogic child, object userData) { OnDetached(child, userData); }

        /// <summary>
        /// 获取实体。
        /// </summary>
        public IEntity Entity
        {
            get;
            internal set;
        }

        /// <summary>
        /// 获取或设置实体名称。
        /// </summary>
        public string Name
        {
            get { return gameObject.name; }
            set { gameObject.name = value; }
        }

        /// <summary>
        /// 获取实体是否可用。
        /// </summary>
        public bool Available
        {
            get { return m_Available; }
        }

        /// <summary>
        /// 获取或设置实体是否可见。
        /// </summary>
        public bool Visible
        {
            get { return m_Visible; }
            set
            {
                if (m_Visible == value)
                {
                    return;
                }

                m_Visible = value;
                InternalSetVisible(value);
            }
        }

        /// <summary>
        /// 获取已缓存的 Transform。
        /// </summary>
        public Transform CachedTransform
        {
            get;
            private set;
        }

        /// <summary>
        /// 实体初始化。
        /// </summary>
        /// <param name="userData">用户自定义数据。</param>
        protected internal virtual void OnInit(object userData)
        {
            CachedTransform = transform;
            m_OriginalLayer = gameObject.layer;
            m_OriginalTransform = CachedTransform.parent;
        }

        /// <summary>
        /// 实体回收。
        /// </summary>
        protected internal virtual void OnRecycle()
        {
        }

        /// <summary>
        /// 实体显示。
        /// </summary>
        /// <param name="userData">用户自定义数据。</param>
        protected internal virtual void OnShow(object userData)
        {
            m_Available = true;
            Visible = true;
        }

        /// <summary>
        /// 实体隐藏。
        /// </summary>
        /// <param name="isShutdown">是否是关闭实体管理器时触发。</param>
        /// <param name="userData">用户自定义数据。</param>
        protected internal virtual void OnHide(bool isShutdown, object userData)
        {
            gameObject.SetActive(false);
            m_Available = false;
            Visible = false;
        }

        /// <summary>
        /// 实体附加子实体。
        /// </summary>
        /// <param name="childEntity">附加的子实体。</param>
        /// <param name="userData">用户自定义数据。</param>
        protected internal virtual void OnAttached(EntityLogic childEntity, Transform parentTransform, object userData)
        {
        }

        /// <summary>
        /// 实体解除子实体。
        /// </summary>
        /// <param name="childEntity">解除的子实体。</param>
        /// <param name="userData">用户自定义数据。</param>
        protected internal virtual void OnDetached(EntityLogic childEntity, object userData)
        {
        }

        /// <summary>
        /// 实体轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        protected internal virtual void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 设置实体的可见性。
        /// </summary>
        /// <param name="visible">实体的可见性。</param>
        protected virtual void InternalSetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }
    }
}
