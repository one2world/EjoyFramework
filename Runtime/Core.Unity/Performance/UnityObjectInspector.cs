//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Performance;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// <see cref="IUnityObjectInspector"/> 的 Unity 实现：用 UnityEngine.Object 重载的 <c>==</c> 语义区分原生存活 / 已毁。
    /// 启动时（<see cref="RuntimeInitializeLoadType.AfterAssembliesLoaded"/>）自动注入到 <see cref="ObjectTracker"/>，
    /// 无需手动接线、也不强制创建任何模块。
    /// </summary>
    public sealed class UnityObjectInspector : IUnityObjectInspector
    {
        public bool IsUnityObjectType(Type type)
        {
            return type != null && typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        public bool IsNativeAlive(object obj)
        {
            // Unity 重载 ==：原生对象被 Destroy 后 (uo == null) 为 true，故已毁对象在此返回 false。
            return obj is UnityEngine.Object uo && uo != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void AutoInstall()
        {
            ObjectTracker.SetUnityObjectInspector(new UnityObjectInspector());
        }
    }
}
