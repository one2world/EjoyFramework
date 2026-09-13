//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.IO;

namespace EjoyFramework.Core.HotUpdate
{
    /// <summary>
    /// 补丁加载辅助器（由 Unity 层实现，封装具体热更后端——当前为 InjectFix/iFix）。
    /// iFix 的运行时入口是 <c>IFix.PatchManager.Load(Stream)</c>：加载一个补丁流即把其中的方法替换为可解释执行的修复版。
    ///
    /// 约定：实现方把后端异常/失败映射为 <c>false</c> + <paramref name="error"/>，不抛出。
    /// </summary>
    public interface IPatchLoader
    {
        /// <summary>应用一个补丁流。成功返回 true；失败返回 false 并填充 <paramref name="error"/>。</summary>
        bool ApplyPatch(Stream patchStream, out string error);
    }
}
