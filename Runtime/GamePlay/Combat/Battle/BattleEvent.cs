//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Battle
{
    /// <summary>
    /// 战斗事件类型。每个原语（伤害/治疗/回合）在执行时会产出一个或多个此类事件，并据此触发 <see cref="BattleTrigger"/>。
    /// </summary>
    public enum BattleEventType
    {
        /// <summary>
        /// 造成伤害：从攻击方视角发出。匹配触发器时按事件的 <see cref="BattleEvent.SourceId"/> 归属。
        /// </summary>
        DamageDealt,

        /// <summary>
        /// 受到伤害：从受击方视角发出。匹配触发器时按事件的 <see cref="BattleEvent.TargetId"/> 归属。
        /// </summary>
        DamageTaken,

        /// <summary>
        /// 单位阵亡。匹配触发器时按事件的 <see cref="BattleEvent.TargetId"/>（阵亡者）归属。
        /// </summary>
        UnitDied,

        /// <summary>
        /// 接受治疗。匹配触发器时按事件的 <see cref="BattleEvent.TargetId"/>（被治疗者）归属。
        /// </summary>
        Healed,

        /// <summary>
        /// 回合开始。匹配触发器时按事件的 <see cref="BattleEvent.TargetId"/>（行动者）归属。
        /// </summary>
        TurnStarted,

        /// <summary>
        /// 回合结束。匹配触发器时按事件的 <see cref="BattleEvent.TargetId"/>（行动者）归属。
        /// </summary>
        TurnEnded
    }

    /// <summary>
    /// 战斗事件：不可变值类型，描述一次已发生的战斗结果。
    /// <see cref="Amount"/> 表示伤害或治疗的最终数值（已经过减免/钳制）。
    /// </summary>
    public readonly struct BattleEvent
    {
        /// <summary>
        /// 事件类型。
        /// </summary>
        public BattleEventType Type { get; }

        /// <summary>
        /// 来源单位标识（攻击方/治疗方/回合行动者来源）；可能为 null。
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        /// 目标单位标识（受击方/被治疗方/阵亡者/回合行动者）；可能为 null。
        /// </summary>
        public string TargetId { get; }

        /// <summary>
        /// 数值（减免后的伤害量或钳制后的治疗量）；与数值无关的事件为 0。
        /// </summary>
        public int Amount { get; }

        /// <summary>
        /// 构造战斗事件。
        /// </summary>
        /// <param name="type">事件类型。</param>
        /// <param name="sourceId">来源单位标识。</param>
        /// <param name="targetId">目标单位标识。</param>
        /// <param name="amount">数值。</param>
        public BattleEvent(BattleEventType type, string sourceId, string targetId, int amount)
        {
            Type = type;
            SourceId = sourceId;
            TargetId = targetId;
            Amount = amount;
        }
    }
}
