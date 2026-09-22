//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core;
using EjoyFramework.Core.Serialization;
using EjoyFramework.Core.Streaming;

namespace EjoyFramework.GamePlay.Population
{
    /// <summary>
    /// 种群（生成点）管理：把"哪里该有什么怪/NPC/资源点"与流送、预算、复活、持久化串起来。
    ///
    /// 模型：
    ///   • 生成点属于某个流送单元（cellId）；单元加载 → 该单元所有"活着"的生成点按预算生成实例；
    ///     单元卸载 → 实例回收（<see cref="IPopulationSpawner.Despawn"/>），生成点记住存亡与复活倒计时。
    ///   • <see cref="NotifyKilled"/>：生成点进入死亡态，<c>RespawnSeconds</c> 后（单元仍加载时）复活；
    ///     单元卸载期间**时间不流逝**（离开再回来，剩余倒计时不变）——可预测且无需世界时钟。
    ///   • 预算：每个原型（archetype）一个"最大存活数"，超预算的生成点排队，有实例回收后按最近未生成优先补。
    ///   • 持久化：实现 <see cref="ICellStateSerializer"/>，按单元内**局部序号**记录（存亡 + 剩余倒计时），
    ///     与运行期 id 无关，跨会话稳定；挂到 <see cref="PersistentStreamingHandler"/> 上即可。
    ///
    /// 零分配：槽位数组 + 每单元 List 复用；Update 只扫描"已加载且有待复活点"的单元。线程契约：主线程。
    /// </summary>
    public sealed class PopulationManager : ICellStateSerializer
    {
        private const int MaxArchetypes = 256;

        private struct SpawnPoint
        {
            public int CellId;
            public int LocalIndex;
            public int Archetype;
            public float X;
            public float Z;
            public float RespawnSeconds;
            public float RespawnRemaining;   // > 0 = 死亡等待复活
            public int Instance;             // > 0 = 已生成
            public bool Dead;
            public bool InUse;
        }

        private sealed class CellEntry
        {
            public readonly List<int> Points = new List<int>();
            public bool Loaded;
        }

        private readonly IPopulationSpawner m_Spawner;
        private readonly Dictionary<int, CellEntry> m_Cells = new Dictionary<int, CellEntry>();
        private readonly Stack<CellEntry> m_CellPool = new Stack<CellEntry>();
        private readonly List<int> m_LoadedCells = new List<int>();
        private readonly int[] m_ArchetypeBudget = new int[MaxArchetypes];
        private readonly int[] m_ArchetypeAlive = new int[MaxArchetypes];
        private SpawnPoint[] m_Points = new SpawnPoint[256];
        private int m_PointCount;
        private readonly Stack<int> m_FreePoints = new Stack<int>();
        private int m_AliveCount;
        private long m_TotalSpawned;
        private long m_TotalKilled;

        public PopulationManager(IPopulationSpawner spawner)
        {
            if (spawner == null) throw new FrameworkException("PopulationManager：spawner 不能为 null。");
            m_Spawner = spawner;
            for (int i = 0; i < MaxArchetypes; i++) m_ArchetypeBudget[i] = int.MaxValue;
        }

        /// <summary>当前存活实例数。</summary>
        public int AliveCount { get { return m_AliveCount; } }

        /// <summary>累计生成次数。</summary>
        public long TotalSpawned { get { return m_TotalSpawned; } }

        /// <summary>累计击杀（NotifyKilled）次数。</summary>
        public long TotalKilled { get { return m_TotalKilled; } }

        /// <summary>生成点总数。</summary>
        public int SpawnPointCount { get { return m_PointCount - m_FreePoints.Count; } }

        /// <summary>某原型的存活数。</summary>
        public int GetAliveCount(int archetype)
        {
            ValidateArchetype(archetype);
            return m_ArchetypeAlive[archetype];
        }

        /// <summary>设置原型的最大存活数（全局预算）。默认不限。</summary>
        public void SetArchetypeBudget(int archetype, int maxAlive)
        {
            ValidateArchetype(archetype);
            m_ArchetypeBudget[archetype] = maxAlive < 0 ? 0 : maxAlive;
        }

        // ================================================================
        //  生成点
        // ================================================================

