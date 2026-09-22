//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Streaming;   // Vector3Lite

namespace EjoyFramework.Core.Navigation
{
    /// <summary>
    /// 寻路 / 导航管理器。业务通过此接口请求路径计算或代理移动；底层 NavMesh / A* 通过 Helper 注入。
    ///
    /// 设计：
    ///   - 抽象 Vector3Lite，core 层无 UnityEngine 依赖
    ///   - 路径查询异步（避免大场景一帧卡顿）
    ///   - Agent 模型：业务创建 NavAgent，代理在每帧自动沿 path 移动
    /// </summary>
    public interface INavigationManager
    {
        void SetHelper(INavigationHelper helper);

        /// <summary>
        /// 异步计算从 from 到 to 的路径。回调可能同帧或下一帧触发。
        /// </summary>
        void CalculatePathAsync(Vector3Lite from, Vector3Lite to, int areaMask, Action<NavPathResult> onComplete);

        /// <summary>
        /// 注册一个 NavAgent；返回 agentId。
        /// </summary>
        int RegisterAgent(NavAgentConfig config);

        /// <summary>注销 agent（卸载怪物 / 玩家时调）。</summary>
        bool UnregisterAgent(int agentId);

        /// <summary>给 agent 设置目标点；自动触发 path 计算 + 沿路移动。</summary>
        void SetDestination(int agentId, Vector3Lite destination);

        /// <summary>给 agent 设置新位置（如玩家被传送）。</summary>
        void Warp(int agentId, Vector3Lite position);

        /// <summary>查询 agent 当前位置（每帧从 Helper 更新）。</summary>
        Vector3Lite GetAgentPosition(int agentId);

        /// <summary>查询 agent 是否到达目标。</summary>
        bool HasReachedDestination(int agentId);

        /// <summary>给 agent 停止移动（保留 destination 但暂停）。</summary>
        void StopAgent(int agentId);

        /// <summary>当前注册的 agent 数。</summary>
        int AgentCount { get; }

        /// <summary>流式 navmesh：挂载一块导航网格数据，返回 tile id（&lt;= 0 失败）。</summary>
        int AddNavMeshTile(object navMeshData, Vector3Lite position);

        /// <summary>卸载一块导航网格数据。</summary>
        bool RemoveNavMeshTile(int tileId);

        /// <summary>已挂载的 tile 数。</summary>
        int NavMeshTileCount { get; }

        /// <summary>路径计算完成事件（业务可监听代替单点 callback）。</summary>
        event Action<NavPathResult> PathCalculated;
    }

    /// <summary>Helper：业务侧（Unity 层）实现具体 NavMesh / A* 后端。</summary>
    public interface INavigationHelper
    {
        /// <summary>同步或异步计算路径；helper 完成后调 onComplete(result)。</summary>
        void CalculatePath(Vector3Lite from, Vector3Lite to, int areaMask, Action<NavPathResult> onComplete);

        /// <summary>
        /// 流式 navmesh：把一块导航网格数据（Unity 为 NavMeshData 资产）按世界位置挂载，返回 helper 内部 handle。
        /// 失败返回 null。
        /// </summary>
        object AddNavMeshData(object navMeshData, Vector3Lite position);

        /// <summary>卸载一块导航网格数据。</summary>
        void RemoveNavMeshData(object helperHandle);

        /// <summary>创建一个 agent 实例，返回 helper 内部 handle（业务 wrap 为 agentId）。</summary>
        object CreateAgent(NavAgentConfig config);

        /// <summary>销毁 helper-side agent 实例。</summary>
        void DestroyAgent(object helperHandle);

        /// <summary>把目标点交给 agent，helper 自管 path 计算 + 移动。</summary>
        void SetAgentDestination(object helperHandle, Vector3Lite destination);

        /// <summary>立即定位 agent（跳过寻路）。</summary>
        void WarpAgent(object helperHandle, Vector3Lite position);

        /// <summary>查询 agent 当前世界位置。</summary>
        Vector3Lite GetAgentPosition(object helperHandle);

        /// <summary>查询是否到目的地（剩余距离 &lt; stopping distance）。</summary>
        bool HasReachedDestination(object helperHandle);

        /// <summary>停止移动（保留 destination）。</summary>
        void StopAgent(object helperHandle);
    }

    /// <summary>路径计算结果。</summary>
    public sealed class NavPathResult
    {
        public NavPathStatus Status;
        public Vector3Lite[] Corners;        // path corner 列表（首点 = from，末点 = to 或可达 endpoint）
        public float Length;                 // 累积长度
        public string ErrorMessage;          // Status != Success 时的诊断
    }

    public enum NavPathStatus
    {
        /// <summary>完整路径达到目标。</summary>
        Success,
        /// <summary>部分路径（只能走一段；目标不可达）。</summary>
        Partial,
        /// <summary>完全不可达 / NavMesh 数据缺失。</summary>
        Unreachable,
        /// <summary>helper 抛异常或参数无效。</summary>
        Error,
    }

    /// <summary>Agent 配置。</summary>
    [Serializable]
    public sealed class NavAgentConfig
    {
        public string Name;
        public float Speed = 3.5f;
        public float AngularSpeed = 120f;
        public float Acceleration = 8f;
        public float StoppingDistance = 0.5f;
        public float Radius = 0.5f;
        public float Height = 2f;
        public int AreaMask = -1;            // 全部区域
        public Vector3Lite InitialPosition;
    }
}
