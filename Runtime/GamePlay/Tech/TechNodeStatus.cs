//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Tech
{
    /// <summary>
    /// 科技 / 升级树中单个节点的状态。
    /// </summary>
    public enum TechNodeStatus
    {
        /// <summary>
        /// 锁定：存在尚未解锁（等级 &lt; 1）的前置节点，当前不可解锁。
        /// </summary>
        Locked,

        /// <summary>
        /// 可用：所有前置节点均已解锁，且自身等级为 0，可以进行首次解锁。
        /// </summary>
        Available,

        /// <summary>
        /// 已解锁：自身等级介于 1 与 MaxLevel 之间（不含 MaxLevel），仍可继续升级。
        /// </summary>
        Unlocked,

        /// <summary>
        /// 已满级：自身等级等于 MaxLevel，无法继续升级。
        /// </summary>
        Maxed
    }
}
