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
    /// 热更（方法级补丁）管理器（FrameworkModule 实现，后端 InjectFix/iFix）。
    ///
    /// 职责：
    ///   1) 持有注入的 <see cref="IPatchLoader"/>，把"应用补丁"委托给它（Unity 层封装 <c>IFix.PatchManager.Load</c>）；
    ///   2) 按 <c>patchId</c> 去重，保证<b>幂等</b>（同 id 重复应用直接成功返回）；
    ///   3) 把成功/失败包装为事件，<b>从不抛异常</b>（启动链友好）。
    ///
    /// 线程：mutation API 走 <see cref="Framework.EnsureMainThread"/>（仅 Editor/DEV 生效）。
    /// </summary>
    public sealed class HotUpdateManager : FrameworkModule, IHotUpdateManager
    {
        private IPatchLoader m_Loader;

        // 已应用补丁 id（去重）+ 顺序列表（稳定枚举）。
        private readonly HashSet<string> m_AppliedSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> m_AppliedOrder = new List<string>();

        /// <summary>公共无参构造，供生成的工厂创建。</summary>
        public HotUpdateManager()
        {
        }

        // Priority 0：业务模块。补丁应用由启动 Procedure 显式驱动，不依赖 Update。
        public override int Priority { get { return 0; } }

        // 需要注入 loader 才能工作。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Loader != null; } }
        public override string ConfigurationHint { get { return "Call SetLoader(...) before use (e.g. new IFixPatchLoader())."; } }

        public override void Update(float elapseSeconds, float realElapseSeconds) { }

        /// <summary>关闭模块：清空账本与 loader 引用（已应用的补丁无法撤销，仅清本地记录）。</summary>
        public override void Shutdown()
        {
            m_AppliedSet.Clear();
            m_AppliedOrder.Clear();
            m_Loader = null;
        }

        /// <inheritdoc />
        public void SetLoader(IPatchLoader loader)
        {
            Framework.EnsureMainThread(nameof(SetLoader));
            m_Loader = loader;
        }

        /// <inheritdoc />
        public bool ApplyPatch(string patchId, byte[] patchBytes)
        {
            if (patchBytes == null || patchBytes.Length == 0)
            {
                RaiseError(patchId, "Patch bytes are null or empty.");
                return false;
            }
            using (var stream = new MemoryStream(patchBytes, writable: false))
            {
                return ApplyPatch(patchId, stream);
            }
        }

        /// <inheritdoc />
        public bool ApplyPatch(string patchId, Stream patchStream)
        {
            Framework.EnsureMainThread(nameof(ApplyPatch));

            if (string.IsNullOrEmpty(patchId))
            {
                RaiseError(patchId, "Patch id is null or empty.");
                return false;
            }
            // 幂等：同 id 已应用，直接成功返回，不重复加载（iFix 重复加载同一补丁无意义且可能异常）。
            if (m_AppliedSet.Contains(patchId))
            {
                return true;
            }
            if (m_Loader == null)
            {
                RaiseError(patchId, "No IPatchLoader set; call SetLoader(...) first.");
                return false;
            }
            if (patchStream == null)
            {
                RaiseError(patchId, "Patch stream is null.");
                return false;
            }

            bool ok = m_Loader.ApplyPatch(patchStream, out string error);
            if (!ok)
            {
                RaiseError(patchId, string.IsNullOrEmpty(error) ? "ApplyPatch failed." : error);
                return false;
            }

            m_AppliedSet.Add(patchId);
            m_AppliedOrder.Add(patchId);
            OnPatchApplied?.Invoke(this, patchId);
            return true;
        }

        /// <inheritdoc />
        public bool IsPatchApplied(string patchId)
        {
            return !string.IsNullOrEmpty(patchId) && m_AppliedSet.Contains(patchId);
        }

        /// <inheritdoc />
        public int AppliedPatchCount
        {
            get { return m_AppliedOrder.Count; }
        }

        /// <inheritdoc />
        public IEnumerable<string> AppliedPatches
        {
            // 快照，遍历期间即使有新应用也安全。
            get { return new List<string>(m_AppliedOrder); }
        }

        /// <inheritdoc />
        public event Action<IHotUpdateManager, string> OnPatchApplied;

        /// <inheritdoc />
        public event Action<IHotUpdateManager, string, string> OnError;

        // 统一失败出口：记日志 + 触发 OnError，永不抛。
        private void RaiseError(string patchId, string message)
        {
            FrameworkLog.Error("HotUpdateManager patch '{0}' error: {1}", patchId ?? "<null>", message);
            OnError?.Invoke(this, patchId, message);
        }
    }
}
