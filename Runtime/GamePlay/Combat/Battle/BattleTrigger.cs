//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Battle
{
    /// <summary>
    /// 战斗触发器：当指定类型 <see cref="On"/> 的事件被引擎处理时，若归属匹配则调用 <see cref="Handler"/>。
    /// 处理函数可在内部调用 <see cref="BattleEngine.DealDamage"/> / <see cref="BattleEngine.Heal"/> 产生连锁事件（效果链）。
    /// </summary>
    /// <remarks>
    /// 归属（OwnerId）匹配规则：
    /// <list type="bullet">
    /// <item><description><see cref="OwnerId"/> 为 null：全局触发器，对任意单位的该类事件都触发。</description></item>
    /// <item><description><see cref="BattleEventType.DamageDealt"/>：当 <see cref="OwnerId"/> 等于事件的 <see cref="BattleEvent.SourceId"/>（攻击方）时触发。</description></item>
    /// <item><description><see cref="BattleEventType.DamageTaken"/> / <see cref="BattleEventType.UnitDied"/> /
    /// <see cref="BattleEventType.Healed"/> / <see cref="BattleEventType.TurnStarted"/> /
    /// <see cref="BattleEventType.TurnEnded"/>：当 <see cref="OwnerId"/> 等于事件的 <see cref="BattleEvent.TargetId"/> 时触发。</description></item>
    /// </list>
    /// </remarks>
    public sealed class BattleTrigger
    {
        /// <summary>
        /// 监听的事件类型。
        /// </summary>
        public BattleEventType On { get; }

        /// <summary>
        /// 归属单位标识。为 null 表示全局触发器（对任意单位的该类事件触发）。
        /// </summary>
        public string OwnerId { get; }

        /// <summary>
        /// 处理函数：参数为 (引擎, 触发该次调用的事件)。
        /// </summary>
        public Action<BattleEngine, BattleEvent> Handler { get; }

        /// <summary>
        /// 构造触发器。
        /// </summary>
        /// <param name="on">监听的事件类型。</param>
        /// <param name="ownerId">归属单位标识；null 为全局。</param>
        /// <param name="handler">处理函数，不可为空。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="handler"/> 为空时抛出。</exception>
        public BattleTrigger(BattleEventType on, string ownerId, Action<BattleEngine, BattleEvent> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            On = on;
            OwnerId = ownerId;
            Handler = handler;
        }

        /// <summary>
        /// 判断本触发器是否匹配给定事件（类型一致且归属匹配）。
        /// </summary>
        /// <param name="evt">待匹配事件。</param>
        /// <returns>匹配返回 true。</returns>
        internal bool Matches(in BattleEvent evt)
        {
            if (On != evt.Type)
            {
                return false;
            }

            // 全局触发器：任意单位均匹配。
            if (OwnerId == null)
            {
                return true;
            }

            // DamageDealt 按攻击方（SourceId）归属；其余按目标（TargetId）归属。
            string ownerCandidate = evt.Type == BattleEventType.DamageDealt ? evt.SourceId : evt.TargetId;
            return string.Equals(OwnerId, ownerCandidate, StringComparison.Ordinal);
        }
    }
}
