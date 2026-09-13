//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.HotUpdate;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 热更组件（后端 InjectFix/iFix，方法级补丁）。
    /// Awake 解析 <see cref="IHotUpdateManager"/> 并注入一个 <see cref="IFixPatchLoader"/> 作为底层补丁加载器。
    ///
    /// 注意：本组件<b>只做装配</b>，不驱动热更<b>下发流程</b>。完整时序——
    ///   1) 通过 Patch/Download 模块把补丁（<c>*.patch.bytes</c>）下载到本地；
    ///   2) 读出字节/流，调 <see cref="IHotUpdateManager.ApplyPatch(string, byte[])"/> 应用；
    ///   3) 之后被 <c>[IFix.Patch]</c> 标记的问题方法即走修复版。
    /// ——应写在启动 Procedure（尽量早、在进入业务逻辑前）。详见 <c>docs/iFix-Integration.md</c>。
    ///
    /// 设计：不提供 GameEntry 静态访问器；业务通过 GetComponent / 直接用 Framework.GetModule 获取。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/HotUpdate")]
    public sealed class HotUpdateComponent : GameFrameworkComponent
    {
        private IHotUpdateManager m_HotUpdateManager;

        /// <summary>热更管理器（业务可直接访问以驱动补丁应用流程）。</summary>
        public IHotUpdateManager HotUpdateManager
        {
            get { return m_HotUpdateManager; }
        }

        protected override void Awake()
        {
            base.Awake();
            m_HotUpdateManager = Framework.GetModule<IHotUpdateManager>();
            if (m_HotUpdateManager == null)
            {
                Log.Fatal("HotUpdate manager is invalid.");
                return;
            }

            // 注入默认的 iFix 补丁加载器（未装 iFix / 未定义 EJOY_IFIX 时自动走安全回退，见 IFixPatchLoader）。
            m_HotUpdateManager.SetLoader(new IFixPatchLoader());
        }
    }
}
