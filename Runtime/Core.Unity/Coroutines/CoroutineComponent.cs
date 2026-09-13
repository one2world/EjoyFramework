//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using EjoyFramework.Core.Coroutines;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 协程组件（可选）。完全转发给 ICoroutineManager 上的方法；保留 Inspector 入口
    /// 便于 DebuggerComponent 统一观察。
    ///
    /// 注入策略变更（2026-05）：BaseComponent.Awake 自身已经把 UnityCoroutineHelper
    /// 注入到 ICoroutineManager 上，host 是 BaseComponent 本身（DontDestroyOnLoad，生命周期最稳）。
    /// 因此本组件 <b>不再</b> 是必需品；如果场景里同时存在，本组件的 Awake 会检测到
    /// HasHelper=true 并跳过 SetHelper，避免覆盖 BaseComponent 的更稳的 host。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Coroutine")]
    public sealed class CoroutineComponent : GameFrameworkComponent
    {
        private ICoroutineManager m_CoroutineManager;
        private UnityCoroutineHelper m_Helper;

        protected override void Awake()
        {
            base.Awake();
            m_CoroutineManager = Framework.GetModule<ICoroutineManager>();
            if (m_CoroutineManager == null) { Log.Fatal("Coroutine manager is invalid."); return; }

            // BaseComponent installs the canonical helper from its own Awake (it has
            // DefaultExecutionOrder(-10000) + DontDestroyOnLoad — strongest lifetime guarantees).
            // Don't overwrite. Only install if for some reason BaseComponent didn't get there first
            // (e.g. invalid scene setup without BaseComponent) — degraded but still functional.
            if (!m_CoroutineManager.HasHelper)
            {
                m_Helper = new UnityCoroutineHelper(this);
                m_CoroutineManager.SetHelper(m_Helper);
            }
        }

        protected override void OnDestroy()
        {
            // 仅当本组件确实安装了 helper（即没有 BaseComponent 抢先安装）时，才负责停掉所有协程。
            // 否则 helper 归 BaseComponent（DontDestroyOnLoad）所有，本组件随场景卸载时 StopAll()
            // 会误杀由 BaseComponent 驱动的协程（资源加载/场景挂载/音频淡入等）。
            try
            {
                if (m_Helper != null) m_CoroutineManager?.StopAll();
            }
            catch (Exception ex) { Log.Error("CoroutineComponent.StopAll threw: {0}", ex); }
            base.OnDestroy();
        }

        public int RunningCount { get { return m_CoroutineManager != null ? m_CoroutineManager.RunningCount : 0; } }

        public CoroutineHandle Run(IEnumerator routine, string tag, object owner = null)
        {
            if (m_CoroutineManager == null) { Log.Error("Coroutine manager not ready."); return default; }
            return m_CoroutineManager.Run(routine, tag, owner);
        }

        public bool Stop(CoroutineHandle h)
        {
            return m_CoroutineManager != null && m_CoroutineManager.Stop(h);
        }

        public int StopByOwner(object owner)
        {
            return m_CoroutineManager != null ? m_CoroutineManager.StopByOwner(owner) : 0;
        }

        public int StopByTag(string tag)
        {
            return m_CoroutineManager != null ? m_CoroutineManager.StopByTag(tag) : 0;
        }

        public CoroutineSnapshot[] GetAllRunning()
        {
            return m_CoroutineManager != null ? m_CoroutineManager.GetAllRunning() : Array.Empty<CoroutineSnapshot>();
        }
    }
}
