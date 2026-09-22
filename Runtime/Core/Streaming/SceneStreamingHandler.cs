//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Scene;

namespace EjoyFramework.Core.Streaming
{
    /// <summary>
    /// 内置流送处理器：每个单元对应一个附加场景（开放世界最常见的分块方式）。
    /// 内容键 → 场景资源名由 <see cref="ISceneNameResolver"/> 提供（通常查 ConfigBlob 表或按 "World/L{layer}_{cx}_{cz}" 规则拼）。
    ///
    /// 语义：
    ///   • BeginLoad → ISceneManager.LoadScene(sceneName, priority=LOD 越小越高, userData=cellId)；
    ///     场景管理器的成功/失败事件回报给流送管理器。
    ///   • CancelLoad：场景加载不可中断，这里只记录；结果到达后流送管理器会按"迟到结果"发起卸载。
    ///   • BeginUnload → UnloadScene；成功事件回报 NotifyUnloaded。
    ///   • OnLodChanged：场景级 LOD 由业务自行处理（本处理器不做）。
    ///
    /// 同一场景名只允许对应一个单元；重复由 resolver 保证。线程契约：主线程。
    /// </summary>
    public sealed class SceneStreamingHandler : IWorldStreamingHandler, IDisposable
    {
        /// <summary>内容键 → 场景资源名。返回 null/空 表示该单元无场景（视为立即加载成功）。</summary>
        public interface ISceneNameResolver
        {
            string Resolve(int layer, int cx, int cz, int contentKey);
        }

        private readonly IWorldStreamingManager m_Streaming;
        private readonly ISceneManager m_Scenes;
        private readonly ISceneNameResolver m_Resolver;
        private readonly Dictionary<string, int> m_SceneToCell = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> m_CellToScene = new Dictionary<int, string>();
        private readonly EventHandler<LoadSceneSuccessEventArgs> m_OnLoadSuccess;
        private readonly EventHandler<LoadSceneFailureEventArgs> m_OnLoadFailure;
        private readonly EventHandler<UnloadSceneSuccessEventArgs> m_OnUnloadSuccess;
        private readonly EventHandler<UnloadSceneFailureEventArgs> m_OnUnloadFailure;
        private bool m_Disposed;

        public SceneStreamingHandler(IWorldStreamingManager streaming, ISceneManager scenes, ISceneNameResolver resolver)
        {
            if (streaming == null) throw new FrameworkException("SceneStreamingHandler：streaming 不能为 null。");
            if (scenes == null) throw new FrameworkException("SceneStreamingHandler：scenes 不能为 null。");
            if (resolver == null) throw new FrameworkException("SceneStreamingHandler：resolver 不能为 null。");
            m_Streaming = streaming;
            m_Scenes = scenes;
            m_Resolver = resolver;
            m_OnLoadSuccess = OnLoadSuccess;
            m_OnLoadFailure = OnLoadFailure;
            m_OnUnloadSuccess = OnUnloadSuccess;
            m_OnUnloadFailure = OnUnloadFailure;
            m_Scenes.LoadSceneSuccess += m_OnLoadSuccess;
            m_Scenes.LoadSceneFailure += m_OnLoadFailure;
            m_Scenes.UnloadSceneSuccess += m_OnUnloadSuccess;
            m_Scenes.UnloadSceneFailure += m_OnUnloadFailure;
        }

        /// <summary>当前由本处理器持有（加载中或已加载）的场景数。</summary>
        public int TrackedSceneCount
        {
            get { return m_CellToScene.Count; }
        }

        public void BeginLoad(int cellId, int layer, int cx, int cz, int contentKey, int lod)
        {
            string sceneName = m_Resolver.Resolve(layer, cx, cz, contentKey);
            if (string.IsNullOrEmpty(sceneName))
            {
                m_Streaming.NotifyLoaded(cellId, true);
                return;
            }

            int existing;
            if (m_SceneToCell.TryGetValue(sceneName, out existing) && existing != cellId)
            {
                FrameworkLog.Error("SceneStreamingHandler：场景 '{0}' 已被单元 {1} 持有，单元 {2} 不能再加载它（resolver 映射重复）。", sceneName, existing, cellId);
                m_Streaming.NotifyLoaded(cellId, false);
                return;
            }

            m_SceneToCell[sceneName] = cellId;
            m_CellToScene[cellId] = sceneName;
            // LOD 越小越近 → 优先级越高
            m_Scenes.LoadScene(sceneName, 100 - lod, null);
        }

        public void CancelLoad(int cellId)
        {
            // 场景加载不可中断；迟到的成功结果会被流送管理器转成卸载。
        }

        public void BeginUnload(int cellId, int layer, int cx, int cz, int contentKey)
        {
            string sceneName;
            if (!m_CellToScene.TryGetValue(cellId, out sceneName))
            {
                m_Streaming.NotifyUnloaded(cellId);   // 无场景单元
                return;
            }

            m_Scenes.UnloadScene(sceneName, null);
        }

        public void OnLodChanged(int cellId, int fromLod, int toLod)
        {
        }

        private void OnLoadSuccess(object sender, LoadSceneSuccessEventArgs e)
        {
            int cellId;
            if (m_SceneToCell.TryGetValue(e.SceneAssetName, out cellId)) m_Streaming.NotifyLoaded(cellId, true);
        }

        private void OnLoadFailure(object sender, LoadSceneFailureEventArgs e)
        {
            int cellId;
            if (!m_SceneToCell.TryGetValue(e.SceneAssetName, out cellId)) return;
            Untrack(cellId, e.SceneAssetName);
            m_Streaming.NotifyLoaded(cellId, false);
        }

        private void OnUnloadSuccess(object sender, UnloadSceneSuccessEventArgs e)
        {
            int cellId;
            if (!m_SceneToCell.TryGetValue(e.SceneAssetName, out cellId)) return;
            Untrack(cellId, e.SceneAssetName);
            m_Streaming.NotifyUnloaded(cellId);
        }

        private void OnUnloadFailure(object sender, UnloadSceneFailureEventArgs e)
        {
            int cellId;
            if (!m_SceneToCell.TryGetValue(e.SceneAssetName, out cellId)) return;
            // 卸载失败：场景仍在内存里。回报已卸载会让管理器以为内存已释放；保守起见记录错误并按已卸载处理，
            // 否则单元永远卡在 Unloading。业务应在此类错误出现时排查场景管理器。
            FrameworkLog.Error("SceneStreamingHandler：场景 '{0}' 卸载失败，单元 {1} 仍按已卸载处理。", e.SceneAssetName, cellId);
            Untrack(cellId, e.SceneAssetName);
            m_Streaming.NotifyUnloaded(cellId);
        }

        private void Untrack(int cellId, string sceneName)
        {
            m_SceneToCell.Remove(sceneName);
            m_CellToScene.Remove(cellId);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Scenes.LoadSceneSuccess -= m_OnLoadSuccess;
            m_Scenes.LoadSceneFailure -= m_OnLoadFailure;
            m_Scenes.UnloadSceneSuccess -= m_OnUnloadSuccess;
            m_Scenes.UnloadSceneFailure -= m_OnUnloadFailure;
            m_SceneToCell.Clear();
            m_CellToScene.Clear();
        }
    }
}
