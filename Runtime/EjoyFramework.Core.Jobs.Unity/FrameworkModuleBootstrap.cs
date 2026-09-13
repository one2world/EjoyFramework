//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.Core.Jobs.Unity
{
    /// <summary>
    /// Registers Unity Job System framework modules before scene loading.
    /// </summary>
    internal static class FrameworkModuleBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterAll()
        {
            Generated.FrameworkModuleRegistrations.RegisterAll();
        }
    }
}
