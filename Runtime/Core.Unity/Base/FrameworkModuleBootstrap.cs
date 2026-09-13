//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 在场景加载前触发 <c>EjoyFramework.Core</c> 模块工厂注册。
    ///
    /// <para><c>EjoyFramework.Core</c> 是 <c>noEngineReferences</c> 的纯 C# 程序集，无法自挂
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/>；故由本 Unity 层在 <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/>
    /// 阶段（早于任何场景 MonoBehaviour 的 Awake，含各 <c>XxxComponent</c>）显式调用其生成的工厂注册表
    /// <c>EjoyFramework.Core.Generated.FrameworkModuleRegistrations.RegisterAll</c>。</para>
    ///
    /// <para>注册后 <see cref="EjoyFramework.Core.Framework.GetModule{T}"/> 走静态 <c>new</c> 工厂、不再反射查找类型，
    /// 从根本上避免 IL2CPP 托管代码裁剪导致的 "Can not find framework module type" 崩溃（<c>link.xml</c> 在本工程未被
    /// 链接器采纳，故改用此生成 + 静态注册方案）。<c>RegisterAll</c> 幂等，重复触发安全。</para>
    ///
    /// <para>注：GamePlay.* 模块程序集本游戏未引用、不进包，其生成的注册表暂无需触发；当将来用到时，
    /// 由引用它们的 Unity 侧程序集（如 GamePlay.Unity）按同样方式调用对应 RegisterAll 即可。</para>
    /// </summary>
    internal static class FrameworkModuleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterFrameworkModules()
        {
            EjoyFramework.Core.Generated.FrameworkModuleRegistrations.RegisterAll();
        }
    }
}
