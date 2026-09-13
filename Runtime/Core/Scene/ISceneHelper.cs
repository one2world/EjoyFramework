//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Scene
{
    /// <summary>
    /// 场景加载/卸载 helper 接口。
    /// 由 Unity 层注入具体实现（基于 UnityEngine.SceneManagement.SceneManager 协程）。
    /// </summary>
    public interface ISceneHelper
    {
        void LoadAsync(string sceneAssetName, int priority, object userData,
            Action<float> onProgress,
            Action<string> onSuccess,
            Action<string, string> onFailure);

        void UnloadAsync(string sceneAssetName, object userData,
            Action<string> onSuccess,
            Action<string, string> onFailure);
    }
}
