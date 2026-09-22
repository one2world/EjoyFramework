//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Scene;
using EjoyFramework.Core.Streaming;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 世界分区流送组件：Inspector 配置 + 观察者 Transform 绑定 + 浮动原点桥接 + 内置场景处理器开关。
    ///
    /// 用法：
    ///   1) 挂到框架根对象，配置 CellSize / 预算；按层 <see cref="ConfigureLayer"/>；
    ///   2) <see cref="BindObserver"/>(id, transform)——每帧 LateUpdate 把 Transform 的 XZ 推给管理器；
    ///   3) 单元登记（通常在关卡进入时从 ConfigBlob 表批量 <see cref="Streaming"/>.RegisterCell）；
    ///   4) 要么 <see cref="UseSceneHandler"/>（每单元一个附加场景），要么 <see cref="Streaming"/>.SetHandler 接自定义处理器。
    ///
    /// 浮动原点：业务做原点平移时调用 <see cref="ShiftOrigin"/>（把世界原点在场景空间的偏移交给管理器），
    /// 绑定的观察者 Transform 继续用场景本地坐标即可。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/WorldStreaming")]
    public sealed class WorldStreamingComponent : GameFrameworkComponent
    {
        private struct BoundObserver
        {
            public int Id;
            public Transform Transform;
        }

        [SerializeField]
        [Tooltip("单元边长（世界单位）。")]
        private float m_CellSize = 64f;

        [SerializeField]
        [Tooltip("每帧最多启动的加载数。")]
        private int m_MaxLoadStartsPerFrame = 4;

        [SerializeField]
        [Tooltip("同时在途的最大加载数。")]
        private int m_MaxLoadsInFlight = 8;

        [SerializeField]
        [Tooltip("每帧最多启动的卸载数。")]
        private int m_MaxUnloadsPerFrame = 4;

        [SerializeField]
        [Tooltip("观察者移动超过该距离才重新评估；0 = 使用 CellSize/4。")]
        private float m_ReevaluateMoveThreshold = 0f;

        private IWorldStreamingManager m_Streaming;
        private readonly List<BoundObserver> m_Observers = new List<BoundObserver>();
        private SceneStreamingHandler m_SceneHandler;

        protected override void Awake()
        {
            base.Awake();
            m_Streaming = Framework.GetModule<IWorldStreamingManager>();
            if (m_Streaming == null)
            {
                Log.Fatal("World streaming manager is invalid.");
                return;
            }

            m_Streaming.CellSize = m_CellSize;
            m_Streaming.MaxLoadStartsPerFrame = m_MaxLoadStartsPerFrame;
            m_Streaming.MaxLoadsInFlight = m_MaxLoadsInFlight;
            m_Streaming.MaxUnloadsPerFrame = m_MaxUnloadsPerFrame;
            if (m_ReevaluateMoveThreshold > 0f) m_Streaming.ReevaluateMoveThreshold = m_ReevaluateMoveThreshold;
        }

        private void LateUpdate()
        {
            for (int i = m_Observers.Count - 1; i >= 0; i--)
            {
                Transform t = m_Observers[i].Transform;
                if (t == null)
                {
                    m_Streaming.RemoveObserver(m_Observers[i].Id);
                    m_Observers.RemoveAt(i);
                    continue;
                }

                Vector3 p = t.position;
                m_Streaming.SetObserver(m_Observers[i].Id, p.x, p.z);
            }
        }

        protected override void OnDestroy()
        {
            try
            {
                if (m_SceneHandler != null)
                {
                    m_SceneHandler.Dispose();
                    m_SceneHandler = null;
                }
            }
            finally
            {
                base.OnDestroy();
            }
        }

        /// <summary>底层管理器（登记单元、查询状态、自定义处理器）。</summary>
        public IWorldStreamingManager Streaming
        {
            get { return m_Streaming; }
        }

        /// <summary>配置层。</summary>
        public void ConfigureLayer(int layer, float loadRadius, float unloadRadius, float[] lodDistances = null, int priorityBias = 0)
        {
            m_Streaming.ConfigureLayer(layer, new StreamingLayerSettings
            {
                LoadRadius = loadRadius,
                UnloadRadius = unloadRadius,
                LodDistances = lodDistances,
                PriorityBias = priorityBias,
            });
        }

        /// <summary>绑定观察者 Transform（每帧自动推位置）。同 id 重复绑定替换 Transform。</summary>
        public void BindObserver(int observerId, Transform observer)
        {
            if (observer == null)
            {
                throw new FrameworkException("WorldStreamingComponent.BindObserver：observer 不能为 null。");
            }

            for (int i = 0; i < m_Observers.Count; i++)
            {
                if (m_Observers[i].Id == observerId)
                {
                    m_Observers[i] = new BoundObserver { Id = observerId, Transform = observer };
                    return;
                }
            }

            m_Observers.Add(new BoundObserver { Id = observerId, Transform = observer });
            Vector3 p = observer.position;
            m_Streaming.SetObserver(observerId, p.x, p.z);
        }

        /// <summary>解绑并移除观察者。</summary>
        public bool UnbindObserver(int observerId)
        {
            for (int i = 0; i < m_Observers.Count; i++)
            {
                if (m_Observers[i].Id != observerId) continue;
                m_Observers.RemoveAt(i);
                return m_Streaming.RemoveObserver(observerId);
            }

            return false;
        }

        /// <summary>浮动原点平移（业务把场景整体平移 -delta 时，调用 ShiftOrigin(delta)）。</summary>
        public void ShiftOrigin(Vector3 delta)
        {
            m_Streaming.ShiftOrigin(delta.x, delta.z);
        }

        /// <summary>启用内置"每单元一个附加场景"处理器。resolver 把内容键映射为场景资源名。</summary>
        public SceneStreamingHandler UseSceneHandler(SceneStreamingHandler.ISceneNameResolver resolver)
        {
            ISceneManager scenes = Framework.GetModule<ISceneManager>();
            if (scenes == null)
            {
                throw new FrameworkException("WorldStreamingComponent.UseSceneHandler：SceneManager 未注册。");
            }

            if (m_SceneHandler != null) m_SceneHandler.Dispose();
            m_SceneHandler = new SceneStreamingHandler(m_Streaming, scenes, resolver);
            m_Streaming.SetHandler(m_SceneHandler);
            return m_SceneHandler;
        }
    }
}
