//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core;
using EjoyFramework.Core.Streaming;

namespace EjoyFramework.GamePlay.Population
{
    /// <summary>
    /// 把 <see cref="PopulationManager"/> 接进流送链的装饰器：
    ///   • NotifyLoaded(success) → <see cref="PopulationManager.OnCellLoaded"/>（生成存活点）→ 下游；
    ///   • BeginUnload → <see cref="PopulationManager.OnCellUnloaded"/>（回收实例）→ 内层。
    ///
    /// 推荐接线（加载时先恢复存亡再生成）：
    /// <code>
    /// handler 链：manager.SetHandler(persistence); persistence.Inner = population; population.Inner = scene;
    /// 回报 链：scene → persistence（Restore）→ population（OnCellLoaded）→ manager
    ///   即 new SceneStreamingHandler(persistence, …)，new PersistentStreamingHandler(manager, downstream: population, …)，
    ///      new PopulationStreamingHandler(manager, downstream: manager, population)
    /// </code>
    /// </summary>
    public sealed class PopulationStreamingHandler : IWorldStreamingHandler, IWorldStreamingNotifier
    {
        private readonly IWorldStreamingManager m_Manager;
        private readonly IWorldStreamingNotifier m_Downstream;
        private readonly PopulationManager m_Population;
        private IWorldStreamingHandler m_Inner;

        public PopulationStreamingHandler(IWorldStreamingManager manager, IWorldStreamingNotifier downstream, PopulationManager population)
        {
            if (manager == null) throw new FrameworkException("PopulationStreamingHandler：manager 不能为 null。");
            if (downstream == null) throw new FrameworkException("PopulationStreamingHandler：downstream 不能为 null。");
            if (population == null) throw new FrameworkException("PopulationStreamingHandler：population 不能为 null。");
            m_Manager = manager;
            m_Downstream = downstream;
            m_Population = population;
        }

        public IWorldStreamingHandler Inner
        {
            get { return m_Inner; }
            set { m_Inner = value; }
        }

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
            m_Population.OnCellUnloaded(cellId);
            if (m_Inner != null) m_Inner.BeginUnload(cellId, layer, cx, cz, contentKey);
            else m_Downstream.NotifyUnloaded(cellId);
        }

        public void OnLodChanged(int cellId, int fromLod, int toLod)
        {
            if (m_Inner != null) m_Inner.OnLodChanged(cellId, fromLod, toLod);
        }

        public void NotifyLoaded(int cellId, bool success)
        {
            if (success)
            {
                StreamingCellInfo info;
                if (m_Manager.TryGetCell(cellId, out info) && info.State == StreamingCellState.Loading)
                {
                    m_Population.OnCellLoaded(cellId);
                }
            }

            m_Downstream.NotifyLoaded(cellId, success);
        }

        public void NotifyUnloaded(int cellId)
        {
            m_Downstream.NotifyUnloaded(cellId);
        }
    }
}
