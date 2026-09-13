using System;
using EjoyFramework.Core.Ecs;
using EcsEntity = EjoyFramework.Core.Ecs.Entity;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 表现对象到 ECS 句柄的显式绑定。回收表现对象不销毁模拟实体；池化 OnHide 时调用 Unbind。
    /// Transform 与业务组件的同步由游戏表现层负责。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/ECS Entity View")]
    public sealed class EcsEntityView : MonoBehaviour
    {
        private World m_World;
        public EcsEntity Entity { get; private set; }
        public bool IsBound => m_World != null && m_World.IsAlive(Entity);
        public World World => m_World;

        public void Bind(World world, EcsEntity entity)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsAlive(entity)) throw new InvalidOperationException("View requires a live entity in the supplied world.");
            m_World = world;
            Entity = entity;
        }

        public void Unbind()
        {
            m_World = null;
            Entity = default;
        }

        private void OnDestroy() => Unbind();
    }
}
