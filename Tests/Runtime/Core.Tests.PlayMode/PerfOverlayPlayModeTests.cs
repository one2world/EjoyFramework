//------------------------------------------------------------
// EjoyGame Framework Tests (PlayMode)
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EjoyFramework.Core;
using EjoyFramework.Core.Performance;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Tests.PlayMode
{
    /// <summary>WS4-M3：性能覆盖层在真实播放循环里建 UI、采集、画出字形与帧图，隐藏时不刷新。</summary>
    public sealed class PerfOverlayPlayModeTests : PlayModeTestBase
    {
        private GameObject m_Go;

        public override void TearDown()
        {
            if (m_Go != null) UnityEngine.Object.DestroyImmediate(m_Go);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator Overlay_BuildsUi_RendersGlyphs_AndCustomLines()
        {
            IPerformanceManager perf = Framework.GetModule<IPerformanceManager>();
            perf.Initialize(120);
            for (int i = 0; i < 60; i++) perf.Sample(0.016f + (i % 10) * 0.002f, 100L << 20);

            m_Go = CreateGameObject("Overlay");
            PerfOverlayComponent overlay = m_Go.AddComponent<PerfOverlayComponent>();
            yield return null;

            Assert.IsFalse(overlay.Visible, "默认不显示。");
            overlay.Visible = true;
            overlay.SetCustomLine(0, "Custom 42".AsSpan());
            overlay.RefreshNow();
            Canvas.ForceUpdateCanvases();
            yield return null;

            OverlayTextBuffer buffer = overlay.Buffer;
            StringAssert.StartsWith("FPS ", buffer.GetLine(0).ToString());
            StringAssert.StartsWith("Mem MB", buffer.GetLine(2).ToString());
            Assert.AreEqual("Custom 42", buffer.GetLine(5).ToString());

            OverlayTextGraphic text = m_Go.GetComponentInChildren<OverlayTextGraphic>(true);
            Assert.IsNotNull(text);
            Assert.IsNotNull(text.Font, "内置字体 LegacyRuntime.ttf 应可加载。");
            Assert.Greater(text.LastGlyphCount, 20, "字形应已画出。");

            FrameGraphGraphic graph = m_Go.GetComponentInChildren<FrameGraphGraphic>(true);
            Assert.IsNotNull(graph);
            Assert.AreEqual(60, graph.SampleCount);
            Assert.AreEqual(16f, graph.Samples[0], 1e-3f, "样本转换为毫秒。");

            Assert.Throws<FrameworkException>(() => overlay.SetCustomLine(4, "x".AsSpan()));
            overlay.Visible = false;
            Assert.IsFalse(text.gameObject.activeInHierarchy);
        }
    }
}
