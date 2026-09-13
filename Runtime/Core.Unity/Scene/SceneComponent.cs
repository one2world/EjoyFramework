//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Coroutines;
using EjoyFramework.Core.Resource;
using EjoyFramework.Core.Scene;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 场景组件。
    /// 完全转发给 SceneManager（FrameworkModule）；在 Awake 注入 ResourceManager + DefaultSceneHelper。
    /// 暴露 Unity 风格 Action 事件方便业务订阅。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Scene")]
    public sealed class SceneComponent : GameFrameworkComponent
    {
        public event Action<string, float, object> LoadSceneSuccess;
        public event Action<string, string, object> LoadSceneFailure;
        public event Action<string, float, object> LoadSceneUpdate;
        public event Action<string, object> UnloadSceneSuccess;
        public event Action<string, object> UnloadSceneFailure;

        private ISceneManager m_SceneManager;
        private DefaultSceneHelper m_Helper;

        protected override void Awake()
        {
            base.Awake();
            m_SceneManager = Framework.GetModule<ISceneManager>();
            if (m_SceneManager == null)
            {
                Log.Fatal("Scene manager is invalid.");
                return;
            }

            m_SceneManager.LoadSceneSuccess += OnLoadSceneSuccess;
            m_SceneManager.LoadSceneFailure += OnLoadSceneFailure;
            m_SceneManager.LoadSceneUpdate += OnLoadSceneUpdate;
            m_SceneManager.UnloadSceneSuccess += OnUnloadSceneSuccess;
            m_SceneManager.UnloadSceneFailure += OnUnloadSceneFailure;
            ConfigureManager();
        }

        private void ConfigureManager()
        {
            if (m_SceneManager == null) return;

            IResourceManager rm = Framework.GetModule<IResourceManager>();
            if (rm == null)
            {
                Log.Fatal("Resource manager is invalid.");
                return;
            }
            // SceneManager 不直接持有 ResourceManager（场景 IO 走 helper）

            ICoroutineManager cm = Framework.GetModule<ICoroutineManager>();
            if (cm == null)
            {
                Log.Fatal("Coroutine manager is invalid (add CoroutineComponent to scene).");
                return;
            }

            m_Helper = new DefaultSceneHelper(cm, rm);
            m_SceneManager.SetHelper(m_Helper);
        }

        protected override void OnDestroy()
        {
            if (m_SceneManager != null)
            {
                m_SceneManager.LoadSceneSuccess -= OnLoadSceneSuccess;
                m_SceneManager.LoadSceneFailure -= OnLoadSceneFailure;
                m_SceneManager.LoadSceneUpdate -= OnLoadSceneUpdate;
                m_SceneManager.UnloadSceneSuccess -= OnUnloadSceneSuccess;
                m_SceneManager.UnloadSceneFailure -= OnUnloadSceneFailure;
            }
            base.OnDestroy();
        }

        public bool SceneIsLoaded(string sceneAssetName)
        {
            return m_SceneManager != null && m_SceneManager.SceneIsLoaded(sceneAssetName);
        }

        public bool SceneIsLoading(string sceneAssetName)
        {
            return m_SceneManager != null && m_SceneManager.SceneIsLoading(sceneAssetName);
        }

        public bool SceneIsUnloading(string sceneAssetName)
        {
            return m_SceneManager != null && m_SceneManager.SceneIsUnloading(sceneAssetName);
        }

        public string[] GetLoadedSceneAssetNames()
        {
            return m_SceneManager != null ? m_SceneManager.GetLoadedSceneAssetNames() : Array.Empty<string>();
        }

        public void LoadScene(string sceneAssetName, object userData = null)
        {
            LoadScene(sceneAssetName, 0, userData);
        }

        public void LoadScene(string sceneAssetName, int priority, object userData = null)
        {
            if (m_SceneManager == null) { Log.Fatal("Scene manager is invalid."); return; }
            if (string.IsNullOrEmpty(sceneAssetName)) { Log.Error("Scene asset name is invalid."); return; }
            m_SceneManager.LoadScene(sceneAssetName, priority, userData);
        }

        public void UnloadScene(string sceneAssetName, object userData = null)
        {
            if (m_SceneManager == null) { Log.Fatal("Scene manager is invalid."); return; }
            if (string.IsNullOrEmpty(sceneAssetName)) { Log.Error("Scene asset name is invalid."); return; }
            m_SceneManager.UnloadScene(sceneAssetName, userData);
        }

        private void OnLoadSceneSuccess(object sender, LoadSceneSuccessEventArgs e)
        {
            try { LoadSceneSuccess?.Invoke(e.SceneAssetName, e.Duration, e.UserData); }
            catch (Exception ex) { Log.Error("SceneComponent.LoadSceneSuccess listener threw: {0}", ex); }
        }

        private void OnLoadSceneFailure(object sender, LoadSceneFailureEventArgs e)
        {
            try { LoadSceneFailure?.Invoke(e.SceneAssetName, e.ErrorMessage, e.UserData); }
            catch (Exception ex) { Log.Error("SceneComponent.LoadSceneFailure listener threw: {0}", ex); }
        }

        private void OnLoadSceneUpdate(object sender, LoadSceneUpdateEventArgs e)
        {
            try { LoadSceneUpdate?.Invoke(e.SceneAssetName, e.Progress, e.UserData); }
            catch (Exception ex) { Log.Error("SceneComponent.LoadSceneUpdate listener threw: {0}", ex); }
        }

        private void OnUnloadSceneSuccess(object sender, UnloadSceneSuccessEventArgs e)
        {
            try { UnloadSceneSuccess?.Invoke(e.SceneAssetName, e.UserData); }
            catch (Exception ex) { Log.Error("SceneComponent.UnloadSceneSuccess listener threw: {0}", ex); }
        }

        private void OnUnloadSceneFailure(object sender, UnloadSceneFailureEventArgs e)
        {
            try { UnloadSceneFailure?.Invoke(e.SceneAssetName, e.UserData); }
            catch (Exception ex) { Log.Error("SceneComponent.UnloadSceneFailure listener threw: {0}", ex); }
        }
    }
}
