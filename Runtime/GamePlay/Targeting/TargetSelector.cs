//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Targeting
{
    /// <summary>
    /// 目标选择器。在一组候选 <see cref="TargetInfo"/> 中，依据 <see cref="TargetingStrategy"/>
    /// 与可选射程，挑选出相对于来源点的“最佳”目标。
    /// </summary>
    /// <remarks>
    /// 选择过程不分配堆内存、不使用 LINQ，单次线性遍历候选列表即可完成，适合每帧高频调用（塔防选敌、AI 选仇恨目标）。
    /// 射程过滤使用 <see cref="SqrDistance"/>（避免开方）；<c>maxRange &lt;= 0</c> 表示无限射程。
    /// 所有比较在出现平局时统一以“较小 <see cref="TargetInfo.Id"/>”裁决，保证结果完全确定、可复现。
    /// </remarks>
    public static class TargetSelector
    {
        /// <summary>
        /// 依据策略在候选中挑选最佳目标。
        /// </summary>
        /// <param name="candidates">候选目标列表（只读）。可为空列表，但不可为 null。</param>
        /// <param name="sourceX">来源点 X 坐标（射程与距离类策略的参考原点）。</param>
        /// <param name="sourceY">来源点 Y 坐标。</param>
        /// <param name="strategy">选择策略。</param>
        /// <param name="maxRange">最大射程；<c>&lt;= 0</c> 表示不做射程过滤。</param>
        /// <param name="best">输出：命中的最佳目标；返回 false 时为默认值。</param>
        /// <returns>存在合格目标时返回 <c>true</c>；候选为空或全部超出射程时返回 <c>false</c>。</returns>
        public static bool TrySelect(
            IReadOnlyList<TargetInfo> candidates,
            float sourceX, float sourceY, TargetingStrategy strategy, float maxRange,
            out TargetInfo best)
        {
            best = default;
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            bool hasRange = maxRange > 0f;
            float maxRangeSqr = maxRange * maxRange;

            bool found = false;
            // 当前最佳的“评分”，方向（越大越好 / 越小越好）由策略决定。
            float bestScore = 0f;
            TargetInfo bestCandidate = default;

            for (int i = 0; i < candidates.Count; i++)
            {
                TargetInfo candidate = candidates[i];

                float distSqr = SqrDistance(sourceX, sourceY, candidate.X, candidate.Y);
                if (hasRange && distSqr > maxRangeSqr)
                {
                    continue;
                }

                float score = ScoreOf(candidate, strategy, distSqr);
                bool preferHigher = PreferHigher(strategy);

                if (!found)
                {
                    found = true;
                    bestScore = score;
                    bestCandidate = candidate;
                    continue;
                }

                if (IsBetter(score, bestScore, preferHigher))
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
                else if (score == bestScore && candidate.Id < bestCandidate.Id)
                {
                    // 评分相同 → 确定性平局裁决：取较小 Id。
                    bestCandidate = candidate;
                }
            }

            if (found)
            {
                best = bestCandidate;
            }

            return found;
        }

        /// <summary>
        /// 计算两点间的欧氏距离。
        /// </summary>
        /// <param name="ax">点 A 的 X。</param>
        /// <param name="ay">点 A 的 Y。</param>
        /// <param name="bx">点 B 的 X。</param>
        /// <param name="by">点 B 的 Y。</param>
        /// <returns>两点间距离。</returns>
        public static float Distance(float ax, float ay, float bx, float by)
        {
            return (float)System.Math.Sqrt(SqrDistance(ax, ay, bx, by));
        }

        /// <summary>
        /// 计算两点间距离的平方（避免开方，用于射程比较与“最近/最远”排序）。
        /// </summary>
        /// <param name="ax">点 A 的 X。</param>
        /// <param name="ay">点 A 的 Y。</param>
        /// <param name="bx">点 B 的 X。</param>
        /// <param name="by">点 B 的 Y。</param>
        /// <returns>两点间距离的平方。</returns>
        public static float SqrDistance(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx;
            float dy = ay - by;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// 将候选按策略折算为可比较的单一评分。距离类策略复用已算好的距离平方以免重复计算。
        /// </summary>
        private static float ScoreOf(TargetInfo candidate, TargetingStrategy strategy, float distSqr)
        {
            switch (strategy)
            {
                case TargetingStrategy.Nearest:
                case TargetingStrategy.Farthest:
                    // 距离平方与距离单调一致，可直接用于最近/最远比较。
                    return distSqr;
                case TargetingStrategy.LowestHealth:
                case TargetingStrategy.HighestHealth:
                    return candidate.Health;
                case TargetingStrategy.LowestHealthPercent:
                    return HealthPercent(candidate);
                case TargetingStrategy.HighestThreat:
                    return candidate.Threat;
                case TargetingStrategy.HighestPriority:
                    return candidate.Priority;
                case TargetingStrategy.FirstInProgress:
                case TargetingStrategy.LastInProgress:
                    return candidate.Progress;
                default:
                    return distSqr;
            }
        }

        /// <summary>
        /// 指示该策略是“评分越大越好”还是“越小越好”。
        /// </summary>
        private static bool PreferHigher(TargetingStrategy strategy)
        {
            switch (strategy)
            {
                case TargetingStrategy.Farthest:
                case TargetingStrategy.HighestHealth:
                case TargetingStrategy.HighestThreat:
                case TargetingStrategy.HighestPriority:
                case TargetingStrategy.FirstInProgress:
                    return true;
                case TargetingStrategy.Nearest:
                case TargetingStrategy.LowestHealth:
                case TargetingStrategy.LowestHealthPercent:
                case TargetingStrategy.LastInProgress:
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 依据偏好方向判断 <paramref name="score"/> 是否严格优于 <paramref name="current"/>。
        /// </summary>
        private static bool IsBetter(float score, float current, bool preferHigher)
        {
            return preferHigher ? score > current : score < current;
        }

        /// <summary>
        /// 计算血量百分比。<see cref="TargetInfo.MaxHealth"/> 非正时退化为绝对血量，避免除零。
        /// </summary>
        private static float HealthPercent(TargetInfo candidate)
        {
            float max = candidate.MaxHealth;
            if (max <= 0f)
            {
                return candidate.Health;
            }

            return candidate.Health / max;
        }
    }
}
