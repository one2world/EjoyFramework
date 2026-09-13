//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

#if UNITY_EDITOR
using UnityEditor;

namespace EjoyFramework.Core.Unity.Editor
{
    /// <summary>
    /// 在 "Domain Reload Disabled" Enter PlayMode 时清掉 Framework 的静态状态。
    /// 本类必须放在 Editor 平台的程序集（EjoyFramework.Core.Unity.Editor）内，因为 EjoyFramework.Core 核心是 noEngineReferences。
    /// </summary>
    internal static class FrameworkEditorPlayModeReset
    {
        [InitializeOnEnterPlayMode]
        private static void OnEnterPlayMode()
        {
            Framework.ResetForEnterPlayMode();
        }
    }
}
#endif
