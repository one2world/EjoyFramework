//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using EjoyFramework.Core.HotUpdate;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 基于 InjectFix/iFix 的补丁加载辅助器（<see cref="IPatchLoader"/> 的 Unity 实现）。
    ///
    /// 编译开关 <c>EJOY_IFIX</c>：
    ///   - <b>已定义</b>（按 <c>docs/iFix-Integration.md</c> 装好 InjectFix 后手动加上 Scripting Define）：
    ///     走 <c>IFix.PatchManager.Load(Stream)</c> 真正应用补丁。
    ///   - <b>未定义</b>（默认；iFix 未安装）：本文件仍能编译——iFix 类型引用都在 <c>#if</c> 内；
    ///     应用补丁降级为安全失败（首次告警一次），返回 false（编辑器/无补丁开发期的预期行为）。
    /// </summary>
    public sealed class IFixPatchLoader : IPatchLoader
    {
        // #else 路径下只在首次告警一次，避免每次应用补丁刷屏。
        private static bool s_NotInstalledWarned;

        /// <inheritdoc />
        public bool ApplyPatch(Stream patchStream, out string error)
        {
            if (patchStream == null)
            {
                error = "Patch stream is null.";
                return false;
            }

#if EJOY_IFIX
            try
            {
                // iFix 运行时入口：加载补丁流即把其中方法替换为可解释执行的修复版。
                IFix.PatchManager.Load(patchStream);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = "IFix.PatchManager.Load threw: " + ex.Message;
                return false;
            }
#else
            if (!s_NotInstalledWarned)
            {
                s_NotInstalledWarned = true;
                Log.Warning("InjectFix not installed (define EJOY_IFIX after installing iFix); patch NOT applied. " +
                            "Expected in Editor/dev without iFix. See docs/iFix-Integration.md.");
            }
            error = "InjectFix not installed (define EJOY_IFIX after installing iFix); patch not applied.";
            return false;
#endif
        }
    }
}
