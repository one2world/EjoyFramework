//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Telemetry;

namespace EjoyFramework.Core.Quality
{
    /// <summary><see cref="IQualityManager"/> 实现。全部主线程；<see cref="ReportFrame"/> 稳态零分配。</summary>
    internal sealed class QualityManager : FrameworkModule, IQualityManager
    {
        private sealed class Knob
        {
            public string Name;
            public float[] Values;
        }

        private readonly Dictionary<int, Knob> m_Knobs = new Dictionary<int, Knob>();
        private readonly List<IQualityApplier> m_Appliers = new List<IQualityApplier>();
        private readonly AutoQualityController m_Controller = new AutoQualityController();

        private DeviceTierResult m_Classified;
        private DeviceTier? m_TierOverride;
        private int m_Level = (int)DeviceTier.High;
        private int m_UserLevel = -1;
        private int m_ThermalCap = -1;
        private bool m_AutoAdjust = true;
        private bool m_DynamicResolution = true;
        private float m_SavedMinScale;
        private int m_Suspend;
        private int m_LevelChanges;
        private ITelemetryManager m_Telemetry;

        public QualityManager()
        {
            m_Classified.Tier = DeviceTier.High;
            m_Classified.Reason = DeviceTierReason.Score;
            QualityKnobs.DefineDefaults(this);
            SyncFrameBudget();
        }

        public override int Priority { get { return 0; } }

        // ================================================================
        //  分级
        // ================================================================

        public DeviceTierResult ClassifyDevice(in DeviceProfile profile, DeviceTierClassifier classifier)
        {
            if (classifier == null) throw new FrameworkException("QualityManager.ClassifyDevice：classifier 为空。");
            m_Classified = classifier.Classify(in profile);
            RecordTierTelemetry();
            RebaseLevel(QualityChangeCause.Tier, QualityChange.All);
            return Tier;
        }

        public void SetTierOverride(DeviceTier? tier)
        {
            m_TierOverride = tier;
            RecordTierTelemetry();
            RebaseLevel(QualityChangeCause.Tier, QualityChange.All);
        }

        public DeviceTierResult Tier
        {
            get
            {
                if (!m_TierOverride.HasValue) return m_Classified;
                DeviceTierResult r = m_Classified;
                r.Tier = m_TierOverride.Value;
                r.Reason = DeviceTierReason.UserOverride;
                r.RuleLine = 0;
                return r;
            }
        }

        // ================================================================
        //  档位
        // ================================================================

        public int Level { get { return m_Level; } }

        public int UserLevel { get { return m_UserLevel; } }

        public void SetUserLevel(int level)
        {
            if (level < -1 || level >= QualityKnobs.LevelCount) throw new FrameworkException("QualityManager.SetUserLevel：档位须为 -1..4。");
            m_UserLevel = level;
            RebaseLevel(QualityChangeCause.User, QualityChange.Level);
        }

        public void SetThermalCap(int maxLevel)
        {
            if (maxLevel < -1 || maxLevel >= QualityKnobs.LevelCount) throw new FrameworkException("QualityManager.SetThermalCap：上限须为 -1..4。");
            m_ThermalCap = maxLevel;
            if (!m_AutoAdjust)
            {
                SetLevel(MaxLevel, QualityChangeCause.Thermal, QualityChange.Level);   // 静态画质：始终等于上限
            }
            else if (m_Level > MaxLevel)
            {
                SetLevel(MaxLevel, QualityChangeCause.Thermal, QualityChange.Level);   // 自动画质：只压不抬，回升交给自动升档
            }
        }

        public int MaxLevel
        {
            get
            {
                int ceiling = m_UserLevel >= 0 ? m_UserLevel : (int)Tier.Tier;
                return m_ThermalCap >= 0 && m_ThermalCap < ceiling ? m_ThermalCap : ceiling;
            }
        }

