//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.ObjectPool;

namespace EjoyFramework.Core.Entity
{
    /// <summary>
    /// 实体实例对象。包装由 IEntityHelper 实例化的实体实例（如 Unity GameObject），
    /// 注册进 ObjectPool 实现复用。
    /// Release（池销毁）时：通过 helper 销毁实例 + 通过 ResourceManager 卸载底层 asset 引用。
    /// </summary>
    internal sealed class EntityInstanceObject : ObjectBase
    {
        private object m_EntityAsset;
        private IEntityHelper m_EntityHelper;
        private Resource.IResourceManager m_ResourceManager;

        public EntityInstanceObject() { }

        public static EntityInstanceObject Create(string name, object entityAsset, object entityInstance,
            IEntityHelper entityHelper, Resource.IResourceManager resourceManager)
        {
            if (entityAsset == null) throw new FrameworkException("Entity asset is invalid.");
            if (entityHelper == null) throw new FrameworkException("Entity helper is invalid.");

            EntityInstanceObject obj = ReferencePool.Acquire<EntityInstanceObject>();
            obj.InitializeInternal(name, entityInstance, entityAsset, entityHelper, resourceManager);
            return obj;
        }

        private void InitializeInternal(string name, object target, object asset, IEntityHelper helper, Resource.IResourceManager resMgr)
        {
            base.Initialize(name, target);
            m_EntityAsset = asset;
            m_EntityHelper = helper;
            m_ResourceManager = resMgr;
        }

        public override void Clear()
        {
            base.Clear();
            m_EntityAsset = null;
            m_EntityHelper = null;
            m_ResourceManager = null;
        }

        protected internal override void Release(bool isShutdown)
        {
            try { m_EntityHelper.ReleaseEntity(m_EntityAsset, Target); }
            catch (System.Exception ex) { FrameworkLog.Error("EntityHelper.ReleaseEntity threw: {0}", ex); }

            if (m_ResourceManager != null && m_EntityAsset != null)
            {
                try { m_ResourceManager.UnloadAsset(m_EntityAsset); }
                catch (System.Exception ex) { FrameworkLog.Error("ResourceManager.UnloadAsset threw: {0}", ex); }
            }
        }
    }
}
