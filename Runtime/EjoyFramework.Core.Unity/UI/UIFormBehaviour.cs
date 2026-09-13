//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.UI;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// UIForm 抽象基类 —— Framework 内部 IUIForm 实现，负责标准 form 生命周期与 RectTransform/SafeArea 初始化。
    ///
    /// 业务**不应直接继承本类**。Framework 唯一对外 form 基类是 <see cref="MvvmView{TViewModel}"/>；
    /// MvvmView 在此基类之上叠加 ViewModel 生命周期与 binder Bind/Unbind 调度。
    /// </summary>
    public abstract class UIFormBehaviour : MonoBehaviour, IUIForm
    {
        public int SerialId { get; private set; }
        public string UIFormAssetName { get; private set; }
        public object Handle { get { return gameObject; } }
        public IUIGroup UIGroup { get; private set; }
        public int DepthInUIGroup { get; private set; }
        public bool PauseCoveredUIForm { get; private set; }

        /// <summary>
        /// 上一次 OnPause/OnCover/OnReveal/OnResume 的 userData（业务从 OnResume 想要 refresh 时用）。
        /// 由 UIController.RefocusUIForm(form, userData) 等路径在状态切换时设置。
        /// </summary>
        protected object LastUserData { get; private set; }

        public void OnInit(int serialId, string uiFormAssetName, IUIGroup uiGroup, bool pauseCoveredUIForm, bool isNewInstance, object userData)
        {
            SerialId = serialId;
            UIFormAssetName = uiFormAssetName;
            UIGroup = uiGroup;
            PauseCoveredUIForm = pauseCoveredUIForm;
            LastUserData = userData;

            ApplyDefaultRect();

            try
            {
                if (isNewInstance) OnFirstInit(userData);
                else OnReuseInit(userData);
            }
            catch (System.Exception ex)
            {
                FrameworkLog.Error("[UIFormBehaviour] {0} on '{1}' threw: {2}",
                    isNewInstance ? "OnFirstInit" : "OnReuseInit", GetType().Name, ex);
            }
        }

        /// <summary>第一次新实例化时调用一次。MvvmView 在此构造 ViewModel + Bind 所有 binder。</summary>
        protected abstract void OnFirstInit(object userData);

        /// <summary>实例池复用时每次调用。MvvmView 在此重新 Bind 所有 binder。</summary>
        protected abstract void OnReuseInit(object userData);

        public virtual void OnRecycle()
        {
            // 实例归池前的钩子。MvvmView 在此清空 ViewModel 引用。
        }

        public virtual void OnOpen(object userData) { gameObject.SetActive(true); LastUserData = userData; }
        public virtual void OnClose(bool isShutdown, object userData) { gameObject.SetActive(false); }
        public virtual void OnPause() { gameObject.SetActive(false); }
        public virtual void OnResume() { gameObject.SetActive(true); }
        public virtual void OnCover() { }
        public virtual void OnReveal() { }
        public virtual void OnRefocus(object userData) { LastUserData = userData; }
        public virtual void OnUpdate(float elapseSeconds, float realElapseSeconds) { }
        public virtual void OnDepthChanged(int uiGroupDepth, int depthInUIGroup)
        {
            DepthInUIGroup = depthInUIGroup;
            var t = transform;
            var pos = t.localPosition;
            t.localPosition = new Vector3(pos.x, pos.y, depthInUIGroup * -1f);
        }

        /// <summary>默认让 RectTransform 全屏拉伸。业务子类可 override。</summary>
        protected virtual void ApplyDefaultRect()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

    }
}