        // 起点变化（分级 / 玩家选档）：档位直接回到上限，动态分辨率回满
        private void RebaseLevel(QualityChangeCause cause, QualityChange change)
        {
            m_Controller.ResetScale(m_Controller.MaxRenderScale);
            SetLevel(MaxLevel, cause, change, true);
        }

        private void SetLevel(int level, QualityChangeCause cause, QualityChange change, bool force = false)
        {
            if (level < 0) level = 0;
            if (level > MaxLevel) level = MaxLevel;
            if (level == m_Level && !force) return;

            int old = m_Level;
            m_Level = level;
            if (old != level) m_LevelChanges++;
            m_Controller.ResetAccumulators();
            SyncFrameBudget();
            RecordLevelTelemetry(old, level, cause);
            Notify(change | QualityChange.Level);
        }

        private void SyncFrameBudget()
        {
            Knob knob;
            if (m_Knobs.TryGetValue(QualityKnobs.TargetFrameRate, out knob))
            {
                float fps = knob.Values[m_Level];
                if (fps > 0f) m_Controller.TargetFrameMs = 1000f / fps;
            }
        }

        // ================================================================
        //  自动画质
        // ================================================================

        public bool AutoAdjust
        {
            get { return m_AutoAdjust; }
            set
            {
                if (m_AutoAdjust == value) return;
                m_AutoAdjust = value;
                m_Controller.ResetAccumulators();
                if (!value)
                {
                    // 关掉自动：回到静态上限与满分辨率
                    m_Controller.ResetScale(m_Controller.MaxRenderScale);
                    SetLevel(MaxLevel, QualityChangeCause.User, QualityChange.Level | QualityChange.RenderScale, true);
                }
            }
        }

        public bool DynamicResolution
        {
            get { return m_DynamicResolution; }
            set
            {
                if (m_DynamicResolution == value) return;
                m_DynamicResolution = value;
                if (!value)
                {
                    // 关闭：把下限钉到上限，控制器直接走换档逻辑
                    m_SavedMinScale = m_Controller.MinRenderScale;
                    m_Controller.MinRenderScale = m_Controller.MaxRenderScale;
                }
                else
                {
                    m_Controller.MinRenderScale = m_SavedMinScale;
                }

                m_Controller.ResetScale(m_Controller.MaxRenderScale);
                Notify(QualityChange.RenderScale);
            }
        }

        public AutoQualityController Controller { get { return m_Controller; } }

        public void SuspendAuto()
        {
            m_Suspend++;
        }

        public void ResumeAuto()
        {
            if (m_Suspend == 0) throw new FrameworkException("QualityManager.ResumeAuto：与 SuspendAuto 不配对。");
            m_Suspend--;
            if (m_Suspend == 0) m_Controller.ResetAccumulators();   // 暂停期间的帧不参与判断
        }

        public void ReportFrame(float frameWorkMs, float deltaSeconds)
        {
            if (!m_AutoAdjust || m_Suspend > 0) return;
            AutoQualityDecision decision = m_Controller.Feed(frameWorkMs, deltaSeconds, m_Level, 0, MaxLevel);
            switch (decision)
            {
                case AutoQualityDecision.RenderScaleChanged:
                    if (m_DynamicResolution) Notify(QualityChange.RenderScale);
                    break;
                case AutoQualityDecision.LevelDown:
                    SetLevel(m_Level - 1, QualityChangeCause.AutoDown, QualityChange.Level);
                    break;
                case AutoQualityDecision.LevelUp:
                    SetLevel(m_Level + 1, QualityChangeCause.AutoUp, QualityChange.Level);
                    break;
            }
        }

        public float RenderScale
        {
            get
            {
                float table = GetKnobAt(QualityKnobs.RenderScale, m_Level);
                if (!m_DynamicResolution) return table;
                float dynamic = m_Controller.RenderScale;
                return dynamic < table ? dynamic : table;
            }
        }

