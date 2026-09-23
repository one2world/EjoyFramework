//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Quality
{
    /// <summary>自动画质一次评估的结论。</summary>
    public enum AutoQualityDecision
    {
        None = 0,
        RenderScaleChanged = 1,
        LevelDown = 2,
        LevelUp = 3,
    }

    /// <summary>
    /// 自动画质控制器（纯逻辑，零分配，时间由调用方传入，可确定性测试）。
    ///
    /// 两级调节：先动**渲染分辨率**（连续、便宜、玩家几乎无感），分辨率到底仍超预算且持续 <see cref="LevelDownSustainSeconds"/>
    /// 才**降一档**；帧时间长期有富余（低于预算 × <see cref="UpRatio"/>）先把分辨率升回上限，再持续 <see cref="LevelUpSustainSeconds"/>
    /// 且过了升档冷却才**升一档**。
    ///
    /// 防振荡：降快升慢；升档后 <see cref="FailedUpWindowSeconds"/> 内又被迫降档视为"升档失败"，升档冷却翻倍（上限
    /// <see cref="MaxUpCooldownSeconds"/>），成功稳定后恢复。超过 <see cref="HitchIgnoreMs"/> 的单帧（加载 / 切场景卡顿）不计入。
    /// </summary>
    public sealed class AutoQualityController
    {
        private float m_RenderScale = 1f;
        private float m_Clock;
        private float m_IntervalElapsed;
        private float m_IntervalSum;
        private int m_IntervalCount;
        private float m_OverTime;
        private float m_UnderTime;
        private float m_LastLevelChange = -1e9f;
        private float m_LastUp = -1e9f;
        private float m_UpCooldown;
        private float m_LastAverageMs;

        public AutoQualityController()
        {
            m_UpCooldown = BaseUpCooldownSeconds;
        }

        // ---- 配置 ----

        /// <summary>帧预算（毫秒）。默认 16.67（60fps）。</summary>
        public float TargetFrameMs = 1000f / 60f;

        public float MinRenderScale = 0.7f;
        public float MaxRenderScale = 1f;
        public float ScaleStepDown = 0.05f;
        public float ScaleStepUp = 0.025f;

        /// <summary>评估周期（秒）：周期内帧时间取均值再决策。</summary>
        public float AdjustIntervalSeconds = 0.5f;

        /// <summary>均值 &gt; 预算 × DownRatio 判为超预算。</summary>
        public float DownRatio = 1.05f;

        /// <summary>均值 &lt; 预算 × UpRatio 判为有富余。</summary>
        public float UpRatio = 0.8f;

        /// <summary>均值 &gt; 预算 × 该值时分辨率一次降双步。</summary>
        public float SevereRatio = 1.3f;

        public float LevelDownSustainSeconds = 2f;
        public float LevelUpSustainSeconds = 8f;
        public float BaseUpCooldownSeconds = 15f;
        public float MaxUpCooldownSeconds = 120f;
        public float FailedUpWindowSeconds = 10f;

        /// <summary>超过该值的单帧（毫秒）视为加载卡顿，不计入。</summary>
        public float HitchIgnoreMs = 250f;

        // ---- 状态 ----

        public float RenderScale { get { return m_RenderScale; } }

        /// <summary>当前升档冷却（秒，含失败回退）。</summary>
        public float UpCooldownSeconds { get { return m_UpCooldown; } }

        /// <summary>最近一个评估周期的平均帧时间（毫秒）。</summary>
        public float LastAverageMs { get { return m_LastAverageMs; } }

        /// <summary>把渲染缩放设到指定值（夹在 [Min, Max]），并清空累计（档位被外部改变时调用）。</summary>
        public void ResetScale(float renderScale)
        {
            m_RenderScale = Clamp(renderScale, MinRenderScale, MaxRenderScale);
            ResetAccumulators();
        }

        public void ResetAccumulators()
        {
            m_IntervalElapsed = 0f;
            m_IntervalSum = 0f;
            m_IntervalCount = 0;
            m_OverTime = 0f;
            m_UnderTime = 0f;
        }

        /// <summary>
        /// 喂一帧。<paramref name="level"/> 为当前档位，<paramref name="minLevel"/>/<paramref name="maxLevel"/> 为允许区间。
        /// 返回 LevelDown/LevelUp 时由调用方执行换档（控制器已记录换档时间）。
        /// </summary>
        public AutoQualityDecision Feed(float frameMs, float deltaSeconds, int level, int minLevel, int maxLevel)
        {
            m_Clock += deltaSeconds;
            if (frameMs > HitchIgnoreMs || frameMs <= 0f) return AutoQualityDecision.None;

            m_IntervalSum += frameMs;
            m_IntervalCount++;
            m_IntervalElapsed += deltaSeconds;
            if (m_IntervalElapsed < AdjustIntervalSeconds) return AutoQualityDecision.None;

            float interval = m_IntervalElapsed;
            float avg = m_IntervalSum / m_IntervalCount;
            m_LastAverageMs = avg;
            m_IntervalElapsed = 0f;
            m_IntervalSum = 0f;
            m_IntervalCount = 0;

            float ratio = avg / TargetFrameMs;
            if (ratio > DownRatio) return OnOverBudget(ratio, interval, level, minLevel);
            if (ratio < UpRatio) return OnUnderBudget(interval, level, maxLevel);

            m_OverTime = 0f;
            m_UnderTime = 0f;
            return AutoQualityDecision.None;
        }

        private AutoQualityDecision OnOverBudget(float ratio, float interval, int level, int minLevel)
        {
            m_UnderTime = 0f;
            if (m_RenderScale > MinRenderScale)
            {
                float step = ratio > SevereRatio ? ScaleStepDown * 2f : ScaleStepDown;
                m_RenderScale = Clamp(m_RenderScale - step, MinRenderScale, MaxRenderScale);
                return AutoQualityDecision.RenderScaleChanged;
            }

            m_OverTime += interval;
            if (m_OverTime < LevelDownSustainSeconds || level <= minLevel) return AutoQualityDecision.None;

            m_OverTime = 0f;
            if (m_Clock - m_LastUp <= FailedUpWindowSeconds)
            {
                // 刚升上去又扛不住：升档失败，冷却翻倍
                m_UpCooldown = m_UpCooldown * 2f > MaxUpCooldownSeconds ? MaxUpCooldownSeconds : m_UpCooldown * 2f;
            }

            m_LastLevelChange = m_Clock;
            return AutoQualityDecision.LevelDown;
        }

        private AutoQualityDecision OnUnderBudget(float interval, int level, int maxLevel)
        {
            m_OverTime = 0f;
            if (m_RenderScale < MaxRenderScale)
            {
                m_RenderScale = Clamp(m_RenderScale + ScaleStepUp, MinRenderScale, MaxRenderScale);
                return AutoQualityDecision.RenderScaleChanged;
            }

            m_UnderTime += interval;
            if (m_LastUp > m_LastLevelChange - 1e-3f && m_Clock - m_LastUp > FailedUpWindowSeconds)
            {
                m_UpCooldown = BaseUpCooldownSeconds;   // 上次升档已稳定：恢复基础冷却
            }

            if (m_UnderTime < LevelUpSustainSeconds || level >= maxLevel) return AutoQualityDecision.None;
            if (m_Clock - m_LastLevelChange < m_UpCooldown) return AutoQualityDecision.None;

            m_UnderTime = 0f;
            m_LastLevelChange = m_Clock;
            m_LastUp = m_Clock;
            return AutoQualityDecision.LevelUp;
        }

        // 夹紧并吸附到端点：步进累计的浮点误差（如 0.70000005）不应让"已到底 / 已到顶"的判断多走一个周期
        private static float Clamp(float v, float min, float max)
        {
            if (v <= min + ScaleEpsilon) return min;
            if (v >= max - ScaleEpsilon) return max;
            return v;
        }

        private const float ScaleEpsilon = 1e-4f;
    }
}
