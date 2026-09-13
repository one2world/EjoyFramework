//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Streaming
{
    internal sealed class WorldStreamingManager : FrameworkModule, IWorldStreamingManager
    {
        private readonly Dictionary<string, ChunkInfo> m_AllChunks = new Dictionary<string, ChunkInfo>();
        private readonly HashSet<string> m_Active = new HashSet<string>();
        private readonly Dictionary<string, int> m_CurrentLod = new Dictionary<string, int>();
        // 稳定有序的 chunkId 列表，供轮询游标使用（Dictionary 迭代顺序不稳定，无法跨帧定位）。
        private readonly List<string> m_ChunkOrder = new List<string>();
        // 轮询游标：跨帧推进，保证预算受限时所有 chunk 最终都被评估，远处 chunk 不会饿死。
        private int m_RoundRobinCursor;
        private StreamingPolicy m_Policy = new StreamingPolicy();
        private Vector3Lite m_PlayerPos;
        private bool m_PositionDirty;

        public override int Priority { get { return 5; } }   // 早于业务模块
        public override void Update(float a, float b)
        {
            if (m_PositionDirty)
            {
                m_PositionDirty = false;
                Reevaluate();
            }
        }
        public override void Shutdown()
        {
            m_AllChunks.Clear();
            m_Active.Clear();
            m_CurrentLod.Clear();
            m_ChunkOrder.Clear();
            m_RoundRobinCursor = 0;
        }

        public event Action<ChunkInfo, int> ChunkLoadRequested;
        public event Action<ChunkInfo> ChunkUnloadRequested;
        public event Action<ChunkInfo, int, int> ChunkLodChanged;

        public int RegisteredChunkCount => m_AllChunks.Count;
        public int ActiveChunkCount => m_Active.Count;

        public void SetPolicy(StreamingPolicy policy)
        {
            Framework.EnsureMainThread(nameof(SetPolicy));
            if (policy == null) throw new FrameworkException("policy is null.");
            if (policy.UnloadRadius <= policy.LoadRadius)
                throw new FrameworkException("UnloadRadius must be > LoadRadius for hysteresis.");
            m_Policy = policy;
            m_PositionDirty = true;
        }

        public void RegisterChunk(ChunkInfo chunk)
        {
            Framework.EnsureMainThread(nameof(RegisterChunk));
            if (chunk == null) throw new FrameworkException("chunk is null.");
            if (string.IsNullOrEmpty(chunk.ChunkId)) throw new FrameworkException("chunk.ChunkId is empty.");
            if (!m_AllChunks.ContainsKey(chunk.ChunkId)) m_ChunkOrder.Add(chunk.ChunkId);
            m_AllChunks[chunk.ChunkId] = chunk;
            m_PositionDirty = true;
        }

        public bool UnregisterChunk(string chunkId)
        {
            Framework.EnsureMainThread(nameof(UnregisterChunk));
            if (!m_AllChunks.TryGetValue(chunkId, out var chunk)) return false;
            m_AllChunks.Remove(chunkId);
            m_ChunkOrder.Remove(chunkId);
            if (m_RoundRobinCursor >= m_ChunkOrder.Count) m_RoundRobinCursor = 0;
            if (m_Active.Remove(chunkId))
            {
                m_CurrentLod.Remove(chunkId);
                try { ChunkUnloadRequested?.Invoke(chunk); }
                catch (Exception ex) { FrameworkLog.Error("ChunkUnloadRequested handler threw: {0}", ex); }
            }
            return true;
        }

        public void SetPlayerPosition(Vector3Lite position)
        {
            Framework.EnsureMainThread(nameof(SetPlayerPosition));
            m_PlayerPos = position;
            m_PositionDirty = true;
        }

        public void ForceReevaluate()
        {
            Framework.EnsureMainThread(nameof(ForceReevaluate));
            Reevaluate();
        }

        /// <summary>
        /// 返回当前 active 的所有 chunk。
        /// 注意：每次调用都会分配一个新数组，且只能在主线程调用。
        /// 热路径请改用 <see cref="GetActiveChunks(List{ChunkInfo})"/> 无分配重载。
        /// </summary>
        public ChunkInfo[] GetActiveChunks()
        {
            var arr = new ChunkInfo[m_Active.Count];
            int i = 0;
            foreach (var id in m_Active) arr[i++] = m_AllChunks[id];
            return arr;
        }

        /// <summary>
        /// 无分配版本：将当前 active 的 chunk 填入调用方提供的列表（先 Clear）。仅限主线程调用。
        /// </summary>
        public void GetActiveChunks(List<ChunkInfo> results)
        {
            if (results == null) throw new FrameworkException("results is null.");
            results.Clear();
            foreach (var id in m_Active) results.Add(m_AllChunks[id]);
        }

        // ===== 核心评估循环 =====

        private bool m_Evaluating;

        private void Reevaluate()
        {
            // 防重入：ChunkLoad/Unload 回调里若调用 ForceReevaluate/RegisterChunk 等触发嵌套评估，
            // 推迟到下一帧而非在当前评估中途修改 m_Active/m_ChunkOrder。
            if (m_Evaluating) { m_PositionDirty = true; return; }
            m_Evaluating = true;
            try
            {
                ReevaluateCore();
            }
            finally
            {
                m_Evaluating = false;
            }
        }

        private void ReevaluateCore()
        {
            // 1. 决定每个 chunk 应该 active 否
            List<string> toLoad = null, toUnload = null;
            var lodChanges = new List<(string, int, int)>();
            int budget = m_Policy.MaxChunksPerFrame > 0 ? m_Policy.MaxChunksPerFrame : int.MaxValue;
            int total = m_ChunkOrder.Count;
            // 本帧最多评估的 chunk 数：受预算和总数共同约束。
            int toEvaluate = total < budget ? total : budget;
            if (m_RoundRobinCursor >= total) m_RoundRobinCursor = 0;

            for (int n = 0; n < toEvaluate; n++)
            {
                // 轮询游标：从上次停下的位置继续，环绕推进，保证所有 chunk 最终都被评估。
                if (m_RoundRobinCursor >= total) m_RoundRobinCursor = 0;
                string chunkId = m_ChunkOrder[m_RoundRobinCursor];
                m_RoundRobinCursor++;

                var chunk = m_AllChunks[chunkId];
                float dist = Vector3Lite.Distance(m_PlayerPos, chunk.Center);
                float effectiveDist = dist - chunk.Radius;   // 玩家到 chunk 边界的距离
                bool currentlyActive = m_Active.Contains(chunk.ChunkId);

                if (currentlyActive)
                {
                    // 滞后卸载：超 UnloadRadius 才卸
                    if (effectiveDist > m_Policy.UnloadRadius)
                    {
                        (toUnload ??= new List<string>()).Add(chunk.ChunkId);
                    }
                    else
                    {
                        // 检查 LOD 变化
                        int newLod = ComputeLod(chunk, dist);
                        if (m_CurrentLod.TryGetValue(chunk.ChunkId, out int oldLod) && oldLod != newLod)
                        {
                            lodChanges.Add((chunk.ChunkId, oldLod, newLod));
                            m_CurrentLod[chunk.ChunkId] = newLod;
                        }
                    }
                }
                else
                {
                    if (effectiveDist <= m_Policy.LoadRadius)
                    {
                        (toLoad ??= new List<string>()).Add(chunk.ChunkId);
                    }
                }
            }

            // 预算受限未评估完全部 chunk：标脏，下一帧 Update 从游标处继续。
            if (toEvaluate < total) m_PositionDirty = true;

            // 2. 应用变更（先卸载释放内存，再加载）
            if (toUnload != null)
            {
                foreach (var id in toUnload)
                {
                    var chunk = m_AllChunks[id];
                    m_Active.Remove(id);
                    m_CurrentLod.Remove(id);
                    try { ChunkUnloadRequested?.Invoke(chunk); }
                    catch (Exception ex) { FrameworkLog.Error("ChunkUnloadRequested handler threw: {0}", ex); }
                }
            }
            if (toLoad != null)
            {
                foreach (var id in toLoad)
                {
                    var chunk = m_AllChunks[id];
                    m_Active.Add(id);
                    int lod = ComputeLod(chunk, Vector3Lite.Distance(m_PlayerPos, chunk.Center));
                    m_CurrentLod[id] = lod;
                    try { ChunkLoadRequested?.Invoke(chunk, lod); }
                    catch (Exception ex) { FrameworkLog.Error("ChunkLoadRequested handler threw: {0}", ex); }
                }
            }
            foreach (var (id, oldLod, newLod) in lodChanges)
            {
                try { ChunkLodChanged?.Invoke(m_AllChunks[id], oldLod, newLod); }
                catch (Exception ex) { FrameworkLog.Error("ChunkLodChanged handler threw: {0}", ex); }
            }
        }

        private static int ComputeLod(ChunkInfo chunk, float distance)
        {
            if (chunk.LodDistances == null || chunk.LodDistances.Length == 0) return 0;
            for (int i = 0; i < chunk.LodDistances.Length; i++)
            {
                if (distance < chunk.LodDistances[i]) return i;
            }
            return chunk.LodDistances.Length;   // 最远 LOD
        }
    }
}
