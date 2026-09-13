//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Achievements
{
    /// <summary>
    /// 成就定义（不可变）。布尔型成就用 <see cref="TargetCount"/> = 1；累计型成就（如"击杀 100 个敌人"）
    /// 用 TargetCount = 100，通过 <c>AddProgress</c> 累加。
    /// </summary>
    public sealed class AchievementDefinition
    {
        /// <summary>唯一标识。</summary>
        public string Id { get; }

        /// <summary>达成所需累计量，&gt;= 1。</summary>
        public int TargetCount { get; }

        /// <summary>成就点数/分值（用于汇总玩家成就分），&gt;= 0。</summary>
        public int Points { get; }

        /// <summary>隐藏成就：达成前一般不向玩家展示细节。</summary>
        public bool Hidden { get; }

        /// <summary>
        /// 构造成就定义。
        /// </summary>
        /// <param name="id">唯一标识，不能为空。</param>
        /// <param name="targetCount">达成所需累计量，&gt;= 1（默认 1，即布尔型）。</param>
        /// <param name="points">成就点数，&gt;= 0（默认 0）。</param>
        /// <param name="hidden">是否隐藏成就（默认 false）。</param>
        public AchievementDefinition(string id, int targetCount = 1, int points = 0, bool hidden = false)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("成就 Id 不能为空。", nameof(id));
            }
            if (targetCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(targetCount), "TargetCount 必须 >= 1。");
            }
            if (points < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points), "Points 不能为负。");
            }

            Id = id;
            TargetCount = targetCount;
            Points = points;
            Hidden = hidden;
        }
    }
}
