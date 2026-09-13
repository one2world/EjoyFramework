//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.ObjectPool;

namespace EjoyFramework.Core.UI
{
    /// <summary>
    /// 界面实例对象。包装由 IUIFormHelper 实例化出的 UI 实例（如 Unity GameObject），
    /// 注册进 ObjectPool 实现复用。Release 时通过 helper 销毁实例 + 调 ResourceManager.UnloadAsset 释放底层资源。
    /// 修复：之前只销毁 instance、不卸载 asset，导致长跑 prefab 资源泄漏。
    /// </summary>
    internal sealed class UIFormInstanceObject : ObjectBase
    {
        private object m_UIFormAsset;
        private IUIFormHelper m_UIFormHelper;
        private Resource.IResourceManager m_ResourceManager;

        public UIFormInstanceObject() { }

        public static UIFormInstanceObject Create(string name, object uiFormAsset, object uiFormInstance,
            IUIFormHelper uiFormHelper, Resource.IResourceManager resourceManager)
        {
            if (uiFormAsset == null) throw new FrameworkException("UI form asset is invalid.");
            if (uiFormHelper == null) throw new FrameworkException("UI form helper is invalid.");

            UIFormInstanceObject obj = ReferencePool.Acquire<UIFormInstanceObject>();
            obj.InitializeInternal(name, uiFormInstance, uiFormAsset, uiFormHelper, resourceManager);
            return obj;
        }

        private void InitializeInternal(string name, object target, object asset, IUIFormHelper helper, Resource.IResourceManager resMgr)
        {
            base.Initialize(name, target);
            m_UIFormAsset = asset;
            m_UIFormHelper = helper;
            m_ResourceManager = resMgr;
        }

        public override void Clear()
        {
            base.Clear();
            m_UIFormAsset = null;
            m_UIFormHelper = null;
            m_ResourceManager = null;
        }

        protected internal override void Release(bool isShutdown)
        {
            // 1) 销毁实例（GameObject）
            try { m_UIFormHelper.ReleaseUIForm(m_UIFormAsset, Target); }
            catch (System.Exception ex) { FrameworkLog.Error("UIFormHelper.ReleaseUIForm threw: {0}", ex); }

            // 2) 卸载 asset（prefab 引用），避免长跑泄漏
            if (m_ResourceManager != null && m_UIFormAsset != null)
            {
                try { m_ResourceManager.UnloadAsset(m_UIFormAsset); }
                catch (System.Exception ex) { FrameworkLog.Error("ResourceManager.UnloadAsset threw: {0}", ex); }
            }
        }
    }
}
