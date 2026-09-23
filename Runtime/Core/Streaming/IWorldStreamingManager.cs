//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Streaming
{
    /// <summary>
    /// 世界分区流送管理器（WS3-M2 重写）。
    ///
    /// 模型：
    ///   • 世界按**整数单元坐标** (cx, cz) 分区，每个单元在某一"层"（地形 / 建筑 / 植被 / 装饰……）上是一个流送单元；
    ///     层各自有加载/卸载半径与 LOD 距离表——近处层（碰撞体、地形）半径小而必需，远处层（大体量建筑）半径大。
    ///   • 一个或多个**观察者**（玩家、相机、分屏、服务器上的多个玩家）驱动期望集；期望集 = 任一观察者
    ///     加载半径内的单元；卸载有滞回（UnloadRadius &gt; LoadRadius）。
    ///   • 期望集与活动集的差分产生加载/卸载动作；加载按"到最近观察者的距离"优先，受每帧启动数与在途数预算限制；
    ///     卸载受每帧数量预算限制；离开半径时仍在加载的单元被取消。
    ///   • 业务通过 <see cref="IWorldStreamingHandler"/>（接口，单一接收者，零分配）执行实际 IO，
    ///     异步完成后回报 <see cref="NotifyLoaded"/> / <see cref="NotifyUnloaded"/>。
    ///   • **浮动原点**：管理器内部用世界坐标；调用方渲染空间随原点平移时调用 <see cref="ShiftOrigin"/>，
    ///     观察者位置以"本地坐标 + 原点偏移"换算，单元不动。
    ///   • **同步兜底**：<see cref="RequireLoaded"/> 把某单元提到最高优先级并立即派发（玩家已经站在未加载的地面上）。
    ///
    /// 状态机（每单元）：Unloaded → Loading → Loaded → Unloading → Unloaded；Loading 中取消 → Cancelling → Unloaded。
    /// 线程契约：仅主线程。
    /// </summary>
    /// <summary>
    /// 加载/卸载完成的回报目标。管理器本身实现它；装饰器（持久化、navmesh 分块）也实现它并把回报串成链：
    /// 业务 handler → 装饰器（做完自己的事）→ 管理器。业务 handler 只持有链头的 <see cref="IWorldStreamingNotifier"/>。
    /// </summary>
    public interface IWorldStreamingNotifier
    {
        /// <summary>业务加载完成回报。加载失败也应回报（success = false）。</summary>
        void NotifyLoaded(int cellId, bool success);

        /// <summary>业务卸载完成回报。</summary>
        void NotifyUnloaded(int cellId);
    }

    public interface IWorldStreamingManager : IWorldStreamingNotifier
    {
        // ---- 配置 ----

        /// <summary>单元边长（世界单位）。所有层共用一套网格；设置后已注册单元的坐标不变（cx/cz 是逻辑索引）。</summary>
        float CellSize { get; set; }

        /// <summary>定义或更新一个层。层 id 由调用方分配（0..15）。</summary>
        void ConfigureLayer(int layer, StreamingLayerSettings settings);

        /// <summary>每帧最多启动的加载数。默认 4。</summary>
        int MaxLoadStartsPerFrame { get; set; }

        /// <summary>同时在途（Loading）的最大单元数。默认 8。</summary>
        int MaxLoadsInFlight { get; set; }

        /// <summary>每帧最多启动的卸载数。默认 4。</summary>
        int MaxUnloadsPerFrame { get; set; }

        /// <summary>观察者移动超过该距离才重新评估（避免每帧全量差分）。默认 = CellSize / 4。</summary>
        float ReevaluateMoveThreshold { get; set; }

        /// <summary>
        /// 全局距离缩放（画质档 / 设备分级用）：所有层的加载半径、卸载半径、LOD 切换距离同乘此值。默认 1，须 &gt; 0。
        /// 修改后下一次 Update 重新评估（缩小即卸载超出新半径的单元）。
        /// </summary>
        float RadiusScale { get; set; }

        /// <summary>执行 IO 的接收者。未设置时管理器只维护状态、不产生动作。</summary>
        void SetHandler(IWorldStreamingHandler handler);

        // ---- 单元登记 ----

        /// <summary>登记单元。同 (layer, cx, cz) 重复登记视为更新内容键。返回单元 id（稳定，供回报与查询）。</summary>
        int RegisterCell(int layer, int cx, int cz, int contentKey);

        /// <summary>注销单元；已加载的会先请求卸载。</summary>
        bool UnregisterCell(int cellId);

        /// <summary>按 (layer, cx, cz) 查单元 id；不存在返回 -1。</summary>
        int FindCell(int layer, int cx, int cz);

        /// <summary>读取单元信息。</summary>
        bool TryGetCell(int cellId, out StreamingCellInfo info);

        /// <summary>世界坐标 → 单元坐标。</summary>
        void WorldToCell(float x, float z, out int cx, out int cz);

        // ---- 观察者 ----

        /// <summary>设置/更新观察者位置（本地坐标，会叠加原点偏移）。</summary>
        void SetObserver(int observerId, float x, float z);

        /// <summary>移除观察者。</summary>
        bool RemoveObserver(int observerId);

        /// <summary>浮动原点平移：本地坐标系原点在世界中移动了 (dx, dz)。之后所有 SetObserver 的本地坐标按新原点换算。</summary>
        void ShiftOrigin(float dx, float dz);

        /// <summary>当前原点偏移（世界 = 本地 + 偏移）。</summary>
        void GetOrigin(out float x, out float z);

        // ---- 驱动与回报 ----

        /// <summary>强制立即重新评估（默认只在观察者移动超过阈值 / 配置变化时评估）。</summary>
        void ForceReevaluate();

        /// <summary>
        /// 同步兜底：把单元提到最高优先级并立即派发加载（无视每帧启动预算与在途上限）；
        /// 若 handler 支持同步加载，可以在 BeginLoad 内直接完成并回报。返回是否发出了加载。
        /// </summary>
        bool RequireLoaded(int cellId);

        // ---- 状态 ----

        int RegisteredCellCount { get; }
        int LoadedCellCount { get; }
        int LoadingCellCount { get; }
        int QueuedLoadCount { get; }
        int ObserverCount { get; }
        long TotalLoadsStarted { get; }
        long TotalLoadsCompleted { get; }
        long TotalLoadsCancelled { get; }
        long TotalLoadFailures { get; }
        long TotalUnloads { get; }

        /// <summary>单元状态。</summary>
        StreamingCellState GetCellState(int cellId);

        /// <summary>单元当前 LOD（未加载为 -1）。</summary>
        int GetCellLod(int cellId);

        /// <summary>已加载单元 id（非分配：写入列表，先 Clear）。</summary>
        void GetLoadedCells(List<int> results);
    }

    /// <summary>层设置。</summary>
    [Serializable]
    public sealed class StreamingLayerSettings
    {
        /// <summary>加载半径（世界单位，到单元中心）。</summary>
        public float LoadRadius = 100f;

        /// <summary>卸载半径，必须 &gt; LoadRadius（滞回）。</summary>
        public float UnloadRadius = 130f;

        /// <summary>LOD 切换距离升序表（距离 &lt; LodDistances[i] → LOD i，超过全部 → LOD 表长）；null/空 = 无 LOD。</summary>
        public float[] LodDistances;

        /// <summary>层优先级偏置：同距离下数值小者先加载（地形 0，装饰 10）。</summary>
        public int PriorityBias = 0;
    }

    /// <summary>单元状态。</summary>
    public enum StreamingCellState
    {
        None = 0,        // 无效 id
        Unloaded,
        Queued,          // 在加载队列中，尚未派发
        Loading,         // 已派发 BeginLoad，等待 NotifyLoaded
        Loaded,
        Unloading,       // 已派发 BeginUnload，等待 NotifyUnloaded
        Cancelling,      // Loading 中被取消，等待 NotifyLoaded 到达后丢弃
    }

    /// <summary>单元信息（只读快照）。</summary>
    public readonly struct StreamingCellInfo
    {
        public readonly int CellId;
        public readonly int Layer;
        public readonly int Cx;
        public readonly int Cz;
        public readonly int ContentKey;
        public readonly StreamingCellState State;
        public readonly int Lod;

        public StreamingCellInfo(int cellId, int layer, int cx, int cz, int contentKey, StreamingCellState state, int lod)
        {
            CellId = cellId;
            Layer = layer;
            Cx = cx;
            Cz = cz;
            ContentKey = contentKey;
            State = state;
            Lod = lod;
        }
    }

    /// <summary>
    /// 流送 IO 接收者。所有回调都在主线程；BeginLoad/BeginUnload 可同步完成（在回调内直接 Notify）。
    /// 回调抛出的异常被管理器隔离记录，单元按失败处理。
    /// </summary>
    public interface IWorldStreamingHandler
    {
        /// <summary>开始加载单元（首次进入或从 Unloaded 重新进入）。</summary>
        void BeginLoad(int cellId, int layer, int cx, int cz, int contentKey, int lod);

        /// <summary>取消进行中的加载（管理器随后仍会等 NotifyLoaded 到达后丢弃结果；handler 若能立即取消可直接 NotifyLoaded(false)）。</summary>
        void CancelLoad(int cellId);

        /// <summary>开始卸载。</summary>
        void BeginUnload(int cellId, int layer, int cx, int cz, int contentKey);

        /// <summary>已加载单元的 LOD 变化。</summary>
        void OnLodChanged(int cellId, int fromLod, int toLod);
    }

    /// <summary>纯 C# 三维向量（Core 层无 UnityEngine 依赖）。</summary>
    [Serializable]
    public struct Vector3Lite
    {
        public float X, Y, Z;

        public Vector3Lite(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static float Distance(Vector3Lite a, Vector3Lite b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static float SqrDistance(Vector3Lite a, Vector3Lite b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}