        // ================================================================
        //  旋钮
        // ================================================================

        public void DefineKnob(int knobId, string name, float[] valuesPerLevel)
        {
            if (valuesPerLevel == null || valuesPerLevel.Length != QualityKnobs.LevelCount)
            {
                throw new FrameworkException("QualityManager.DefineKnob：每档一个值，长度须为 " + QualityKnobs.LevelCount + "。");
            }

            Knob knob = new Knob();
            knob.Name = string.IsNullOrEmpty(name) ? knobId.ToString() : name;
            knob.Values = (float[])valuesPerLevel.Clone();
            m_Knobs[knobId] = knob;
            if (knobId == QualityKnobs.TargetFrameRate) SyncFrameBudget();
        }

        public float GetKnob(int knobId)
        {
            return knobId == QualityKnobs.RenderScale ? RenderScale : GetKnobAt(knobId, m_Level);
        }

        public float GetKnobAt(int knobId, int level)
        {
            Knob knob;
            if (!m_Knobs.TryGetValue(knobId, out knob)) throw new FrameworkException("QualityManager：旋钮 " + knobId + " 未定义。");
            if (level < 0 || level >= QualityKnobs.LevelCount) throw new FrameworkException("QualityManager：档位越界 " + level + "。");
            return knob.Values[level];
        }

        public bool HasKnob(int knobId)
        {
            return m_Knobs.ContainsKey(knobId);
        }

        /// <summary>旋钮名（调试 / 覆盖层用）。</summary>
        internal string GetKnobName(int knobId)
        {
            Knob knob;
            return m_Knobs.TryGetValue(knobId, out knob) ? knob.Name : null;
        }

        // ================================================================
        //  应用
        // ================================================================

        public void AddApplier(IQualityApplier applier)
        {
            if (applier == null) throw new FrameworkException("QualityManager.AddApplier：applier 为空。");
            if (m_Appliers.Contains(applier)) return;
            m_Appliers.Add(applier);
        }

        public bool RemoveApplier(IQualityApplier applier)
        {
            return m_Appliers.Remove(applier);
        }

        public void ApplyAll()
        {
            Notify(QualityChange.All);
        }

        private void Notify(QualityChange change)
        {
            for (int i = 0; i < m_Appliers.Count; i++)
            {
                try
                {
                    m_Appliers[i].OnQualityChanged(this, change);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("QualityManager：应用器 {0} 抛出异常：{1}", m_Appliers[i].GetType().Name, ex);
                }
            }
        }

        // ================================================================
        //  遥测 / 状态
        // ================================================================

        public void SetTelemetry(ITelemetryManager telemetry)
        {
            m_Telemetry = telemetry;
            RecordTierTelemetry();   // 分级通常早于遥测接入：接入时补记一次当前分级
        }

        public int LevelChangeCount { get { return m_LevelChanges; } }

        private void RecordTierTelemetry()
        {
            if (m_Telemetry == null || !m_Telemetry.IsSampling) return;
            DeviceTierResult t = Tier;
            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.Tier;
            r.I0 = (int)t.Tier;
            r.I1 = (int)t.Reason;
            r.I2 = t.RuleLine;
            r.F0 = t.Score;
            m_Telemetry.Record(ref r);
        }

        private void RecordLevelTelemetry(int from, int to, QualityChangeCause cause)
        {
            if (m_Telemetry == null || !m_Telemetry.IsSampling) return;
            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.QualityChange;
            r.I0 = from;
            r.I1 = to;
            r.I2 = (int)cause;
            r.F0 = RenderScale;
            r.F1 = m_Controller.LastAverageMs;
            m_Telemetry.Record(ref r);
        }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            // 帧数据由 ReportFrame 推入（Unity 层带 FrameTimingManager 的工作耗时）
        }

        public override void Shutdown()
        {
            m_Appliers.Clear();
            m_Telemetry = null;
        }
    }
}
