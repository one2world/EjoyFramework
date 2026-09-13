//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Battle
{
    /// <summary>战斗实体抽象：所有可参战目标（角色 / 怪物 / 建筑）实现此接口。</summary>
    public interface IBattleEntity
    {
        int EntityId { get; }
        bool IsAlive { get; }

        // 数值
        int CurrentHp { get; set; }
        int MaxHp { get; }
        int Attack { get; }
        int Defense { get; }
        int Camp { get; }   // 阵营 (0=玩家, 1=敌军, 2=中立 ...)

        // 应用伤害（业务可在内部触发 OnDamaged event）
        void ApplyDamage(int amount, IBattleEntity attacker);

        // 应用治疗
        void ApplyHeal(int amount, IBattleEntity healer);
    }
}
