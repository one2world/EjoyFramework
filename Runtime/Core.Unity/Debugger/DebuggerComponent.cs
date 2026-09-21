//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EjoyFramework.Core.Coroutines;
using EjoyFramework.Core.Debugger;
using EjoyFramework.Core.Entity;
using EjoyFramework.Core.Network;
using EjoyFramework.Core.ObjectPool;
using EjoyFramework.Core.Resource;
using EjoyFramework.Core.Sound;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 调试器组件。
    /// F1 切换显示；左侧标签栏，右侧内容区。内置 8 个窗口；业务可通过 IDebuggerManager.RegisterDebuggerWindow 增加自定义窗口。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Debugger")]
    public sealed class DebuggerComponent : GameFrameworkComponent
    {
        [SerializeField] private bool m_ActiveWindow = false;
        [SerializeField] private KeyCode m_ToggleKey = KeyCode.F1;
        [SerializeField] private GUISkin m_Skin = null;

        [SerializeField]
        [Tooltip("调试器窗口隐藏时，每隔 N 帧才采样一次性能数据（含 GC.GetTotalMemory）。窗口打开时每帧采样。>=1。")]
        private int m_HiddenSampleEveryNFrames = 30;

        private int m_FramesSinceSample;

        // Static GUIContent / 常量文案：内容不变，避免每帧 OnGUI 重新分配字符串。
        private static readonly GUIContent s_TitleFull = new GUIContent("EJOY DEBUGGER (F1 to hide)");
        private static readonly GUIContent s_TitleIcon = new GUIContent("DBG");
        private static readonly GUIContent s_BtnOpen = new GUIContent("Open");
        private static readonly GUIContent s_BtnMinimize = new GUIContent("Minimize");
        private static readonly GUIContent s_MemHeader = new GUIContent("--- Managed memory (GC.GetTotalMemory) ---");

        private IDebuggerManager m_DebuggerManager;
        private bool m_ShowFullWindow = true;
        private Rect m_WindowRect = new Rect(10f, 10f, 760f, 480f);
        private Rect m_IconRect = new Rect(10f, 10f, 80f, 60f);
        private Vector2 m_TabScroll;
        private Vector2 m_ContentScroll;
        private readonly List<string> m_BuiltInPaths = new List<string>();
        private string m_CurrentPath;

        // Log window state (logMessageReceived may fire from any thread; use ConcurrentQueue)
        private readonly ConcurrentQueue<LogEntry> m_Logs = new ConcurrentQueue<LogEntry>();
        private const int LOG_CAPACITY = 256;

        // 复用的日志视图：仅当有新日志（m_LogVersion 变化）时才从并发队列快照一次，避免 OnGUI 每帧
        // （Layout+Repaint 多次）foreach 并发队列分配快照枚举器导致的 GC 抖动。
        private readonly List<LogEntry> m_LogView = new List<LogEntry>(LOG_CAPACITY);
        private int m_LogVersion;          // 每次日志增删自增（可能来自后台线程）
        private int m_LogViewVersion = -1; // m_LogView 当前对应的版本

        // Phase 16: 性能采样
        private readonly PerformanceCollector m_PerfCollector = new PerformanceCollector(240);
        private string m_PerfExportStatus;

        // GM 命令台窗口状态
        private string m_ConsoleInput = string.Empty;
        private readonly List<string> m_ConsoleHistory = new List<string>();
        private const int ConsoleMaxLines = 200;

        public bool ActiveWindow
        {
            get { return m_ActiveWindow; }
            set
            {
                m_ActiveWindow = value;
                if (m_DebuggerManager != null) m_DebuggerManager.ActiveWindow = value;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            m_DebuggerManager = Framework.GetModule<IDebuggerManager>();
            if (m_DebuggerManager != null) m_DebuggerManager.ActiveWindow = m_ActiveWindow;
            RegisterBuiltInWindows();
            Application.logMessageReceived += OnUnityLog;
        }

        protected override void OnDestroy()
        {
            Application.logMessageReceived -= OnUnityLog;
            base.OnDestroy();
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(m_ToggleKey)) ActiveWindow = !m_ActiveWindow;

            // Phase 16: 采样性能数据。GC.GetTotalMemory + Sample 有成本，窗口隐藏时不必每帧执行。
            //   - 窗口打开：每帧采样，折线图/统计实时。
            //   - 窗口隐藏：每 m_HiddenSampleEveryNFrames 帧采样一次，仍保留低频历史供事后导出。
            if (m_ActiveWindow)
            {
                m_PerfCollector.Sample(Time.unscaledDeltaTime, GC.GetTotalMemory(false));
                m_FramesSinceSample = 0;
            }
            else
            {
                int divisor = m_HiddenSampleEveryNFrames < 1 ? 1 : m_HiddenSampleEveryNFrames;
                if (++m_FramesSinceSample >= divisor)
                {
                    m_PerfCollector.Sample(Time.unscaledDeltaTime, GC.GetTotalMemory(false));
                    m_FramesSinceSample = 0;
                }
            }
        }

        private void OnGUI()
        {
            if (!m_ActiveWindow) return;
            if (m_Skin != null) GUI.skin = m_Skin;

            if (m_ShowFullWindow)
                m_WindowRect = GUILayout.Window(0, m_WindowRect, DrawFullWindow, s_TitleFull);
            else
                m_IconRect = GUILayout.Window(0, m_IconRect, DrawIcon, s_TitleIcon);
        }

        private void DrawIcon(int id)
        {
            GUI.DragWindow(new Rect(0f, 0f, float.MaxValue, float.MaxValue));
            if (GUILayout.Button(s_BtnOpen, GUILayout.Width(70f), GUILayout.Height(40f)))
                m_ShowFullWindow = true;
        }

        private void DrawFullWindow(int id)
        {
            GUI.DragWindow(new Rect(0f, 0f, float.MaxValue, 25f));

            GUILayout.BeginHorizontal();
            // 左：标签栏
            GUILayout.BeginVertical("box", GUILayout.Width(160f));
            m_TabScroll = GUILayout.BeginScrollView(m_TabScroll);
            foreach (var path in m_BuiltInPaths)
            {
                bool current = path == m_CurrentPath;
                if (GUILayout.Toggle(current, path) && !current) m_CurrentPath = path;
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(s_BtnMinimize)) m_ShowFullWindow = false;
            GUILayout.EndVertical();

            // 右：内容
            GUILayout.BeginVertical("box");
            m_ContentScroll = GUILayout.BeginScrollView(m_ContentScroll);
            try { DrawCurrent(); }
            catch (Exception ex) { GUILayout.Label("Window draw threw: " + ex.Message); }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawCurrent()
        {
            switch (m_CurrentPath)
            {
                case "System": DrawSystem(); break;
                case "Profiler": DrawProfiler(); break;
                case "ReferencePool": DrawReferencePool(); break;
                case "Resource": DrawResource(); break;
                case "ObjectPool": DrawObjectPool(); break;
                case "SpawnPool": DrawSpawnPool(); break;
                case "Entity": DrawEntity(); break;
                case "Sound": DrawSound(); break;
                case "Network": DrawNetwork(); break;
                case "Coroutine": DrawCoroutine(); break;
                case "Log": DrawLog(); break;
                case "Console": DrawConsole(); break;
                default:
                    // Custom windows via IDebuggerManager
                    var w = m_DebuggerManager != null ? m_DebuggerManager.GetDebuggerWindow(m_CurrentPath) : null;
                    if (w != null) { try { w.OnDraw(); } catch (Exception ex) { GUILayout.Label("Custom window threw: " + ex.Message); } }
                    else GUILayout.Label("Pick a window from the left.");
                    break;
            }
        }

        // ===== 内置窗口绘制 =====

        private void DrawSystem()
        {
            GUILayout.Label(Utility.Text.Format("Platform: {0}", Application.platform));
            GUILayout.Label(Utility.Text.Format("Unity Version: {0}", Application.unityVersion));
            GUILayout.Label(Utility.Text.Format("System Memory: {0} MB", SystemInfo.systemMemorySize));
            GUILayout.Label(Utility.Text.Format("Graphics Memory: {0} MB", SystemInfo.graphicsMemorySize));
            GUILayout.Label(Utility.Text.Format("Screen: {0} x {1} @ {2:F0} DPI", Screen.width, Screen.height, Screen.dpi));
            GUILayout.Label(Utility.Text.Format("Game Speed: {0:F2}", Time.timeScale));
            GUILayout.Label(Utility.Text.Format("Application running: {0}", Application.isPlaying));
        }

        private void DrawProfiler()
        {
            // 即时指标
            float instFps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
            GUILayout.Label(Utility.Text.Format("Instant FPS: {0:F1}", instFps));
            GUILayout.Label(Utility.Text.Format("Realtime: {0:F2}s   Frame: {1}", Time.realtimeSinceStartup, Time.frameCount));

            // 滑动窗口统计
            var s = m_PerfCollector.Snapshot;
            GUILayout.Label(string.Format("--- Sliding window ({0}/{1} samples) ---", s.SampleCount, s.Capacity));
            GUILayout.Label(Utility.Text.Format("Avg FPS: {0:F1}   Avg dt: {1:F2}ms", s.AvgFps, s.AvgDeltaTime * 1000f));
            GUILayout.Label(Utility.Text.Format("dt min/max: {0:F2}ms / {1:F2}ms", s.MinDeltaTime * 1000f, s.MaxDeltaTime * 1000f));
            GUILayout.Label(Utility.Text.Format("dt p95/p99: {0:F2}ms / {1:F2}ms", s.P95DeltaTime * 1000f, s.P99DeltaTime * 1000f));

            // Spike 计数（带颜色提示）
            var oldColor = GUI.color;
            if (s.SpikeFrameCount > 0) GUI.color = Color.yellow;
            if (s.MaxDeltaTime > 1f / 15f) GUI.color = Color.red;
            GUILayout.Label(Utility.Text.Format("Spike frames (> {0:F1}ms): {1}",
                s.SpikeThresholdSeconds * 1000f, s.SpikeFrameCount));
            GUI.color = oldColor;

            // 内存
            GUILayout.Label(s_MemHeader);
            GUILayout.Label(Utility.Text.Format("Now: {0:F1} MB    Avg: {1:F1} MB    Max: {2:F1} MB",
                GC.GetTotalMemory(false) / 1048576f,
                s.AvgManagedMemory / 1048576f,
                s.MaxManagedMemory / 1048576f));

            GUILayout.Space(8f);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset stats", GUILayout.Width(120))) m_PerfCollector.Reset();
                if (GUILayout.Button("Export CSV", GUILayout.Width(120))) ExportPerfCsv();
            }
            if (!string.IsNullOrEmpty(m_PerfExportStatus)) GUILayout.Label(m_PerfExportStatus);
        }

        private void ExportPerfCsv()
        {
            try
            {
                var buf = new float[m_PerfCollector.Capacity];
                int n = m_PerfCollector.CopyDeltaSeconds(buf);
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = System.IO.Path.Combine(Application.persistentDataPath, "perf_" + ts + ".csv");
                using (var sw = new System.IO.StreamWriter(path, /*append*/ false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine("frame,delta_seconds,fps");
                    for (int i = 0; i < n; i++)
                    {
                        float dt = buf[i];
                        float fps = dt > 0f ? 1f / dt : 0f;
                        sw.WriteLine(Utility.Text.Format("{0},{1:F6},{2:F2}", i, dt, fps));
                    }
                }
                m_PerfExportStatus = Utility.Text.Format("Exported {0} samples → {1}", n, path);
                Log.Info("Performance CSV exported: {0}", path);
            }
            catch (Exception ex)
            {
                m_PerfExportStatus = "Export failed: " + ex.Message;
                Log.Error("Performance CSV export failed: {0}", ex);
            }
        }

        private void DrawReferencePool()
        {
            var infos = ReferencePool.GetAllReferencePoolInfos();
            GUILayout.Label(Utility.Text.Format("ReferencePool kinds: {0}", infos.Length));
            int totalUnused = 0, totalUsing = 0, totalAcq = 0, totalRel = 0;
            foreach (var info in infos)
            {
                totalUnused += info.UnusedReferenceCount;
                totalUsing += info.UsingReferenceCount;
                totalAcq += info.AcquireReferenceCount;
                totalRel += info.ReleaseReferenceCount;
            }
            GUILayout.Label(Utility.Text.Format("Unused/Using: {0}/{1}", totalUnused, totalUsing));
            GUILayout.Label(Utility.Text.Format("Acquired/Released: {0}/{1}", totalAcq, totalRel));
            GUILayout.Label("Per-type:");
            foreach (var info in infos)
            {
                GUILayout.Label(Utility.Text.Format("  {0}: unused={1} using={2} acq={3} rel={4}",
                    info.Type != null ? info.Type.Name : "?", info.UnusedReferenceCount, info.UsingReferenceCount,
                    info.AcquireReferenceCount, info.ReleaseReferenceCount));
            }
        }

        private void DrawResource()
        {
            var rm = Framework.HasModule<IResourceManager>() ? Framework.GetModule<IResourceManager>() : null;
            if (rm == null) { GUILayout.Label("ResourceManager not registered."); return; }
            GUILayout.Label(Utility.Text.Format("Mode: {0}", rm.Mode));
            GUILayout.Label(Utility.Text.Format("Initialized: {0}", rm.IsInitialized));
            GUILayout.Label(Utility.Text.Format("Loaded bundles: {0}", rm.LoadedBundleCount));
            GUILayout.Label(Utility.Text.Format("Loaded assets: {0}", rm.LoadedAssetCount));
            GUILayout.Label(Utility.Text.Format("Loading tasks: {0}", rm.LoadingTaskCount));
            GUILayout.Label(Utility.Text.Format("ReadOnly: {0}", rm.ReadOnlyPath ?? "<null>"));
            GUILayout.Label(Utility.Text.Format("ReadWrite: {0}", rm.ReadWritePath ?? "<null>"));
        }

        private void DrawObjectPool()
        {
            var opm = Framework.HasModule<IObjectPoolManager>() ? Framework.GetModule<IObjectPoolManager>() : null;
            if (opm == null) { GUILayout.Label("ObjectPoolManager not registered."); return; }
            GUILayout.Label(Utility.Text.Format("Total pools: {0}", opm.Count));
            foreach (var p in opm.GetAllObjectPools())
            {
                ObjectPoolMetrics m;
                p.GetMetrics(out m);
                GUILayout.Label(Utility.Text.Format("  {0} :: count={1}/{2} inUse={3} peak={4} hit={5:P0} miss={6} released={7} expireSec={8:F1}",
                    p.FullName, m.Count, m.Capacity, m.SpawnedCount, m.PeakCount, m.HitRate, m.SpawnMissCount, m.TotalReleaseCount, m.ExpireTime));
            }
        }

        private readonly List<SpawnPoolEntry> m_SpawnPoolEntries = new List<SpawnPoolEntry>();

        private void DrawSpawnPool()
        {
            var sp = ComponentRegistry.GetComponent<SpawnPoolComponent>();
            if (sp == null) { GUILayout.Label("SpawnPoolComponent not registered."); return; }
            sp.GetAllEntries(m_SpawnPoolEntries);
            GUILayout.Label(Utility.Text.Format("Total entries: {0}", m_SpawnPoolEntries.Count));
            for (int i = 0; i < m_SpawnPoolEntries.Count; i++)
            {
                SpawnPoolEntry e = m_SpawnPoolEntries[i];
                ObjectPoolMetrics m;
                e.GetMetrics(out m);
                GUILayout.Label(Utility.Text.Format("  {0} :: {1} inUse={2} idle={3}/{4} peak={5} hit={6:P0} miss={7} instantiated={8} destroyed={9}",
                    e.Key, e.IsReady ? "ready" : (e.IsLoading ? "loading" : "no-prefab"),
                    m.SpawnedCount, m.IdleCount, e.MaxIdle == int.MaxValue ? "inf" : e.MaxIdle.ToString(), m.PeakCount,
                    m.HitRate, m.SpawnMissCount, e.TotalInstantiatedCount, m.TotalReleaseCount));
            }
        }

        private void DrawEntity()
        {
            var em = Framework.HasModule<IEntityManager>() ? Framework.GetModule<IEntityManager>() : null;
            if (em == null) { GUILayout.Label("EntityManager not registered."); return; }
            GUILayout.Label(Utility.Text.Format("Total entities: {0}", em.EntityCount));
            GUILayout.Label(Utility.Text.Format("Groups: {0}", em.EntityGroupCount));
            foreach (var g in em.GetAllEntityGroups())
            {
                GUILayout.Label(Utility.Text.Format("  group '{0}' count={1}", g.Name, g.EntityCount));
            }
        }

        private void DrawSound()
        {
            var sm = Framework.HasModule<ISoundManager>() ? Framework.GetModule<ISoundManager>() : null;
            if (sm == null) { GUILayout.Label("SoundManager not registered."); return; }
            GUILayout.Label(Utility.Text.Format("Groups: {0}", sm.SoundGroupCount));
            foreach (var g in sm.GetAllSoundGroups())
            {
                GUILayout.Label(Utility.Text.Format("  group '{0}' mute={1} vol={2:F2}", g.Name, g.Mute, g.Volume));
            }
        }

        private void DrawNetwork()
        {
            var nm = Framework.HasModule<INetworkManager>() ? Framework.GetModule<INetworkManager>() : null;
            if (nm == null) { GUILayout.Label("NetworkManager not registered."); return; }
            GUILayout.Label(Utility.Text.Format("Channels: {0}", nm.NetworkChannelCount));
            foreach (var c in nm.GetAllNetworkChannels())
            {
                GUILayout.Label(Utility.Text.Format("  channel '{0}' connected={1} sent={2} recv={3} hb={4:F1}/{5:F1}",
                    c.Name, c.Connected, c.SendPacketCount, c.ReceivePacketCount, c.HeartBeatInterval, c.HeartBeatTimeout));
            }
        }

        private void DrawCoroutine()
        {
            var cm = Framework.HasModule<ICoroutineManager>() ? Framework.GetModule<ICoroutineManager>() : null;
            if (cm == null) { GUILayout.Label("CoroutineManager not registered."); return; }
            GUILayout.Label(Utility.Text.Format("Running: {0}", cm.RunningCount));
            foreach (var s in cm.GetAllRunning())
            {
                GUILayout.Label(Utility.Text.Format("  #{0} tag='{1}' owner={2} elapsed={3:F2}s",
                    s.Id, s.Tag, s.OwnerName ?? "<null>", s.ElapsedSeconds));
            }
        }

        private void DrawLog()
        {
            GUILayout.Label(Utility.Text.Format("Captured logs (last {0}):", m_Logs.Count));
            if (GUILayout.Button("Clear"))
            {
                while (m_Logs.TryDequeue(out _)) { }
                System.Threading.Interlocked.Increment(ref m_LogVersion);
            }

            // 仅当日志发生变化时才从并发队列快照一次到复用列表；OnGUI 每帧多次调用之间零分配复用。
            int version = System.Threading.Volatile.Read(ref m_LogVersion);
            if (version != m_LogViewVersion)
            {
                m_LogView.Clear();
                foreach (var e in m_Logs) m_LogView.Add(e);
                m_LogViewVersion = version;
            }

            for (int i = 0; i < m_LogView.Count; i++)
            {
                LogEntry e = m_LogView[i];
                Color old = GUI.color;
                GUI.color = e.Type == LogType.Error || e.Type == LogType.Exception ? Color.red
                          : e.Type == LogType.Warning ? Color.yellow : Color.white;
                GUILayout.Label(Utility.Text.Format("[{0}] {1}", e.Type, e.Message));
                GUI.color = old;
            }
        }

        private void OnUnityLog(string msg, string stack, LogType type)
        {
            m_Logs.Enqueue(new LogEntry { Type = type, Message = msg });
            while (m_Logs.Count > LOG_CAPACITY && m_Logs.TryDequeue(out _)) { }
            System.Threading.Interlocked.Increment(ref m_LogVersion);
        }

        // GM 命令台窗口：输入命令 → IDebuggerManager.ExecuteCommand → 追加输出历史。
        private void DrawConsole()
        {
            GUILayout.Label("GM 命令台 — 输入命令后回车或点 Run 执行（输入 help 查看命令）");

            // 全限定 UnityEngine.Event：EjoyFramework.Core.Event 命名空间会遮蔽裸 Event。
            bool enterPressed = UnityEngine.Event.current.type == EventType.KeyDown &&
                                (UnityEngine.Event.current.keyCode == KeyCode.Return || UnityEngine.Event.current.keyCode == KeyCode.KeypadEnter);

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("GmConsoleInput");
            m_ConsoleInput = GUILayout.TextField(m_ConsoleInput ?? string.Empty, GUILayout.MinWidth(280f));
            bool runPressed = GUILayout.Button("Run", GUILayout.Width(60f));
            if (GUILayout.Button("Clear", GUILayout.Width(60f))) m_ConsoleHistory.Clear();
            GUILayout.EndHorizontal();

            bool focused = GUI.GetNameOfFocusedControl() == "GmConsoleInput";
            if ((runPressed || (enterPressed && focused)) && !string.IsNullOrWhiteSpace(m_ConsoleInput))
            {
                string line = m_ConsoleInput.Trim();
                m_ConsoleInput = string.Empty;
                AppendConsole("> " + line);
                string output = m_DebuggerManager != null
                    ? m_DebuggerManager.ExecuteCommand(line)
                    : "(debugger manager unavailable)";
                if (!string.IsNullOrEmpty(output)) AppendConsole(output);
                if (enterPressed) UnityEngine.Event.current.Use();
                GUI.FocusControl("GmConsoleInput");
            }

            GUILayout.Space(4f);
            for (int i = 0; i < m_ConsoleHistory.Count; i++) GUILayout.Label(m_ConsoleHistory[i]);
        }

        private void AppendConsole(string line)
        {
            m_ConsoleHistory.Add(line);
            while (m_ConsoleHistory.Count > ConsoleMaxLines) m_ConsoleHistory.RemoveAt(0);
        }

        private void RegisterBuiltInWindows()
        {
            m_BuiltInPaths.Add("System");
            m_BuiltInPaths.Add("Profiler");
            m_BuiltInPaths.Add("ReferencePool");
            m_BuiltInPaths.Add("Resource");
            m_BuiltInPaths.Add("ObjectPool");
            m_BuiltInPaths.Add("SpawnPool");
            m_BuiltInPaths.Add("Entity");
            m_BuiltInPaths.Add("Sound");
            m_BuiltInPaths.Add("Network");
            m_BuiltInPaths.Add("Coroutine");
            m_BuiltInPaths.Add("Log");
            m_BuiltInPaths.Add("Console");
            m_CurrentPath = "System";
        }

        private struct LogEntry
        {
            public LogType Type;
            public string Message;
        }
    }
}
