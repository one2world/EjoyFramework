//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Matchmaking
{
    /// <summary>
    /// 房间（大厅）生命周期状态。纯逻辑、与引擎无关。
    /// 由匹配/房间逻辑驱动：从 <see cref="Waiting"/> 等待玩家，经 <see cref="Starting"/> 准备开局，
    /// 进入 <see cref="InGame"/> 对局中，最终在所有人离开后变为 <see cref="Closed"/>。
    /// </summary>
    public enum RoomState
    {
        /// <summary>
        /// 等待中：房间可被列出、可加入；这是房间创建后的初始状态。
        /// </summary>
        Waiting,

        /// <summary>
        /// 开局准备中：通常在所有占位玩家就绪后切入，用于倒计时或建立 netcode 连接。
        /// </summary>
        Starting,

        /// <summary>
        /// 对局进行中：不再对外列出，也不再接受新玩家加入。
        /// </summary>
        InGame,

        /// <summary>
        /// 已关闭：房间为空或被显式关闭后的终态，不可再加入。
        /// </summary>
        Closed,
    }
}
