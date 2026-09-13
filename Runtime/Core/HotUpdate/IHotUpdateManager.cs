//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace EjoyFramework.Core.HotUpdate
{
    /// <summary>
    /// 热更（方法级补丁）管理器接口。后端为 InjectFix/iFix：加载补丁后，把线上版本中的问题方法替换为可解释执行的
    /// 修复版（方法级 hotfix，而非整程序集热更）。
    ///
    /// 设计要点：
    ///   - <b>与下发解耦</b>：本模块不下载。调用方（通常是启动 Procedure）用 Patch/Download 模块把补丁拉到本地，
    ///     再把<b>字节/流</b>交给本模块应用。
    ///   - <b>引擎无关</b>：真正应用补丁委托给注入的 <see cref="IPatchLoader"/>（Unity 层封装 <c>IFix.PatchManager.Load</c>）；
    ///     Core 层只做编排、去重、事件，可独立单测。
    ///   - <b>幂等</b>：同一 <c>patchId</c> 重复应用直接返回 true，不重复加载。
    ///   - <b>不抛异常</b>：无 loader / 应用失败 一律走 <see cref="OnError"/> 并返回安全值，避免崩在启动链上。
    ///
    /// 完整接入见 <c>docs/iFix-Integration.md</c>。
    /// </summary>
    public interface IHotUpdateManager
    {
        /// <summary>注入补丁加载后端（通常是 Unity 层的 IFixPatchLoader）。</summary>
        void SetLoader(IPatchLoader loader);

        /// <summary>
        /// 应用一个补丁（字节）。<paramref name="patchId"/> 用于去重与记录（同一 id 已应用则直接返回 true）。
        /// 成功触发 <see cref="OnPatchApplied"/>；失败（无 loader / 后端报错 / 字节为空）触发 <see cref="OnError"/> 返回 false。
        /// </summary>
        bool ApplyPatch(string patchId, byte[] patchBytes);

        /// <summary>应用一个补丁（流）。语义同字节重载；调用方负责流的生命周期。</summary>
        bool ApplyPatch(string patchId, Stream patchStream);

        /// <summary>该补丁 id 是否已应用。</summary>
        bool IsPatchApplied(string patchId);

        /// <summary>已应用补丁数量。</summary>
        int AppliedPatchCount { get; }

        /// <summary>已应用补丁 id（按应用顺序）。</summary>
        IEnumerable<string> AppliedPatches { get; }

        /// <summary>补丁成功应用时触发。参数：管理器、补丁 id。</summary>
        event Action<IHotUpdateManager, string> OnPatchApplied;

        /// <summary>补丁应用失败时触发。参数：管理器、补丁 id、错误信息。</summary>
        event Action<IHotUpdateManager, string, string> OnError;
    }
}
