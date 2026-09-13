//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Streaming
{
    /// <summary>
    /// 大世界资源 streaming：基于 player position 自动加载 / 卸载 chunk。
    ///
    /// 流程：
    ///   1. 业务 RegisterChunk 注册所有 chunk 信息（中心 + 半径 + bundles + LOD levels）
    ///   2. 业务 SetPlayerPosition(pos) 每帧（或每秒）调一次
    ///   3. Manager 按 StreamingPolicy 决定哪些 chunk 应进入 active 集，触发 ChunkLoadRequested / ChunkUnloadRequested 事件
    ///   4. 业务监听事件并调 ResourceManager / SceneManager 完成实际加载
    /// </summary>
    public interface IWorldStreamingManager
    {
        /// <summary>注入 streaming policy（决定 chunk 何时加载 / 卸载）。</summary>
        void SetPolicy(StreamingPolicy policy);

        /// <summary>注册一个 chunk。</summary>
        void RegisterChunk(ChunkInfo chunk);

        /// <summary>注销 chunk。</summary>
        bool UnregisterChunk(string chunkId);

        /// <summary>更新玩家位置（每帧或每 N 帧调）。Manager 据此决定加载 / 卸载。</summary>
        void SetPlayerPosition(Vector3Lite position);

        /// <summary>已注册的 chunk 总数。</summary>
        int RegisteredChunkCount { get; }

        /// <summary>当前 active（已加载）的 chunk 数量。</summary>
        int ActiveChunkCount { get; }

        /// <summary>触发：当某 chunk 进入加载半径。业务应在此回调中实际加载资源。</summary>
        event Action<ChunkInfo, int> ChunkLoadRequested;   // chunk, lod level

        /// <summary>触发：当某 chunk 离开加载半径。业务应在此回调中卸载资源。</summary>
        event Action<ChunkInfo> ChunkUnloadRequested;

        /// <summary>触发：当某 chunk 的 LOD 级别改变（业务可不接，按需做平滑过渡）。</summary>
        event Action<ChunkInfo, int, int> ChunkLodChanged;   // chunk, fromLod, toLod

        /// <summary>强制重新评估所有 chunk（例如玩家瞬移后）。</summary>
        void ForceReevaluate();

        /// <summary>列出当前 active 的 chunk。每次调用分配新数组，仅限主线程。</summary>
        ChunkInfo[] GetActiveChunks();

        /// <summary>无分配版本：将当前 active 的 chunk 填入调用方列表（先 Clear）。仅限主线程。</summary>
        void GetActiveChunks(List<ChunkInfo> results);
    }

    /// <summary>
    /// 轻量 Vector3（无 UnityEngine 依赖；core 层独立可测）。
    /// 业务侧可与 UnityEngine.Vector3 互转。
    /// </summary>
    [Serializable]
    public struct Vector3Lite
    {
        public float X, Y, Z;
        public Vector3Lite(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static float Distance(Vector3Lite a, Vector3Lite b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static float SqrDistance(Vector3Lite a, Vector3Lite b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }

    /// <summary>Chunk 元信息：中心 + 半径 + 依赖 bundles + LOD 级别。</summary>
    [Serializable]
    public sealed class ChunkInfo
    {
        public string ChunkId;
        public Vector3Lite Center;
        public float Radius;          // 自身边界半径
        public string[] BundleNames;  // 业务加载所需 bundle 列表
        public float[] LodDistances;  // LOD 切换距离（从 LOD 0 到 N-1 的边界）；null 表示无 LOD
    }

    /// <summary>Streaming 策略：决定加载半径 / LOD 阈值。</summary>
    [Serializable]
    public sealed class StreamingPolicy
    {
        /// <summary>距玩家此距离内的 chunk 加载。</summary>
        public float LoadRadius = 100f;
        /// <summary>距玩家此距离外的 chunk 卸载（>LoadRadius，提供滞后避免抖动）。</summary>
        public float UnloadRadius = 130f;
        /// <summary>每帧最多评估的 chunk 数（>0 启用 budget 控制）。0 表示不限。</summary>
        public int MaxChunksPerFrame = 16;
    }
}
