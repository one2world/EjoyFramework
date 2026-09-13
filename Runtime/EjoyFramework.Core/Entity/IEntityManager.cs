//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Entity
{
    /// <summary>
    /// 实体管理器接口。
    /// </summary>
    public interface IEntityManager
    {
        /// <summary>
        /// 获取实体数量。
        /// </summary>
        int EntityCount { get; }

        /// <summary>
        /// 获取实体组数量。
        /// </summary>
        int EntityGroupCount { get; }

        /// <summary>
        /// 是否存在实体组。
        /// </summary>
        bool HasEntityGroup(string entityGroupName);

        /// <summary>
        /// 获取实体组。
        /// </summary>
        IEntityGroup GetEntityGroup(string entityGroupName);

        /// <summary>
        /// 获取所有实体组。
        /// </summary>
        IEntityGroup[] GetAllEntityGroups();

        /// <summary>
        /// 增加实体组。
        /// </summary>
        bool AddEntityGroup(string entityGroupName, float instanceAutoReleaseInterval, int instanceCapacity, float instanceExpireTime, int instancePriority, IEntityGroupHelper entityGroupHelper);

        /// <summary>
        /// 是否存在实体。
        /// </summary>
        bool HasEntity(int entityId);

        /// <summary>
        /// 是否存在实体。
        /// </summary>
        bool HasEntity(string entityAssetName);

        /// <summary>
        /// 获取实体。
        /// </summary>
        IEntity GetEntity(int entityId);

        /// <summary>
        /// 获取实体。
        /// </summary>
        IEntity[] GetEntities(string entityAssetName);

        /// <summary>
        /// 获取所有已加载的实体。
        /// </summary>
        IEntity[] GetAllLoadedEntities();

        /// <summary>
        /// 显示实体。
        /// </summary>
        void ShowEntity(int entityId, string entityAssetName, string entityGroupName, int priority, object userData);

        /// <summary>
        /// 隐藏实体。
        /// </summary>
        void HideEntity(int entityId);

        /// <summary>
        /// 隐藏实体。
        /// </summary>
        void HideEntity(int entityId, object userData);

        /// <summary>
        /// 隐藏所有已加载的实体。
        /// </summary>
        void HideAllLoadedEntities();

        /// <summary>
        /// 隐藏所有正在加载的实体。
        /// </summary>
        void HideAllLoadingEntities();

        /// <summary>
        /// 把子实体附加到父实体（驱动父实体的 OnAttached）。两者都须已加载。
        /// </summary>
        void AttachEntity(int childEntityId, int parentEntityId, object userData);

        /// <summary>
        /// 解除子实体与其父实体的附加关系（驱动父实体的 OnDetached）。
        /// </summary>
        void DetachEntity(int childEntityId, object userData);

        /// <summary>
        /// 设置实体辅助器。
        /// </summary>
        void SetEntityHelper(IEntityHelper entityHelper);

        /// <summary>
        /// 显示实体成功事件。
        /// </summary>
        event EventHandler<ShowEntitySuccessEventArgs> ShowEntitySuccess;

        /// <summary>
        /// 显示实体失败事件。
        /// </summary>
        event EventHandler<ShowEntityFailureEventArgs> ShowEntityFailure;

        /// <summary>
        /// 隐藏实体完成事件。
        /// </summary>
        event EventHandler<HideEntityCompleteEventArgs> HideEntityComplete;
    }

    /// <summary>
    /// 实体接口。
    /// </summary>
    public interface IEntity
    {
        /// <summary>
        /// 获取实体编号。
        /// </summary>
        int Id { get; }

        /// <summary>
        /// 获取实体资源名称。
        /// </summary>
        string EntityAssetName { get; }

        /// <summary>
        /// 获取实体实例。
        /// </summary>
        object Handle { get; }

        /// <summary>
        /// 获取实体所属的实体组。
        /// </summary>
        IEntityGroup EntityGroup { get; }
    }

    /// <summary>
    /// 实体组接口。
    /// </summary>
    public interface IEntityGroup
    {
        /// <summary>
        /// 获取实体组名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取实体组中实体数量。
        /// </summary>
        int EntityCount { get; }
    }

    /// <summary>
    /// 实体辅助器接口。
    /// </summary>
    public interface IEntityHelper
    {
        /// <summary>
        /// 实例化实体。
        /// </summary>
        object InstantiateEntity(object entityAsset);

        /// <summary>
        /// 创建实体（首次实例化或池复用都会调用；触发 OnInit[仅首次]+OnShow）。
        /// </summary>
        IEntity CreateEntity(object entityInstance, IEntityGroup entityGroup, object userData);

        /// <summary>
        /// 隐藏实体（归还池前调用，触发 OnHide）。
        /// </summary>
        void HideEntity(IEntity entity, bool isShutdown, object userData);

        /// <summary>
        /// 轮询实体（每帧由 EntityManager.Update 驱动，触发 OnUpdate）。
        /// </summary>
        void UpdateEntity(IEntity entity, float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 把子实体附加到父实体（触发父的 OnAttached）。
        /// </summary>
        void AttachEntity(IEntity child, IEntity parent, object userData);

        /// <summary>
        /// 解除子实体（触发父的 OnDetached）。
        /// </summary>
        void DetachEntity(IEntity child, IEntity parent, object userData);

        /// <summary>
        /// 释放实体（池销毁时调用，触发 OnRecycle 后销毁实例）。
        /// </summary>
        void ReleaseEntity(object entityAsset, object entityInstance);
    }

    /// <summary>
    /// 实体组辅助器接口。
    /// </summary>
    public interface IEntityGroupHelper
    {
    }

    /// <summary>
    /// 显示实体成功事件。
    /// </summary>
    public sealed class ShowEntitySuccessEventArgs : FrameworkEventArgs
    {
        public int EntityId { get; private set; }
        public string EntityAssetName { get; private set; }
        public IEntityGroup EntityGroup { get; private set; }
        public float Duration { get; private set; }
        public object UserData { get; private set; }
        public IEntity Entity { get; private set; }

        public override void Clear()
        {
            EntityId = 0;
            EntityAssetName = null;
            EntityGroup = null;
            Duration = 0f;
            UserData = null;
            Entity = null;
        }

        public static ShowEntitySuccessEventArgs Create(int entityId, string entityAssetName, IEntityGroup entityGroup, float duration, object userData, IEntity entity)
        {
            ShowEntitySuccessEventArgs e = ReferencePool.Acquire<ShowEntitySuccessEventArgs>();
            e.EntityId = entityId;
            e.EntityAssetName = entityAssetName;
            e.EntityGroup = entityGroup;
            e.Duration = duration;
            e.UserData = userData;
            e.Entity = entity;
            return e;
        }
    }

    /// <summary>
    /// 显示实体失败事件。
    /// </summary>
    public sealed class ShowEntityFailureEventArgs : FrameworkEventArgs
    {
        public int EntityId { get; private set; }
        public string EntityAssetName { get; private set; }
        public string EntityGroupName { get; private set; }
        public string ErrorMessage { get; private set; }
        public object UserData { get; private set; }

        public override void Clear()
        {
            EntityId = 0;
            EntityAssetName = null;
            EntityGroupName = null;
            ErrorMessage = null;
            UserData = null;
        }

        public static ShowEntityFailureEventArgs Create(int entityId, string entityAssetName, string entityGroupName, string errorMessage, object userData)
        {
            ShowEntityFailureEventArgs e = ReferencePool.Acquire<ShowEntityFailureEventArgs>();
            e.EntityId = entityId;
            e.EntityAssetName = entityAssetName;
            e.EntityGroupName = entityGroupName;
            e.ErrorMessage = errorMessage;
            e.UserData = userData;
            return e;
        }
    }

    /// <summary>
    /// 隐藏实体完成事件。
    /// </summary>
    public sealed class HideEntityCompleteEventArgs : FrameworkEventArgs
    {
        public int EntityId { get; private set; }
        public string EntityAssetName { get; private set; }
        public IEntityGroup EntityGroup { get; private set; }
        public object UserData { get; private set; }

        public override void Clear()
        {
            EntityId = 0;
            EntityAssetName = null;
            EntityGroup = null;
            UserData = null;
        }

        public static HideEntityCompleteEventArgs Create(int entityId, string entityAssetName, IEntityGroup entityGroup, object userData)
        {
            HideEntityCompleteEventArgs e = ReferencePool.Acquire<HideEntityCompleteEventArgs>();
            e.EntityId = entityId;
            e.EntityAssetName = entityAssetName;
            e.EntityGroup = entityGroup;
            e.UserData = userData;
            return e;
        }
    }
}
