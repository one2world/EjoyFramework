//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 资源加载模式。
    /// EditorSimulation: 仅编辑器，直接通过 AssetDatabase 加载，无需打 AB。
    /// AssetBundle: 运行时通过 AssetBundle 加载，依赖 manifest 与构建产物。
    /// Resources: 通过 Unity Resources.LoadAsync 加载（仅供应急/小项目）。
    /// </summary>
    public enum ResourceMode : byte
    {
        Unspecified = 0,
        EditorSimulation = 1,
        AssetBundle = 2,
        Resources = 3,
    }
}
