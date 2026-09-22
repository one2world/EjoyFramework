//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Serialization;

namespace EjoyFramework.Core.Streaming
{
    /// <summary>
    /// 单元状态的采集/恢复由业务实现：Capture 把单元内"需要记住的东西"写进 buffer（拾取标记、怪物存亡、门的开关……），
    /// Restore 在单元内容加载完成后按 buffer 复原。两端都在主线程；buffer 是池化的，回调外不得持有。
    /// </summary>
    public interface ICellStateSerializer
    {
        /// <summary>单元即将卸载：把状态写入 buffer。写入 0 字节表示"无状态"（等价于删除记录）。</summary>
        void Capture(int cellId, int layer, int cx, int cz, int contentKey, ByteBuffer buffer);

        /// <summary>单元刚加载完成且有历史记录：按 buffer 恢复。</summary>
        void Restore(int cellId, int layer, int cx, int cz, int contentKey, ByteBuffer buffer);
    }

    /// <summary>
    /// 持久化装饰器：串在业务 handler 与管理器之间。
    ///   • BeginUnload：先让 <see cref="ICellStateSerializer.Capture"/> 采集，存进 <see cref="WorldStateStore"/>，再转给内层 handler 卸载；
    ///   • NotifyLoaded(success)：若存储里有该单元记录，先 <see cref="ICellStateSerializer.Restore"/>，再转给下游（通常是管理器）。
    ///   • 取消/失败/LOD 透传。
    ///
    /// 组合方式：<c>manager ← persistence ← sceneHandler</c>——业务 handler 的回报目标是 persistence，
    /// persistence 的下游是 manager；管理器的 handler 设为 persistence。
    /// </summary>
    public sealed class PersistentStreamingHandler : IWorldStreamingHandler, IWorldStreamingNotifier
    {
        private readonly IWorldStreamingManager m_Manager;
        private readonly IWorldStreamingNotifier m_Downstream;
        private readonly WorldStateStore m_Store;
        private readonly ICellStateSerializer m_Serializer;
        private IWorldStreamingHandler m_Inner;
        private long m_CaptureCount;
        private long m_RestoreCount;

        /// <param name="manager">用于读取单元信息（layer/cx/cz/contentKey）。</param>
        /// <param name="downstream">回报下游（通常就是 manager 本身，也可以是另一个装饰器）。</param>
        public PersistentStreamingHandler(IWorldStreamingManager manager, IWorldStreamingNotifier downstream, WorldStateStore store, ICellStateSerializer serializer)
        {
            if (manager == null) throw new FrameworkException("PersistentStreamingHandler：manager 不能为 null。");
            if (downstream == null) throw new FrameworkException("PersistentStreamingHandler：downstream 不能为 null。");
            if (store == null) throw new FrameworkException("PersistentStreamingHandler：store 不能为 null。");
            if (serializer == null) throw new FrameworkException("PersistentStreamingHandler：serializer 不能为 null。");
            m_Manager = manager;
            m_Downstream = downstream;
            m_Store = store;
            m_Serializer = serializer;
        }

        /// <summary>内层 handler（做真正 IO 的那个）。</summary>
        public IWorldStreamingHandler Inner
        {
            get { return m_Inner; }
            set { m_Inner = value; }
        }

        public WorldStateStore Store { get { return m_Store; } }
        public long CaptureCount { get { return m_CaptureCount; } }
        public long RestoreCount { get { return m_RestoreCount; } }

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
            Capture(cellId, layer, cx, cz, contentKey);
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
            if (success) Restore(cellId);
            m_Downstream.NotifyLoaded(cellId, success);
        }

        public void NotifyUnloaded(int cellId)
        {
            m_Downstream.NotifyUnloaded(cellId);
        }

        // ---- 内部 ----

        private void Capture(int cellId, int layer, int cx, int cz, int contentKey)
        {
            ByteBuffer buffer = ByteBuffer.Acquire();
            try
            {
                m_Serializer.Capture(cellId, layer, cx, cz, contentKey, buffer);
                m_Store.SetCell(layer, cx, cz, buffer);   // 0 字节 = 删除记录
                m_CaptureCount++;
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("PersistentStreamingHandler：Capture 抛出异常，单元 {0} 本次状态未保存：{1}", cellId, ex);
            }
            finally
            {
                buffer.Release();
            }
        }

        private void Restore(int cellId)
        {
            StreamingCellInfo info;
            if (!m_Manager.TryGetCell(cellId, out info)) return;   // 已注销的迟到结果：没有可恢复的对象
            if (info.State != StreamingCellState.Loading) return;   // Cancelling：内容马上会被卸载，不做恢复
            ByteBuffer buffer = ByteBuffer.Acquire();
            try
            {
                if (!m_Store.TryReadCell(info.Layer, info.Cx, info.Cz, buffer)) return;
                m_Serializer.Restore(cellId, info.Layer, info.Cx, info.Cz, info.ContentKey, buffer);
                m_RestoreCount++;
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("PersistentStreamingHandler：Restore 抛出异常，单元 {0} 保持加载后的默认状态：{1}", cellId, ex);
            }
            finally
            {
                buffer.Release();
            }
        }
    }
}
