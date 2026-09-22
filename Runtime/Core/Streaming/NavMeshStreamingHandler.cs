//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Navigation;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core.Streaming
{
    /// <summary>
    /// 流式 navmesh 装饰器：单元加载完成后，按单元加载对应的导航网格数据资产并挂载到 <see cref="INavigationManager"/>；
    /// 单元卸载时先卸下 tile、归还资产，再转给内层 handler。
    ///
    /// 组合：<c>manager ← navmesh ← (persistence ←) sceneHandler</c>。resolver 返回 null 表示该单元无 navmesh。
    /// 资产加载失败只记录错误，不影响单元本身的加载结果（导航缺失比世界缺失好处理）。
    /// </summary>
    public sealed class NavMeshStreamingHandler : IWorldStreamingHandler, IWorldStreamingNotifier
    {
        /// <summary>内容键 → navmesh 数据资产名；null/空 = 无。</summary>
        public interface INavMeshAssetResolver
        {
            string Resolve(int layer, int cx, int cz, int contentKey);
        }

        private sealed class TileRecord
        {
            public IAssetLoadHandle Handle;
            public object Asset;
            public int TileId;
        }

        private readonly IWorldStreamingManager m_Manager;
        private readonly IWorldStreamingNotifier m_Downstream;
        private readonly INavigationManager m_Navigation;
        private readonly IResourceManager m_Resources;
        private readonly INavMeshAssetResolver m_Resolver;
        private readonly Dictionary<int, TileRecord> m_Tiles = new Dictionary<int, TileRecord>();
        private readonly Stack<TileRecord> m_RecordPool = new Stack<TileRecord>();
        private readonly Action<IAssetLoadHandle> m_OnAssetLoaded;
        private IWorldStreamingHandler m_Inner;
        private float m_CellSize;

        public NavMeshStreamingHandler(IWorldStreamingManager manager, IWorldStreamingNotifier downstream, INavigationManager navigation, IResourceManager resources, INavMeshAssetResolver resolver)
        {
            if (manager == null) throw new FrameworkException("NavMeshStreamingHandler：manager 不能为 null。");
            if (downstream == null) throw new FrameworkException("NavMeshStreamingHandler：downstream 不能为 null。");
            if (navigation == null) throw new FrameworkException("NavMeshStreamingHandler：navigation 不能为 null。");
            if (resources == null) throw new FrameworkException("NavMeshStreamingHandler：resources 不能为 null。");
            if (resolver == null) throw new FrameworkException("NavMeshStreamingHandler：resolver 不能为 null。");
            m_Manager = manager;
            m_Downstream = downstream;
            m_Navigation = navigation;
            m_Resources = resources;
            m_Resolver = resolver;
            m_OnAssetLoaded = OnAssetLoaded;
        }

        public IWorldStreamingHandler Inner
        {
            get { return m_Inner; }
            set { m_Inner = value; }
        }

        /// <summary>已挂载或加载中的 tile 数。</summary>
        public int TrackedTileCount { get { return m_Tiles.Count; } }

        // ---- IWorldStreamingHandler ----

        public void BeginLoad(int cellId, int layer, int cx, int cz, int contentKey, int lod)
        {
            if (m_Inner != null) m_Inner.BeginLoad(cellId, layer, cx, cz, contentKey, lod);
            else m_Downstream.NotifyLoaded(cellId, true);
        }

        public void CancelLoad(int cellId)
        {
            if (m_Inner != null) m_Inner.CancelLoad(cellId);
        }

        public void BeginUnload(int cellId, int layer, int cx, int cz, int contentKey)
        {
            RemoveTile(cellId);
            if (m_Inner != null) m_Inner.BeginUnload(cellId, layer, cx, cz, contentKey);
            else m_Downstream.NotifyUnloaded(cellId);
        }

        public void OnLodChanged(int cellId, int fromLod, int toLod)
        {
            if (m_Inner != null) m_Inner.OnLodChanged(cellId, fromLod, toLod);
        }

        // ---- IWorldStreamingNotifier ----

        public void NotifyLoaded(int cellId, bool success)
        {
            if (success) BeginTileLoad(cellId);
            m_Downstream.NotifyLoaded(cellId, success);
        }

        public void NotifyUnloaded(int cellId)
        {
            m_Downstream.NotifyUnloaded(cellId);
        }

        // ---- 内部 ----

        private void BeginTileLoad(int cellId)
        {
            StreamingCellInfo info;
            if (!m_Manager.TryGetCell(cellId, out info) || info.State != StreamingCellState.Loading) return;
            if (m_Tiles.ContainsKey(cellId)) return;
            string assetName = m_Resolver.Resolve(info.Layer, info.Cx, info.Cz, info.ContentKey);
            if (string.IsNullOrEmpty(assetName)) return;

            TileRecord record = m_RecordPool.Count > 0 ? m_RecordPool.Pop() : new TileRecord();
            record.Handle = m_Resources.LoadAssetWithHandle(assetName, 0, cellId);
            record.Asset = null;
            record.TileId = 0;
            m_Tiles.Add(cellId, record);
            m_CellSize = m_Manager.CellSize;
            record.Handle.Completed += m_OnAssetLoaded;
        }

        private void OnAssetLoaded(IAssetLoadHandle handle)
        {
            int cellId = (int)handle.UserData;
            TileRecord record;
            if (!m_Tiles.TryGetValue(cellId, out record) || record.Handle != handle)
            {
                // 单元已卸载：资产迟到，直接归还
                if (handle.Status == LoadAssetStatus.Done && handle.Asset != null) m_Resources.UnloadAsset(handle.Asset);
                return;
            }

            record.Handle = null;
            if (handle.Status != LoadAssetStatus.Done || handle.Asset == null)
            {
                if (handle.Status == LoadAssetStatus.Failed)
                {
                    FrameworkLog.Error("NavMeshStreamingHandler：单元 {0} 的 navmesh 资产加载失败：{1}", cellId, handle.ErrorMessage);
                }

                m_Tiles.Remove(cellId);
                Release(record);
                return;
            }

            StreamingCellInfo info;
            if (!m_Manager.TryGetCell(cellId, out info))
            {
                m_Resources.UnloadAsset(handle.Asset);
                m_Tiles.Remove(cellId);
                Release(record);
                return;
            }

            record.Asset = handle.Asset;
            Vector3Lite origin = new Vector3Lite(info.Cx * m_CellSize, 0f, info.Cz * m_CellSize);
            try
            {
                record.TileId = m_Navigation.AddNavMeshTile(handle.Asset, origin);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("NavMeshStreamingHandler：AddNavMeshTile 抛出异常（单元 {0}）：{1}", cellId, ex);
                record.TileId = 0;
            }

            if (record.TileId <= 0)
            {
                m_Resources.UnloadAsset(handle.Asset);
                m_Tiles.Remove(cellId);
                Release(record);
            }
        }

        private void RemoveTile(int cellId)
        {
            TileRecord record;
            if (!m_Tiles.TryGetValue(cellId, out record)) return;
            m_Tiles.Remove(cellId);
            if (record.Handle != null)
            {
                record.Handle.Cancel();   // 资产还在路上：取消，迟到结果由 OnAssetLoaded 归还
            }

            if (record.TileId > 0) m_Navigation.RemoveNavMeshTile(record.TileId);
            if (record.Asset != null) m_Resources.UnloadAsset(record.Asset);
            Release(record);
        }

        private void Release(TileRecord record)
        {
            record.Handle = null;
            record.Asset = null;
            record.TileId = 0;
            m_RecordPool.Push(record);
        }
    }
}
