//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 自动释放的资源引用。包装一个 IAssetLoadHandle，并在 Dispose 时
    /// 恰好一次调用 IResourceManager.UnloadAsset(Handle.Asset)（仅在 Asset != null 时），
    /// 用以解决"业务持有资源后忘记 UnloadAsset 导致的材质/纹理泄漏"。
    ///
    /// 典型用法：
    ///   using (var aref = AssetReference.Acquire(res, "Assets/.../Mat.mat"))
    ///   {
    ///       // 轮询 aref.IsDone / aref.Asset
    ///   } // 离开作用域自动卸载
    ///
    /// 线程约束：与 IResourceManager 一致，仅可在 Unity 主线程使用。
    /// </summary>
    public sealed class AssetReference : IDisposable
    {
        private readonly IResourceManager m_Res;
        private readonly IAssetLoadHandle m_Handle;
        private bool m_Disposed;

        private AssetReference(IResourceManager res, IAssetLoadHandle handle)
        {
            m_Res = res;
            m_Handle = handle;
        }

        /// <summary>底层加载句柄。可读取 Status/Progress/Asset/Completed，或主动 Cancel。</summary>
        public IAssetLoadHandle Handle
        {
            get { return m_Handle; }
        }

        /// <summary>加载是否已结束（Done / Failed / Cancelled）。</summary>
        public bool IsDone
        {
            get { return m_Handle.IsDone; }
        }

        /// <summary>已加载的资产；未完成或失败时为 null。</summary>
        public object Asset
        {
            get { return m_Handle.Asset; }
        }

        /// <summary>
        /// 发起一次异步加载并包装为 AssetReference。
        /// </summary>
        /// <param name="res">资源管理器（不可为 null）。</param>
        /// <param name="assetName">资源路径。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="userData">透传给句柄的 userData。</param>
        public static AssetReference Acquire(IResourceManager res, string assetName, int priority = 0, object userData = null)
        {
            if (res == null) throw new ArgumentNullException(nameof(res), "Resource manager is invalid.");
            IAssetLoadHandle handle = res.LoadAssetWithHandle(assetName, priority, userData);
            return new AssetReference(res, handle);
        }

        /// <summary>
        /// 释放资源引用：恰好一次卸载底层 asset（bool 守卫保证幂等；仅在 Asset != null 时执行）。
        /// 若加载尚未结束，先取消句柄；取消后晚到的资产由 ResourceManager 自行卸载。
        /// </summary>
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;

            if (m_Handle == null) return;

            // 加载未结束：取消句柄。Cancel 后到达的资产由 ResourceManager.UnloadAsset 释放（见 IAssetLoadHandle 文档）。
            if (!m_Handle.IsDone)
            {
                m_Handle.Cancel();
                return;
            }

            object asset = m_Handle.Asset;
            if (m_Res != null && asset != null)
            {
                m_Res.UnloadAsset(asset);
            }
        }
    }

    /// <summary>
    /// 把 AssetReference 的生命周期绑定到一个 GameObject：当宿主 GameObject 被销毁时
    /// 自动 Dispose 持有的 AssetReference，从而卸载底层资源。
    ///
    /// 用途：解决"GameObject 被销毁后业务忘记 UnloadAsset，导致材质/纹理常驻泄漏"的问题。
    /// 让资源在 GameObject 死亡时随之释放，无需业务手动追踪卸载时机。
    /// </summary>
    public sealed class AutoReleaseAsset : MonoBehaviour
    {
        private AssetReference m_Reference;

        /// <summary>
        /// 绑定一个 AssetReference。若已绑定旧引用，会先释放旧引用再绑定新引用。
        /// </summary>
        public void Bind(AssetReference reference)
        {
            if (m_Reference != null && !ReferenceEquals(m_Reference, reference))
            {
                m_Reference.Dispose();
            }
            m_Reference = reference;
        }

        private void OnDestroy()
        {
            if (m_Reference != null)
            {
                m_Reference.Dispose();
                m_Reference = null;
            }
        }
    }
}
