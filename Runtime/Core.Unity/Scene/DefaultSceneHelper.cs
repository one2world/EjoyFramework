//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using EjoyFramework.Core.Coroutines;
using EjoyFramework.Core.Resource;
using EjoyFramework.Core.Scene;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认场景 helper（Unity 实现）。
    /// 流程：
    ///   Load: IResourceManager.LoadScene 加载 bundle → 启动 Unity SceneManager.LoadSceneAsync(Additive) → 进度回调
    ///   Unload: SceneManager.UnloadSceneAsync → IResourceManager.UnloadScene
    /// 协程通过 ICoroutineManager 统一托管（带 tag，可在 DebuggerComponent 看到）。
    /// </summary>
    public sealed class DefaultSceneHelper : ISceneHelper
    {
        private readonly ICoroutineManager m_Coroutines;
        private readonly IResourceManager m_ResourceManager;
        private readonly Dictionary<string, LoadingEntry> m_BundleLoading = new Dictionary<string, LoadingEntry>(StringComparer.Ordinal);

        public DefaultSceneHelper(ICoroutineManager coroutineManager, IResourceManager resourceManager)
        {
            m_Coroutines = coroutineManager ?? throw new ArgumentNullException(nameof(coroutineManager));
            m_ResourceManager = resourceManager;
        }

        public void LoadAsync(string sceneAssetName, int priority, object userData,
            Action<float> onProgress,
            Action<string> onSuccess,
            Action<string, string> onFailure)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
            {
                onFailure?.Invoke(sceneAssetName, "Scene asset name is invalid.");
                return;
            }

            if (m_ResourceManager == null || !m_ResourceManager.IsInitialized)
            {
                // 无资源管理器：直接挂载，未持有任何 bundle 引用，失败时无需补偿卸载。
                m_Coroutines.Run(MountSceneCoroutine(sceneAssetName, onProgress, onSuccess, onFailure, releaseBundleOnFailure: false),
                    tag: "Scene/Load/" + sceneAssetName, owner: this);
                return;
            }

            if (m_BundleLoading.ContainsKey(sceneAssetName))
            {
                onFailure?.Invoke(sceneAssetName, "Scene is already loading.");
                return;
            }

            var entry = new LoadingEntry { OnProgress = onProgress, OnSuccess = onSuccess, OnFailure = onFailure };
            m_BundleLoading[sceneAssetName] = entry;

            try
            {
                // 单一加载所有权：ResourceManager.LoadScene 仅确保 bundle（含依赖）就绪并引用计数，
                // 不再自行调用 UnitySceneManager.LoadSceneAsync。真正的场景挂载只在这里执行一次，
                // 修复历史上场景被加载两次（重复 GameObject/灯光/EventSystem）的缺陷。
                m_ResourceManager.LoadScene(
                    sceneAssetName,
                    priority,
                    // bundle 阶段进度占前半段；场景挂载阶段占后半段，避免假的 50/50 跳变。
                    bundleProgress => onProgress?.Invoke(bundleProgress * 0.5f),
                    asset =>
                    {
                        m_BundleLoading.Remove(asset);
                        m_Coroutines.Run(
                            MountSceneCoroutine(asset,
                                p => onProgress?.Invoke(0.5f + p * 0.5f),
                                onSuccess,
                                onFailure,
                                releaseBundleOnFailure: true),   // bundle 已被 LoadScene 引用计数，挂载失败须补偿卸载
                            tag: "Scene/Mount/" + asset, owner: this);
                    },
                    (asset, err) =>
                    {
                        m_BundleLoading.Remove(asset);
                        onFailure?.Invoke(asset, err);
                    },
                    userData);
            }
            catch (Exception ex)
            {
                m_BundleLoading.Remove(sceneAssetName);
                onFailure?.Invoke(sceneAssetName, "ResourceManager.LoadScene threw: " + ex.Message);
            }
        }

        public void UnloadAsync(string sceneAssetName, object userData,
            Action<string> onSuccess,
            Action<string, string> onFailure)
        {
            if (string.IsNullOrEmpty(sceneAssetName))
            {
                onFailure?.Invoke(sceneAssetName, "Scene asset name is invalid.");
                return;
            }
            m_Coroutines.Run(UnmountSceneCoroutine(sceneAssetName, userData, onSuccess, onFailure),
                tag: "Scene/Unload/" + sceneAssetName, owner: this);
        }

        private IEnumerator MountSceneCoroutine(string sceneAssetName,
            Action<float> onProgress,
            Action<string> onSuccess,
            Action<string, string> onFailure,
            bool releaseBundleOnFailure)
        {
            AsyncOperation op;
            try
            {
                op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneAssetName,
                    UnityEngine.SceneManagement.LoadSceneMode.Additive);
            }
            catch (Exception ex)
            {
                ReleaseBundleRefOnMountFailure(sceneAssetName, releaseBundleOnFailure);
                onFailure?.Invoke(sceneAssetName, "SceneManager.LoadSceneAsync threw: " + ex.Message);
                yield break;
            }
            if (op == null)
            {
                ReleaseBundleRefOnMountFailure(sceneAssetName, releaseBundleOnFailure);
                onFailure?.Invoke(sceneAssetName, "SceneManager.LoadSceneAsync returned null (scene not in build / not in bundle).");
                yield break;
            }
            while (!op.isDone)
            {
                onProgress?.Invoke(op.progress);
                yield return null;
            }
            onProgress?.Invoke(1f);
            onSuccess?.Invoke(sceneAssetName);
        }

        private IEnumerator UnmountSceneCoroutine(string sceneAssetName, object userData,
            Action<string> onSuccess,
            Action<string, string> onFailure)
        {
            AsyncOperation op;
            try
            {
                op = UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(sceneAssetName);
            }
            catch (Exception ex)
            {
                onFailure?.Invoke(sceneAssetName, "SceneManager.UnloadSceneAsync threw: " + ex.Message);
                yield break;
            }
            if (op == null)
            {
                onFailure?.Invoke(sceneAssetName, "SceneManager.UnloadSceneAsync returned null.");
                yield break;
            }
            while (!op.isDone) yield return null;

            if (m_ResourceManager != null && m_ResourceManager.IsInitialized)
            {
                try
                {
                    m_ResourceManager.UnloadScene(sceneAssetName,
                        _ => onSuccess?.Invoke(sceneAssetName),
                        (asset, err) => onFailure?.Invoke(asset, err),
                        userData);
                }
                catch (Exception ex)
                {
                    onFailure?.Invoke(sceneAssetName, "ResourceManager.UnloadScene threw: " + ex.Message);
                }
            }
            else
            {
                onSuccess?.Invoke(sceneAssetName);
            }
        }

        // 场景挂载失败时，释放此前 LoadScene 成功取得的 bundle 引用（否则每次失败永久泄漏一份 bundle 引用计数）。
        private void ReleaseBundleRefOnMountFailure(string sceneAssetName, bool releaseBundleOnFailure)
        {
            if (!releaseBundleOnFailure) return;
            if (m_ResourceManager == null || !m_ResourceManager.IsInitialized) return;
            try
            {
                m_ResourceManager.UnloadScene(sceneAssetName, _ => { }, (_, __) => { }, null);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Scene mount-failure bundle release threw for '{0}': {1}", sceneAssetName, ex);
            }
        }

        private sealed class LoadingEntry
        {
            public Action<float> OnProgress;
            public Action<string> OnSuccess;
            public Action<string, string> OnFailure;
        }
    }
}
