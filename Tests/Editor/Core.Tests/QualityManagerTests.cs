//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Quality;
using EjoyFramework.Core.Streaming;
using EjoyFramework.Core.Telemetry;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Tests
{
    /// <summary>WS4-M3：画质管理（分级起点 / 玩家档位 / 热上限 / 旋钮 / 应用器 / 自动画质接线 / 遥测）、流送半径缩放、覆盖层文字缓冲。</summary>
    public sealed class QualityManagerTests
    {
        private sealed class RecordingApplier : IQualityApplier
        {
            public readonly List<QualityChange> Changes = new List<QualityChange>();
            public readonly List<float> ShadowDistances = new List<float>();

            public void OnQualityChanged(IQualityManager quality, QualityChange change)
            {
                Changes.Add(change);
                ShadowDistances.Add(quality.GetKnob(QualityKnobs.ShadowDistance));
            }
        }

        private sealed class ThrowingApplier : IQualityApplier
        {
            public void OnQualityChanged(IQualityManager quality, QualityChange change) { throw new InvalidOperationException("boom"); }
        }

        private QualityManager m_Mgr;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Mgr = new QualityManager();
        }

        [TearDown]
        public void TearDown()
        {
            m_Mgr.Shutdown();
        }

        private static DeviceProfile Device(float targetScore)
        {
            // 只用内存维度调出目标评分之外的饱和设备：其余四项饱和 = .70，内存补足
            DeviceProfile p = default(DeviceProfile);
            p.IsMobile = true;
            p.CpuCores = 8;
            p.CpuFrequencyMHz = 2800;
            p.GraphicsMemoryMB = 4096;
            p.GraphicsShaderLevel = 50;
            p.SystemMemoryMB = (int)((targetScore - 0.70f) / 0.30f * 8192f);
            return p;
        }

        [Test]
        public void Classify_SetsTierAndStartsAtTierLevel_NotifiesAll()
        {
            RecordingApplier applier = new RecordingApplier();
            m_Mgr.AddApplier(applier);
            DeviceProfile p = Device(0.75f);   // 移动阈值 .70 ≤ .75 < .85 → High
            DeviceTierResult r = m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
            Assert.AreEqual(DeviceTier.High, r.Tier);
            Assert.AreEqual(3, m_Mgr.Level);
            Assert.AreEqual(3, m_Mgr.MaxLevel);
            Assert.AreEqual(1, applier.Changes.Count);
            Assert.AreEqual(QualityChange.All, applier.Changes[0]);
            Assert.AreEqual(120f, applier.ShadowDistances[0], "High 档阴影距离。");

            m_Mgr.SetTierOverride(DeviceTier.Low);
            Assert.AreEqual(DeviceTierReason.UserOverride, m_Mgr.Tier.Reason);
            Assert.AreEqual(1, m_Mgr.Level);
            m_Mgr.SetTierOverride(null);
            Assert.AreEqual(3, m_Mgr.Level);
        }

        [Test]
        public void UserLevel_BecomesCeiling_ThermalCapLowersAndRestoresWhenStatic()
        {
            m_Mgr.AutoAdjust = false;
            DeviceProfile p = Device(0.9f);    // Ultra
            m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
            Assert.AreEqual(4, m_Mgr.Level);

            m_Mgr.SetUserLevel(2);
            Assert.AreEqual(2, m_Mgr.Level);
            Assert.AreEqual(2, m_Mgr.MaxLevel);

            m_Mgr.SetThermalCap(1);
            Assert.AreEqual(1, m_Mgr.Level);
            m_Mgr.SetThermalCap(-1);
            Assert.AreEqual(2, m_Mgr.Level, "静态画质：解除热上限回到玩家档位。");

            m_Mgr.SetUserLevel(-1);
            Assert.AreEqual(4, m_Mgr.Level, "回到自动 = 设备档位。");
            Assert.Throws<FrameworkException>(() => m_Mgr.SetUserLevel(5));
            Assert.Throws<FrameworkException>(() => m_Mgr.SetThermalCap(-2));
        }

        [Test]
        public void ThermalCap_WithAuto_OnlyLowers()
        {
            DeviceProfile p = Device(0.9f);
            m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
            m_Mgr.SetThermalCap(2);
            Assert.AreEqual(2, m_Mgr.Level);
            m_Mgr.SetThermalCap(-1);
            Assert.AreEqual(2, m_Mgr.Level, "自动画质：回升交给自动升档。");
        }

        [Test]
        public void Knobs_TablePerLevel_RenderScaleIsMinOfTableAndDynamic()
        {
            DeviceProfile p = Device(0.9f);
            m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
            Assert.AreEqual(1f, m_Mgr.RenderScale);
            m_Mgr.Controller.ResetScale(0.8f);
            Assert.AreEqual(0.8f, m_Mgr.GetKnob(QualityKnobs.RenderScale), 1e-5f, "动态分辨率更低时取动态值。");
            m_Mgr.SetUserLevel(0);                 // 表值 0.75 < 动态（重置为满）
            Assert.AreEqual(0.75f, m_Mgr.RenderScale, 1e-5f);

            m_Mgr.DefineKnob(QualityKnobs.Custom + 1, "Grass", new[] { 0f, 0.25f, 0.5f, 0.75f, 1f });
            Assert.AreEqual(0f, m_Mgr.GetKnob(QualityKnobs.Custom + 1));
            Assert.AreEqual(0.75f, m_Mgr.GetKnobAt(QualityKnobs.Custom + 1, 3));
            Assert.Throws<FrameworkException>(() => m_Mgr.DefineKnob(QualityKnobs.Custom + 2, "Bad", new[] { 1f, 2f }));
            Assert.Throws<FrameworkException>(() => m_Mgr.GetKnob(QualityKnobs.Custom + 99));
            Assert.Throws<FrameworkException>(() => m_Mgr.GetKnobAt(QualityKnobs.Custom + 1, 5));
        }

        [Test]
        public void TargetFrameRateKnob_DrivesAutoBudget()
        {
            DeviceProfile p = Device(0.9f);
            m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
            Assert.AreEqual(1000f / 60f, m_Mgr.Controller.TargetFrameMs, 1e-3f);
            m_Mgr.SetUserLevel(0);
            Assert.AreEqual(1000f / 30f, m_Mgr.Controller.TargetFrameMs, 1e-3f);
        }

        [Test]
        public void ReportFrame_DrivesDynamicResolutionThenLevelDown_WithAppliers()
        {
            RecordingApplier applier = new RecordingApplier();
            m_Mgr.AddApplier(applier);
            m_Mgr.AddApplier(new ThrowingApplier());   // 某个应用器抛异常不影响其他
            DeviceProfile p = Device(0.9f);
            m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
            applier.Changes.Clear();

            for (int i = 0; i < 32 * 3; i++) m_Mgr.ReportFrame(20f, 0.03125f);   // 预算 16.7，超 20%
            Assert.AreEqual(4, m_Mgr.Level);
            Assert.Less(m_Mgr.RenderScale, 1f);
            Assert.IsTrue(applier.Changes.TrueForAll(c => c == QualityChange.RenderScale));

            for (int i = 0; i < 32 * 3; i++) m_Mgr.ReportFrame(20f, 0.03125f);
            Assert.AreEqual(3, m_Mgr.Level, "分辨率到底且持续超预算后降档。");
            Assert.AreEqual(QualityChange.Level, applier.Changes[applier.Changes.Count - 1]);
            Assert.AreEqual(1, m_Mgr.LevelChangeCount - 1, "分级一次 + 自动降档一次。");
        }

        [Test]
        public void SuspendAuto_IgnoresFrames_DisabledAutoIgnoresFrames()
        {
            DeviceProfile p = Device(0.9f);
            m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
            m_Mgr.SuspendAuto();
            for (int i = 0; i < 32 * 10; i++) m_Mgr.ReportFrame(40f, 0.03125f);
            Assert.AreEqual(1f, m_Mgr.RenderScale);
            m_Mgr.ResumeAuto();
            Assert.Throws<FrameworkException>(() => m_Mgr.ResumeAuto());

            m_Mgr.AutoAdjust = false;
            for (int i = 0; i < 32 * 10; i++) m_Mgr.ReportFrame(40f, 0.03125f);
            Assert.AreEqual(4, m_Mgr.Level);
            Assert.AreEqual(1f, m_Mgr.RenderScale);
        }

        [Test]
        public void DynamicResolutionOff_GoesStraightToLevelChanges()
        {
            DeviceProfile p = Device(0.9f);
            m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
            m_Mgr.DynamicResolution = false;
            for (int i = 0; i < 32 * 2 + 1; i++) m_Mgr.ReportFrame(20f, 0.03125f);
            Assert.AreEqual(3, m_Mgr.Level, "无分辨率阶段，持续 2s 即降档。");
            Assert.AreEqual(1f, m_Mgr.RenderScale);
            m_Mgr.DynamicResolution = true;
            Assert.AreEqual(0.7f, m_Mgr.Controller.MinRenderScale, 1e-5f, "重新开启恢复原下限。");
        }

        [Test]
        public void Telemetry_RecordsTierAndLevelChanges()
        {
            TelemetryManager telemetry = new TelemetryManager();
            try
            {
                telemetry.BatchSize = 1000;
                telemetry.StartSession("s", "d", "v");
                DeviceProfile p = Device(0.9f);
                m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());   // 遥测未接入：不记
                m_Mgr.SetTelemetry(telemetry);                            // 接入时补记分级
                Assert.AreEqual(1, telemetry.PendingRecordCount);
                m_Mgr.SetUserLevel(1);                                    // 档位变化
                Assert.AreEqual(2, telemetry.PendingRecordCount);
            }
            finally
            {
                telemetry.Shutdown();
            }
        }

        [Test]
        public void StreamingApplier_ScalesRadius_AndUnloadsBeyondNewRadius()
        {
            WorldStreamingManager streaming = new WorldStreamingManager();
            try
            {
                AutoHandler handler = new AutoHandler(streaming);
                streaming.SetHandler(handler);
                streaming.CellSize = 10f;
                streaming.ConfigureLayer(0, new StreamingLayerSettings { LoadRadius = 40f, UnloadRadius = 50f });
                streaming.MaxLoadStartsPerFrame = 1000;
                streaming.MaxLoadsInFlight = 1000;
                streaming.MaxUnloadsPerFrame = 1000;
                for (int cx = -8; cx <= 8; cx++)
                    for (int cz = -8; cz <= 8; cz++)
                        streaming.RegisterCell(0, cx, cz, 0);
                streaming.SetObserver(1, 5f, 5f);
                streaming.Update(0.016f, 0.016f);
                int full = streaming.LoadedCellCount;

                m_Mgr.AddApplier(new StreamingQualityApplier(streaming));
                DeviceProfile p = Device(0.9f);
                m_Mgr.ClassifyDevice(in p, new DeviceTierClassifier());
                Assert.AreEqual(1.2f, streaming.RadiusScale, 1e-5f, "Ultra 档流送半径 ×1.2。");
                m_Mgr.SetUserLevel(0);
                Assert.AreEqual(0.6f, streaming.RadiusScale, 1e-5f);
                streaming.Update(0.016f, 0.016f);
                Assert.Less(streaming.LoadedCellCount, full, "半径缩小后超出的单元被卸载。");
                Assert.Throws<FrameworkException>(() => streaming.RadiusScale = 0f);
            }
            finally
            {
                streaming.Shutdown();
            }
        }

        private sealed class AutoHandler : IWorldStreamingHandler
        {
            private readonly WorldStreamingManager m_Mgr;
            public AutoHandler(WorldStreamingManager mgr) { m_Mgr = mgr; }
            public void BeginLoad(int cellId, int layer, int cx, int cz, int contentKey, int lod) { m_Mgr.NotifyLoaded(cellId, true); }
            public void CancelLoad(int cellId) { }
            public void BeginUnload(int cellId, int layer, int cx, int cz, int contentKey) { m_Mgr.NotifyUnloaded(cellId); }
            public void OnLodChanged(int cellId, int fromLod, int toLod) { }
        }

        // ---------------- 覆盖层文字缓冲 ----------------

        [Test]
        public void OverlayBuffer_VersionOnlyOnVisibleChange_TruncatesAndBounds()
        {
            OverlayTextBuffer b = new OverlayTextBuffer(3, 8);
            Color32 white = new Color32(255, 255, 255, 255);
            int v0 = b.Version;
            b.SetLine(0, "FPS 60".AsSpan(), white);
            Assert.AreEqual(v0 + 1, b.Version);
            b.SetLine(0, "FPS 60".AsSpan(), white);
            Assert.AreEqual(v0 + 1, b.Version, "内容与颜色未变不递增版本。");
            b.SetLine(0, "FPS 60".AsSpan(), new Color32(255, 0, 0, 255));
            Assert.AreEqual(v0 + 2, b.Version, "仅颜色变化也要重建。");
            b.SetLine(1, "0123456789".AsSpan(), white);
            Assert.AreEqual("01234567", b.GetLine(1).ToString(), "超出列数截断。");
            b.ClearLine(2);
            Assert.AreEqual(v0 + 3, b.Version, "空行清空不改版本。");
            b.Clear();
            Assert.AreEqual(0, b.GetLength(0));
            Assert.Throws<FrameworkException>(() => b.SetLine(3, "x".AsSpan(), white));
            Assert.Throws<FrameworkException>(() => new OverlayTextBuffer(0, 1));
        }

        [Test]
        public void OverlayBuffer_SetLine_DoesNotAllocate()
        {
            OverlayTextBuffer b = new OverlayTextBuffer(2, 64);
            Color32 white = new Color32(255, 255, 255, 255);
            NUnit.Framework.TestDelegate body = () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    using (TempText t = TempText.Rent(64))
                    {
                        t.Append("FPS ").Append(59.5f + (i % 3), "F1").Append(" | ms ").Append(i);
                        b.SetLine(0, t.AsSpan(), white);
                    }
                }
            };
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }
    }
}
