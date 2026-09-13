//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Matchmaking
{
    /// <summary>
    /// 匹配/房间客户端模块。纯逻辑、与引擎无关：封装“本地玩家如何进出房间”的流程，
    /// 把创建/加入/快速匹配/离开/拉列表统一路由到注入的 <see cref="IMatchmakingBackend"/>，
    /// 并跟踪当前所在房间 <see cref="CurrentRoom"/>，在状态变迁时对外抛出事件。
    /// 后端可以是空实现、内存实现或真实网络实现；客户端逻辑对此无感知（传输无关）。
    /// 作为 <see cref="FrameworkModule"/> 注册，通过 <see cref="Framework.GetModule{T}"/> 以
    /// <see cref="IMatchmakingClient"/> 获取；使用前须先 <see cref="SetBackend"/> 注入后端。
    /// </summary>
    public sealed class MatchmakingClient : FrameworkModule, IMatchmakingClient
    {
        private IMatchmakingBackend m_Backend;
        private string m_LocalPlayerId;

        private Room m_CurrentRoom;

        /// <summary>
        /// 无参构造（<see cref="Framework.GetModule{T}"/> 经 Activator 创建所必需）。
        /// 默认退化为 <see cref="NullMatchmakingBackend"/>，使开箱即用的获取不会抛异常；
        /// 真实使用前应调用 <see cref="SetBackend"/> 注入后端、<see cref="SetLocalPlayerId"/> 设置玩家标识。
        /// </summary>
        public MatchmakingClient()
        {
            m_Backend = new NullMatchmakingBackend();
            m_LocalPlayerId = null;
            m_CurrentRoom = null;
        }

        /// <summary>
        /// 便捷构造（供测试与显式装配使用）。等价于无参构造后依次 <see cref="SetBackend"/>、<see cref="SetLocalPlayerId"/>。
        /// </summary>
        /// <param name="backend">匹配后端；为 null 时退化为 <see cref="NullMatchmakingBackend"/>。</param>
        /// <param name="localPlayerId">本地玩家唯一标识。</param>
        internal MatchmakingClient(IMatchmakingBackend backend, string localPlayerId)
            : this()
        {
            SetBackend(backend);
            SetLocalPlayerId(localPlayerId);
        }

        /// <summary>
        /// 模块优先级。无特殊轮询顺序要求，取默认 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 该模块要求外部注入后端后才能真正工作。
        /// </summary>
        public override bool RequiresConfiguration
        {
            get { return true; }
        }

        /// <summary>
        /// 是否已注入后端（非默认空后端）。
        /// </summary>
        public override bool IsModuleConfigured
        {
            get { return m_BackendConfigured; }
        }

        /// <summary>
        /// 未配置时的修复提示。
        /// </summary>
        public override string ConfigurationHint
        {
            get { return "Call SetBackend(IMatchmakingBackend) before use."; }
        }

        // 标记是否已通过 SetBackend 显式注入后端；默认的 NullMatchmakingBackend 不计为已配置。
        private bool m_BackendConfigured;

        /// <summary>
        /// 本地玩家唯一标识。未通过 <see cref="SetLocalPlayerId"/> 设置时为 null。
        /// </summary>
        public string LocalPlayerId
        {
            get { return m_LocalPlayerId; }
        }

        /// <summary>
        /// 当前所在房间；未在任何房间内时为 null。
        /// </summary>
        public Room CurrentRoom
        {
            get { return m_CurrentRoom; }
        }

        /// <summary>
        /// 注入匹配后端。为 null 时退化为 <see cref="NullMatchmakingBackend"/>（且不视为已配置）。
        /// </summary>
        public void SetBackend(IMatchmakingBackend backend)
        {
            if (backend == null)
            {
                m_Backend = new NullMatchmakingBackend();
                m_BackendConfigured = false;
                return;
            }

            m_Backend = backend;
            m_BackendConfigured = true;
        }

        /// <summary>
        /// 设置本地玩家唯一标识。
        /// </summary>
        public void SetLocalPlayerId(string localPlayerId)
        {
            m_LocalPlayerId = localPlayerId;
        }

        /// <summary>
        /// 模块轮询。匹配客户端无逐帧工作（一切由后端回调驱动），故为空实现。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理运行期状态：清空当前房间与事件订阅，后端复位为默认空实现。
        /// </summary>
        public override void Shutdown()
        {
            m_CurrentRoom = null;
            m_Backend = new NullMatchmakingBackend();
            m_BackendConfigured = false;
            OnRoomJoined = null;
            OnRoomLeft = null;
            OnRoomList = null;
        }

        /// <summary>
        /// 创建房间（本地玩家作为房主）。成功后设置 <see cref="CurrentRoom"/> 并触发 <see cref="OnRoomJoined"/>。
        /// </summary>
        /// <param name="name">房间显示名。</param>
        /// <param name="maxPlayers">最大玩家数。</param>
        /// <param name="isPrivate">是否私有房间。</param>
        public void CreateRoom(string name, int maxPlayers, bool isPrivate = false)
        {
            m_Backend.CreateRoom(name, maxPlayers, m_LocalPlayerId, isPrivate, OnEnteredRoom);
        }

        /// <summary>
        /// 加入指定房间。成功后设置 <see cref="CurrentRoom"/> 并触发 <see cref="OnRoomJoined"/>；失败则不改变当前状态。
        /// </summary>
        /// <param name="roomId">目标房间标识。</param>
        public void JoinRoom(string roomId)
        {
            m_Backend.JoinRoom(roomId, m_LocalPlayerId, OnEnteredRoom);
        }

        /// <summary>
        /// 快速匹配：加入一个等待中公开房间，或在无可用房间时新建。
        /// 成功后设置 <see cref="CurrentRoom"/> 并触发 <see cref="OnRoomJoined"/>。
        /// </summary>
        /// <param name="maxPlayers">期望的房间容量。</param>
        public void QuickMatch(int maxPlayers)
        {
            m_Backend.QuickMatch(m_LocalPlayerId, maxPlayers, OnEnteredRoom);
        }

        /// <summary>
        /// 离开当前房间。无论后端结果如何，本地都会清空 <see cref="CurrentRoom"/> 并触发 <see cref="OnRoomLeft"/>。
        /// 未在任何房间内时为空操作。
        /// </summary>
        public void Leave()
        {
            Room room = m_CurrentRoom;
            if (room == null)
            {
                return;
            }

            m_Backend.LeaveRoom(room.Id, m_LocalPlayerId, OnLeftRoom);
        }

        /// <summary>
        /// 拉取可加入的公开房间列表，结果通过 <see cref="OnRoomList"/> 抛出。
        /// </summary>
        public void RefreshRoomList()
        {
            m_Backend.ListRooms(OnRoomListReceived);
        }

        /// <summary>
        /// 进入房间的统一回调：后端返回有效房间时更新当前房间并抛出加入事件；返回 null 视为失败，不改变状态。
        /// </summary>
        private void OnEnteredRoom(Room room)
        {
            if (room == null)
            {
                return;
            }

            m_CurrentRoom = room;
            RaiseRoomJoined(room);
        }

        /// <summary>
        /// 离开房间的统一回调：清空当前房间并抛出离开事件。
        /// </summary>
        private void OnLeftRoom()
        {
            m_CurrentRoom = null;
            RaiseRoomLeft();
        }

        /// <summary>
        /// 房间列表回调：转抛 <see cref="OnRoomList"/>。
        /// </summary>
        private void OnRoomListReceived(IReadOnlyList<Room> rooms)
        {
            Action<IReadOnlyList<Room>> handler = OnRoomList;
            handler?.Invoke(rooms ?? Array.Empty<Room>());
        }

        /// <summary>
        /// 触发 <see cref="OnRoomJoined"/>。先快照委托引用，避免迭代中订阅者增删自身的风险。
        /// </summary>
        private void RaiseRoomJoined(Room room)
        {
            Action<Room> handler = OnRoomJoined;
            handler?.Invoke(room);
        }

        /// <summary>
        /// 触发 <see cref="OnRoomLeft"/>。先快照委托引用，避免迭代中订阅者增删自身的风险。
        /// </summary>
        private void RaiseRoomLeft()
        {
            Action handler = OnRoomLeft;
            handler?.Invoke();
        }

        /// <summary>
        /// 成功进入某房间（创建/加入/快速匹配）后触发，参数为所进入的房间。
        /// </summary>
        public event Action<Room> OnRoomJoined;

        /// <summary>
        /// 离开当前房间后触发。
        /// </summary>
        public event Action OnRoomLeft;

        /// <summary>
        /// <see cref="RefreshRoomList"/> 完成后触发，参数为可加入的公开房间列表。
        /// </summary>
        public event Action<IReadOnlyList<Room>> OnRoomList;
    }
}
