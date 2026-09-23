//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Diagnostics;
using EjoyFramework.Core.Performance;
using EjoyFramework.Core.Quality;
using EjoyFramework.Core.Streaming;
using EjoyFramework.Core.Telemetry;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
#if EJOY_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 运行时性能覆盖层（真机可用）：帧率 / 帧时间分位 / spike、画质档位与渲染缩放、内存、流送、崩溃与遥测状态，
    /// 外加帧时间柱状图与业务自定义行。自带 UGUI 画布（不依赖任何美术资源，字体用引擎内置 LegacyRuntime.ttf）。
    ///
    /// 稳态零分配：数值用 <see cref="TempText"/> 拼进 <see cref="OverlayTextBuffer"/>，内容不变不重建网格；
    /// 刷新间隔 <see cref="m_RefreshInterval"/> 默认 0.25s。隐藏时不采集、不重建。
    /// 开关：<see cref="Visible"/> / <see cref="Toggle"/>；装了 Input System 时键盘 <see cref="m_ToggleKey"/> 或三指同时按下切换。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Perf Overlay")]
    public sealed class PerfOverlayComponent : GameFrameworkComponent
    {
        private const int BuiltInLines = 5;
        private static readonly string[] s_TierNames = { "Minimum", "Low", "Medium", "High", "Ultra" };
        private static readonly string[] s_ReasonNames = { "score", "override", "capped", "user" };
        private static readonly Color32 s_Normal = new Color32(235, 235, 235, 255);
        private static readonly Color32 s_Warn = new Color32(250, 205, 70, 255);
        private static readonly Color32 s_Bad = new Color32(250, 90, 80, 255);

        [SerializeField] private bool m_VisibleOnStart = false;
        [SerializeField] private float m_RefreshInterval = 0.25f;
        [SerializeField] private int m_FontSize = 16;
        [SerializeField] private float m_Width = 820f;
        [SerializeField] private bool m_ShowGraph = true;
        [SerializeField] private float m_GraphHeight = 90f;

        [Tooltip("业务自定义行数（SetCustomLine 的槽位）。")]
        [SerializeField] private int m_CustomLines = 4;

#if EJOY_INPUTSYSTEM
        [SerializeField] private Key m_ToggleKey = Key.F3;
        [SerializeField] private bool m_ToggleWithThreeFingers = true;
        private bool m_ThreeFingersDown;
#endif

        private GameObject m_Root;
        private OverlayTextGraphic m_Text;
        private FrameGraphGraphic m_Graph;
        private RectTransform m_PanelRect;
        private OverlayTextBuffer m_Buffer;
        private float[] m_Deltas;
        private float m_SinceRefresh;
        private bool m_Visible;

        protected override void Awake()
        {
            base.Awake();
            m_Buffer = new OverlayTextBuffer(BuiltInLines + Mathf.Max(0, m_CustomLines), 110);
            BuildUi();
            Visible = m_VisibleOnStart;
        }

        /// <summary>显示 / 隐藏。</summary>
        public bool Visible
        {
            get { return m_Visible; }
            set
            {
                m_Visible = value;
                if (m_Root != null) m_Root.SetActive(value);
                if (value) m_SinceRefresh = m_RefreshInterval;   // 显示后立即刷新一次
            }
        }

        public void Toggle()
        {
            Visible = !m_Visible;
        }

        /// <summary>设置业务自定义行（槽位 0..CustomLines-1；仅 ASCII 可见）。</summary>
        public void SetCustomLine(int slot, ReadOnlySpan<char> text)
        {
            SetCustomLine(slot, text, s_Normal);
        }

        public void SetCustomLine(int slot, ReadOnlySpan<char> text, Color32 color)
        {
            if (slot < 0 || slot >= m_Buffer.Rows - BuiltInLines) throw new FrameworkException("PerfOverlay：自定义行槽位越界 " + slot + "。");
            m_Buffer.SetLine(BuiltInLines + slot, text, color);
        }

        public void ClearCustomLine(int slot)
        {
            if (slot < 0 || slot >= m_Buffer.Rows - BuiltInLines) throw new FrameworkException("PerfOverlay：自定义行槽位越界 " + slot + "。");
            m_Buffer.ClearLine(BuiltInLines + slot);
        }

        /// <summary>文字缓冲（测试 / 扩展用）。</summary>
        public OverlayTextBuffer Buffer { get { return m_Buffer; } }

        private void Update()
        {
            PollToggle();
            if (!m_Visible) return;
            m_SinceRefresh += Time.unscaledDeltaTime;
            if (m_SinceRefresh < m_RefreshInterval) return;
            m_SinceRefresh = 0f;
            RefreshNow();
        }

        /// <summary>立即采集并刷新一次（测试 / 手动调用）。</summary>
        public void RefreshNow()
        {
            WritePerformanceLine();
            WriteQualityLine();
            WriteMemoryLine();
            WriteStreamingLine();
            WriteLiveOpsLine();
            if (m_Text != null) m_Text.Refresh();
            if (m_ShowGraph && m_Graph != null) RefreshGraph();
        }

        // ================================================================
        //  行
        // ================================================================

        private void WritePerformanceLine()
        {
            if (!Framework.HasModule<IPerformanceManager>())
            {
                m_Buffer.ClearLine(0);
                return;
            }

            IPerformanceManager perf = Framework.GetModule<IPerformanceManager>();
            PerformanceMetrics m = perf.Snapshot;
            float budgetMs = CurrentBudgetMs();
            float p95 = m.P95DeltaTime * 1000f;
            Color32 color = p95 <= budgetMs * 1.05f ? s_Normal : p95 <= budgetMs * 2f ? s_Warn : s_Bad;
            using (TempText t = TempText.Rent(128))
            {
                t.Append("FPS ").Append(m.AvgFps, "F1")
                 .Append(" | avg ").Append(m.AvgDeltaTime * 1000f, "F1")
                 .Append(" p95 ").Append(p95, "F1")
                 .Append(" p99 ").Append(m.P99DeltaTime * 1000f, "F1")
                 .Append(" max ").Append(m.MaxDeltaTime * 1000f, "F1")
                 .Append(" ms | spikes ").Append(perf.SpikeFrameCount);
                m_Buffer.SetLine(0, t.AsSpan(), color);
            }
        }

        private void WriteQualityLine()
        {
            if (!Framework.HasModule<IQualityManager>())
            {
                m_Buffer.ClearLine(1);
                return;
            }

            IQualityManager q = Framework.GetModule<IQualityManager>();
            DeviceTierResult tier = q.Tier;
            using (TempText t = TempText.Rent(128))
            {
                t.Append("Quality L").Append(q.Level).Append('/').Append(q.MaxLevel)
                 .Append(" | tier ").Append(s_TierNames[(int)tier.Tier]).Append(" (").Append(s_ReasonNames[(int)tier.Reason])
                 .Append(' ').Append(tier.Score, "F2").Append(')')
                 .Append(" | scale ").Append(q.RenderScale, "F2")
                 .Append(" | work ").Append(q.Controller.LastAverageMs, "F1").Append(" ms")
                 .Append(q.AutoAdjust ? " | auto" : " | fixed");
                m_Buffer.SetLine(1, t.AsSpan(), s_Normal);
            }
        }

        private void WriteMemoryLine()
        {
            using (TempText t = TempText.Rent(128))
            {
                t.Append("Mem MB | mono ").Append(Profiler.GetMonoUsedSizeLong() >> 20)
                 .Append(" | alloc ").Append(Profiler.GetTotalAllocatedMemoryLong() >> 20)
                 .Append(" | reserved ").Append(Profiler.GetTotalReservedMemoryLong() >> 20)
                 .Append(" | gfx ").Append(Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20);
                ResourceComponent rc = ComponentRegistry.GetComponent<ResourceComponent>();
                if (rc != null) t.Append(" | bundles ").Append(rc.ResidentBytes >> 20);
                m_Buffer.SetLine(2, t.AsSpan(), s_Normal);
            }
        }

        private void WriteStreamingLine()
        {
            if (!Framework.HasModule<IWorldStreamingManager>())
            {
                m_Buffer.ClearLine(3);
                return;
            }

            IWorldStreamingManager s = Framework.GetModule<IWorldStreamingManager>();
            using (TempText t = TempText.Rent(128))
            {
                t.Append("Stream cells ").Append(s.LoadedCellCount).Append(" loaded, ")
                 .Append(s.LoadingCellCount).Append(" loading, ").Append(s.QueuedLoadCount).Append(" queued")
                 .Append(" | radius x").Append(s.RadiusScale, "F2")
                 .Append(" | fail ").Append(s.TotalLoadFailures);
                m_Buffer.SetLine(3, t.AsSpan(), s.TotalLoadFailures > 0 ? s_Warn : s_Normal);
            }
        }

        private void WriteLiveOpsLine()
        {
            bool crash = Framework.HasModule<ICrashManager>();
            bool telemetry = Framework.HasModule<ITelemetryManager>();
            if (!crash && !telemetry)
            {
                m_Buffer.ClearLine(4);
                return;
            }

            Color32 color = s_Normal;
            using (TempText t = TempText.Rent(128))
            {
                if (crash)
                {
                    ICrashManager c = Framework.GetModule<ICrashManager>();
                    t.Append("Crash exc ").Append(c.TotalExceptions).Append(" hang ").Append(c.HangCount)
                     .Append(" pending ").Append(c.PendingUploadCount);
                    if (c.TotalExceptions > 0 || c.HangCount > 0) color = s_Warn;
                    if (c.IsHanging) color = s_Bad;
                }

                if (telemetry)
                {
                    ITelemetryManager tm = Framework.GetModule<ITelemetryManager>();
                    if (crash) t.Append(" | ");
                    t.Append("Telemetry ").Append(tm.IsSampling ? "on" : "off")
                     .Append(" sent ").Append(tm.TotalBatchesSent).Append(" queued ").Append(tm.QueuedBatchCount);
                }

                m_Buffer.SetLine(4, t.AsSpan(), color);
            }
        }

        private void RefreshGraph()
        {
            if (!Framework.HasModule<IPerformanceManager>()) return;
            IPerformanceManager perf = Framework.GetModule<IPerformanceManager>();
            float[] samples = m_Graph.Samples;
            int n = perf.CopyDeltaSeconds(samples);
            for (int i = 0; i < n; i++) samples[i] *= 1000f;
            m_Graph.SampleCount = n;
            m_Graph.BudgetMs = CurrentBudgetMs();
            m_Graph.MaxMs = m_Graph.BudgetMs * 3f;
            m_Graph.MarkDirty();
        }

        private static float CurrentBudgetMs()
        {
            if (Framework.HasModule<IQualityManager>()) return Framework.GetModule<IQualityManager>().Controller.TargetFrameMs;
            int target = Application.targetFrameRate;
            return target > 0 ? 1000f / target : 1000f / 60f;
        }

        // ================================================================
        //  开关
        // ================================================================

        private void PollToggle()
        {
#if EJOY_INPUTSYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && m_ToggleKey != Key.None && keyboard[m_ToggleKey].wasPressedThisFrame) Toggle();

            if (m_ToggleWithThreeFingers)
            {
                Touchscreen touch = Touchscreen.current;
                int pressed = 0;
                if (touch != null)
                {
                    var touches = touch.touches;
                    for (int i = 0; i < touches.Count; i++)
                    {
                        if (touches[i].press.isPressed) pressed++;
                    }
                }

                bool three = pressed >= 3;
                if (three && !m_ThreeFingersDown) Toggle();
                m_ThreeFingersDown = three;
            }
#endif
        }

        // ================================================================
        //  UI
        // ================================================================

        private void BuildUi()
        {
            m_Root = new GameObject("EjoyPerfOverlay", typeof(RectTransform));
            m_Root.transform.SetParent(transform, false);
            Canvas canvas = m_Root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            CanvasScaler scaler = m_Root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(m_Root.transform, false);
            RawImage background = panel.AddComponent<RawImage>();
            background.color = new Color(0f, 0f, 0f, 0.6f);
            background.raycastTarget = false;
            m_PanelRect = (RectTransform)panel.transform;
            m_PanelRect.anchorMin = new Vector2(0f, 1f);
            m_PanelRect.anchorMax = new Vector2(0f, 1f);
            m_PanelRect.pivot = new Vector2(0f, 1f);
            m_PanelRect.anchoredPosition = new Vector2(8f, -8f);

            GameObject textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(panel.transform, false);
            m_Text = textGo.AddComponent<OverlayTextGraphic>();
            m_Text.raycastTarget = false;
            m_Text.FontSize = m_FontSize;
            m_Text.Font = LoadBuiltinFont();
            m_Text.Buffer = m_Buffer;
            Stretch((RectTransform)textGo.transform);

            float textHeight = m_Text.PreferredHeight;
            float graphHeight = m_ShowGraph ? m_GraphHeight : 0f;
            m_PanelRect.sizeDelta = new Vector2(m_Width, textHeight + graphHeight);

            if (m_ShowGraph)
            {
                GameObject graphGo = new GameObject("FrameGraph", typeof(RectTransform));
                graphGo.transform.SetParent(panel.transform, false);
                m_Graph = graphGo.AddComponent<FrameGraphGraphic>();
                m_Graph.raycastTarget = false;
                int capacity = Framework.HasModule<IPerformanceManager>() ? Framework.GetModule<IPerformanceManager>().SampleCapacity : 240;
                m_Graph.SetCapacity(capacity > 0 ? capacity : 240);
                RectTransform gr = (RectTransform)graphGo.transform;
                gr.anchorMin = new Vector2(0f, 0f);
                gr.anchorMax = new Vector2(1f, 0f);
                gr.pivot = new Vector2(0.5f, 0f);
                gr.offsetMin = new Vector2(6f, 6f);
                gr.offsetMax = new Vector2(-6f, graphHeight - 6f);
                RectTransform tr = (RectTransform)textGo.transform;
                tr.offsetMin = new Vector2(0f, graphHeight);
            }
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Font LoadBuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) Log.Warning("PerfOverlay：找不到内置字体 LegacyRuntime.ttf，覆盖层文字不可见。");
            return font;
        }

        protected override void OnDestroy()
        {
            try
            {
                if (m_Root != null)
                {
                    if (Application.isPlaying) Destroy(m_Root);
                    else DestroyImmediate(m_Root);
                }
            }
            finally
            {
                base.OnDestroy();
            }
        }
    }
}
