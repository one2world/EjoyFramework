//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.GamePlay.Unity
{
    /// <summary>
    /// 触发 GamePlay.* 纯 C# 模块程序集的工厂注册（与 Core 侧
    /// <c>EjoyFramework.Core.Unity.FrameworkModuleBootstrap</c> 同理）。
    ///
    /// <para>GamePlay.Core / Combat / Netcode / LiveOps 均为 <c>noEngineReferences</c> 的纯 C# 程序集，
    /// 无法自挂 <see cref="RuntimeInitializeOnLoadMethodAttribute"/>；由本 Unity 层在
    /// <see cref="RuntimeInitializeLoadType.BeforeSceneLoad"/> 显式调用各自生成的
    /// <c>FrameworkModuleRegistrations.RegisterAll</c>，使其模块走静态 <c>new</c> 工厂、不被 IL2CPP 裁剪。
    /// <c>RegisterAll</c> 幂等，重复触发安全。</para>
    ///
    /// <para><b>覆盖边界</b>：本 bootstrap 仅在 <c>EjoyFramework.GamePlay.Unity</c> 被游戏引用、进入构建时才会运行。
    /// 若某游戏直接引用某个 GamePlay 模块程序集而<b>不</b>引用本 Unity 层，需在自己的 Unity 侧 bootstrap
    /// 中同样调用该程序集的 <c>RegisterAll</c>（否则 <c>Framework.GetModule</c> 会因工厂未注册而直接抛异常）。
    /// 构建前置守卫 <c>ModuleRegistryBuildCheck</c> 只保证注册表内容最新，不保证触发已接好。</para>
    /// </summary>
    internal static class GamePlayModuleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterGamePlayModules()
        {
            EjoyFramework.GamePlay.Core.Generated.FrameworkModuleRegistrations.RegisterAll();
            EjoyFramework.GamePlay.Combat.Generated.FrameworkModuleRegistrations.RegisterAll();
            EjoyFramework.GamePlay.Netcode.Generated.FrameworkModuleRegistrations.RegisterAll();
            EjoyFramework.GamePlay.LiveOps.Generated.FrameworkModuleRegistrations.RegisterAll();
        }
    }
}
