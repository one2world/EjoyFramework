//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 单位的引擎无关抽象：让目标筛选层无需依赖 Unity 或任何具体单位类即可对“可被作为目标的单位”进行推理。
    /// </summary>
    /// <remarks>
    /// 这是单位系统第一步刻意保留的解耦点：未来的 <c>UnitLogic</c>（以及任何可被瞄准的实体）实现本接口，
    /// 目标采集层（<see cref="TargetingQuery"/>）便可在不引入具体单位依赖的前提下完成阵营过滤与最佳目标选择。
    /// </remarks>
    public interface ITargetableUnit
    {
        /// <summary>
        /// 单位的唯一标识。用于在目标快照与单位实例之间回映，也用于排除自身。
        /// </summary>
        int Id { get; }

        /// <summary>
        /// 单位所属阵营的 Id，配合 <see cref="EjoyFramework.GamePlay.Factions.FactionRelations"/> 判定敌/友/中立。
        /// </summary>
        int FactionId { get; }

        /// <summary>
        /// 单位是否存活。死亡单位通常不应作为目标（受 <see cref="TargetFilter.AliveOnly"/> 控制）。
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// 单位当前是否可被瞄准。<c>false</c> 表示潜行 / 无敌 / 相位等不可选中状态（受 <see cref="TargetFilter.TargetableOnly"/> 控制）。
        /// </summary>
        bool IsTargetable { get; }

        /// <summary>
        /// 单位在世界中的 X 坐标。
        /// </summary>
        float PositionX { get; }

        /// <summary>
        /// 单位在世界中的 Y 坐标。
        /// </summary>
        float PositionY { get; }

        /// <summary>
        /// 当前血量。
        /// </summary>
        float Health { get; }

        /// <summary>
        /// 最大血量。
        /// </summary>
        float MaxHealth { get; }

        /// <summary>
        /// 累计仇恨值。
        /// </summary>
        float Threat { get; }

        /// <summary>
        /// 优先级，越大越重要。
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 沿路径已行进的进度。
        /// </summary>
        float Progress { get; }
    }
}
