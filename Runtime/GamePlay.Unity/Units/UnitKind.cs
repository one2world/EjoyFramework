//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 单位的大类标签。用于在 Unity 层快速区分一个单位的玩法角色，
    /// 与引擎无关的 <see cref="UnitModel"/> 战斗状态正交（例如同为 <see cref="Monster"/> 可有血、
    /// 同为 <see cref="Item"/> 通常无血不可摧毁）。
    /// </summary>
    public enum UnitKind
    {
        /// <summary>
        /// 角色（玩家或可控同伴）。
        /// </summary>
        Character,

        /// <summary>
        /// 怪物 / 敌人。
        /// </summary>
        Monster,

        /// <summary>
        /// 机关 / 可交互世界装置（开关、陷阱、门等）。
        /// </summary>
        Mechanism,

        /// <summary>
        /// 掉落物 / 可拾取道具。
        /// </summary>
        Item,

        /// <summary>
        /// 投射物（子弹、箭矢、法术弹等）。
        /// </summary>
        Projectile,

        /// <summary>
        /// 其它未归类单位。
        /// </summary>
        Other
    }
}
