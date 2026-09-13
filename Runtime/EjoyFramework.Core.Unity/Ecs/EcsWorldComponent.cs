using System;
using EjoyFramework.Core.Ecs;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>场景拥有的 ECS 世界及系统组。禁用组件暂停推进，销毁组件释放世界。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/ECS World")]
    public sealed class EcsWorldComponent : MonoBehaviour
    {
        [SerializeField] private EcsUpdateMode m_UpdateMode = EcsUpdateMode.FixedUpdate;
        private World m_World;
        private SystemGroup m_Systems;
        private bool m_Destroyed;

        public EcsUpdateMode UpdateMode { get => m_UpdateMode; set => m_UpdateMode = value; }
        public World World => m_World ?? throw new InvalidOperationException("ECS world has not been initialized.");
        public SystemGroup Systems => m_Systems ?? throw new InvalidOperationException("ECS world has not been initialized.");
        public bool IsInitialized => m_World != null;

        private void Awake() => Initialize();

        public void Initialize()
        {
            if (m_Destroyed) throw new ObjectDisposedException(nameof(EcsWorldComponent));
            if (m_World != null) return;
            m_World = new World();
            m_Systems = new SystemGroup(m_World);
        }

        /// <summary>显式推进；自动模式也使用同一入口。异常时暂停自动推进，保留世界供检查。</summary>
        public void Step(float deltaTime)
        {
            try { Systems.Update(deltaTime); }
            catch { enabled = false; throw; }
        }

        private void Update()
        {
            if (m_World != null && m_UpdateMode == EcsUpdateMode.Update) Step(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (m_World != null && m_UpdateMode == EcsUpdateMode.FixedUpdate) Step(Time.fixedDeltaTime);
        }

        public void Shutdown()
        {
            if (m_World == null) return;
            // World checks active iteration first: a rejected shutdown must leave Systems usable.
            m_World.Dispose();
            m_Systems.Dispose();
            m_Systems = null;
            m_World = null;
        }

        private void OnDestroy()
        {
            Shutdown();
            m_Destroyed = true;
        }
    }
}
