//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Units;

namespace EjoyFramework.GamePlay.Tests.Units
{
    /// <summary>
    /// 用于测试的可变 <see cref="ITargetableUnit"/> 替身。各字段公开可读写，便于在用例中按需构造场景。
    /// </summary>
    internal sealed class FakeTargetableUnit : ITargetableUnit
    {
        /// <inheritdoc/>
        public int Id { get; set; }

        /// <inheritdoc/>
        public int FactionId { get; set; }

        /// <inheritdoc/>
        public bool IsAlive { get; set; }

        /// <inheritdoc/>
        public bool IsTargetable { get; set; }

        /// <inheritdoc/>
        public float PositionX { get; set; }

        /// <inheritdoc/>
        public float PositionY { get; set; }

        /// <inheritdoc/>
        public float Health { get; set; }

        /// <inheritdoc/>
        public float MaxHealth { get; set; }

        /// <inheritdoc/>
        public float Threat { get; set; }

        /// <inheritdoc/>
        public int Priority { get; set; }

        /// <inheritdoc/>
        public float Progress { get; set; }

        /// <summary>
        /// 构造一个测试单位。未显式给出的字段使用合理默认值（存活、可瞄准、满血）。
        /// </summary>
        public FakeTargetableUnit(
            int id,
            int factionId,
            float x = 0f,
            float y = 0f,
            bool alive = true,
            bool targetable = true,
            float health = 100f,
            float maxHealth = 100f,
            float threat = 0f,
            int priority = 0,
            float progress = 0f)
        {
            Id = id;
            FactionId = factionId;
            PositionX = x;
            PositionY = y;
            IsAlive = alive;
            IsTargetable = targetable;
            Health = health;
            MaxHealth = maxHealth;
            Threat = threat;
            Priority = priority;
            Progress = progress;
        }
    }
}