        /// <summary>添加生成点。单元已加载时立即尝试生成。返回生成点 id。</summary>
        public int AddSpawnPoint(int cellId, int archetype, float x, float z, float respawnSeconds)
        {
            ValidateArchetype(archetype);
            if (respawnSeconds < 0f) throw new FrameworkException("PopulationManager：respawnSeconds 不能为负。");

            CellEntry cell;
            if (!m_Cells.TryGetValue(cellId, out cell))
            {
                cell = m_CellPool.Count > 0 ? m_CellPool.Pop() : new CellEntry();
                cell.Loaded = false;
                m_Cells.Add(cellId, cell);
            }

            int id = AcquirePoint();
            ref SpawnPoint p = ref m_Points[id];
            p.CellId = cellId;
            p.LocalIndex = cell.Points.Count;
            p.Archetype = archetype;
            p.X = x;
            p.Z = z;
            p.RespawnSeconds = respawnSeconds;
            p.RespawnRemaining = 0f;
            p.Instance = 0;
            p.Dead = false;
            p.InUse = true;
            cell.Points.Add(id);
            if (cell.Loaded) TrySpawn(id);
            return id;
        }

        /// <summary>移除单元的全部生成点（回收实例）。</summary>
        public void RemoveSpawnPointsOfCell(int cellId)
        {
            CellEntry cell;
            if (!m_Cells.TryGetValue(cellId, out cell)) return;
            for (int i = 0; i < cell.Points.Count; i++)
            {
                int id = cell.Points[i];
                DespawnPoint(id);
                m_Points[id] = default(SpawnPoint);
                m_FreePoints.Push(id);
            }

            cell.Points.Clear();
            m_Cells.Remove(cellId);
            if (cell.Loaded) m_LoadedCells.Remove(cellId);
            m_CellPool.Push(cell);
        }

        /// <summary>读取生成点状态。</summary>
        public bool TryGetSpawnPoint(int spawnPointId, out SpawnPointInfo info)
        {
            if (!IsValid(spawnPointId))
            {
                info = default(SpawnPointInfo);
                return false;
            }

            ref SpawnPoint p = ref m_Points[spawnPointId];
            info = new SpawnPointInfo(spawnPointId, p.CellId, p.Archetype, p.X, p.Z, p.Instance, p.Dead, p.RespawnRemaining);
            return true;
        }

        // ================================================================
        //  流送钩子
        // ================================================================

        /// <summary>单元加载完成：生成存活的生成点。</summary>
        public void OnCellLoaded(int cellId)
        {
            CellEntry cell;
            if (!m_Cells.TryGetValue(cellId, out cell) || cell.Loaded) return;
            cell.Loaded = true;
            m_LoadedCells.Add(cellId);
            for (int i = 0; i < cell.Points.Count; i++) TrySpawn(cell.Points[i]);
        }

        /// <summary>单元卸载：回收实例，保留存亡与倒计时。</summary>
        public void OnCellUnloaded(int cellId)
        {
            CellEntry cell;
            if (!m_Cells.TryGetValue(cellId, out cell) || !cell.Loaded) return;
            cell.Loaded = false;
            m_LoadedCells.Remove(cellId);
            for (int i = 0; i < cell.Points.Count; i++) DespawnPoint(cell.Points[i]);
        }

        // ================================================================
        //  玩法事件 / 推进
        // ================================================================

        /// <summary>生成点的实例被击杀：回收实例，开始复活倒计时（RespawnSeconds = 0 表示永不复活）。</summary>
        public void NotifyKilled(int spawnPointId)
        {
            if (!IsValid(spawnPointId)) return;
            ref SpawnPoint p = ref m_Points[spawnPointId];
            if (p.Instance <= 0 && p.Dead) return;
            DespawnPoint(spawnPointId);
            p.Dead = true;
            p.RespawnRemaining = p.RespawnSeconds;
            m_TotalKilled++;
        }

        /// <summary>推进复活倒计时（只影响已加载单元），到期且预算允许则复活。</summary>
        public void Update(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;
            for (int c = 0; c < m_LoadedCells.Count; c++)
            {
                CellEntry cell = m_Cells[m_LoadedCells[c]];
                for (int i = 0; i < cell.Points.Count; i++)
                {
                    int id = cell.Points[i];
                    ref SpawnPoint p = ref m_Points[id];
                    if (p.Dead && p.RespawnSeconds > 0f)
                    {
                        p.RespawnRemaining -= deltaSeconds;
                        if (p.RespawnRemaining <= 0f)
                        {
                            p.Dead = false;
                            p.RespawnRemaining = 0f;
                        }
                    }

                    if (!p.Dead && p.Instance <= 0) TrySpawn(id);   // 复活或此前超预算被压住的点
                }
            }
        }

        // ================================================================
        //  ICellStateSerializer：按局部序号持久化
        // ================================================================

        public void Capture(int cellId, int layer, int cx, int cz, int contentKey, ByteBuffer buffer)
        {
            CellEntry cell;
            if (!m_Cells.TryGetValue(cellId, out cell)) return;
            int dirty = 0;
            for (int i = 0; i < cell.Points.Count; i++)
            {
                if (m_Points[cell.Points[i]].Dead) dirty++;
            }

            if (dirty == 0) return;   // 全部存活 = 默认态，不写记录
            buffer.WriteInt(dirty);
            for (int i = 0; i < cell.Points.Count; i++)
            {
                ref SpawnPoint p = ref m_Points[cell.Points[i]];
                if (!p.Dead) continue;
                buffer.WriteInt(p.LocalIndex);
                buffer.WriteFloat(p.RespawnRemaining);
            }
        }

        public void Restore(int cellId, int layer, int cx, int cz, int contentKey, ByteBuffer buffer)
        {
            CellEntry cell;
            if (!m_Cells.TryGetValue(cellId, out cell)) return;
            int dirty = buffer.ReadInt();
            for (int i = 0; i < dirty; i++)
            {
                int localIndex = buffer.ReadInt();
                float remaining = buffer.ReadFloat();
                if (localIndex < 0 || localIndex >= cell.Points.Count) continue;   // 生成点布局变了：忽略过期记录
                int id = cell.Points[localIndex];
                ref SpawnPoint p = ref m_Points[id];
                DespawnPoint(id);
                p.Dead = true;
                p.RespawnRemaining = p.RespawnSeconds > 0f ? remaining : 0f;
            }
        }

        // ================================================================
        //  内部
        // ================================================================

        private void TrySpawn(int id)
        {
            ref SpawnPoint p = ref m_Points[id];
            if (p.Dead || p.Instance > 0) return;
            if (m_ArchetypeAlive[p.Archetype] >= m_ArchetypeBudget[p.Archetype]) return;
            int instance;
            try
            {
                instance = m_Spawner.Spawn(id, p.Archetype, p.X, p.Z, p.CellId);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("PopulationManager：Spawn 抛出异常（生成点 {0}）：{1}", id, ex);
                return;
            }

            if (instance <= 0) return;
            p.Instance = instance;
            m_ArchetypeAlive[p.Archetype]++;
            m_AliveCount++;
            m_TotalSpawned++;
        }

        private void DespawnPoint(int id)
        {
            ref SpawnPoint p = ref m_Points[id];
            if (p.Instance <= 0) return;
            int instance = p.Instance;
            p.Instance = 0;
            m_ArchetypeAlive[p.Archetype]--;
            m_AliveCount--;
            try { m_Spawner.Despawn(instance, id); }
            catch (Exception ex) { FrameworkLog.Error("PopulationManager：Despawn 抛出异常（生成点 {0}）：{1}", id, ex); }
        }

        private int AcquirePoint()
        {
            if (m_FreePoints.Count > 0) return m_FreePoints.Pop();
            if (m_PointCount == m_Points.Length) Array.Resize(ref m_Points, m_Points.Length * 2);
            return m_PointCount++;
        }

        private bool IsValid(int id)
        {
            return id >= 0 && id < m_PointCount && m_Points[id].InUse;
        }

        private static void ValidateArchetype(int archetype)
        {
            if (archetype < 0 || archetype >= MaxArchetypes) throw new FrameworkException("PopulationManager：archetype 越界（0..255）。");
        }
    }

    /// <summary>业务侧实例化：生成/回收真正的游戏对象（通常经 SpawnPool）。</summary>
    public interface IPopulationSpawner
    {
        /// <summary>生成实例；返回 &gt; 0 的实例句柄，失败返回 0。</summary>
        int Spawn(int spawnPointId, int archetype, float x, float z, int cellId);

        /// <summary>回收实例。</summary>
        void Despawn(int instance, int spawnPointId);
    }

    /// <summary>生成点只读快照。</summary>
    public readonly struct SpawnPointInfo
    {
        public readonly int Id;
        public readonly int CellId;
        public readonly int Archetype;
        public readonly float X;
        public readonly float Z;
        public readonly int Instance;
        public readonly bool Dead;
        public readonly float RespawnRemaining;

        public SpawnPointInfo(int id, int cellId, int archetype, float x, float z, int instance, bool dead, float respawnRemaining)
        {
            Id = id;
            CellId = cellId;
            Archetype = archetype;
            X = x;
            Z = z;
            Instance = instance;
            Dead = dead;
            RespawnRemaining = respawnRemaining;
        }
    }
}
