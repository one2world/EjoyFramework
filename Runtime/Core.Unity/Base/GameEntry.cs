//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 框架便捷入口：以 <c>GameEntry.X</c> 形式访问所有 Framework Component。
    ///
    /// 这是 <b>Framework 层</b>提供的通用便利，并非游戏专属逻辑——
    /// 任何使用 EjoyFramework.Core 的游戏都需要 <c>GameEntry.UI.OpenUIForm(...)</c> 这种调用形式。
    /// 因此 GameEntry 放在 framework 包中（<see cref="EjoyFramework.Core.Unity"/>），
    /// 而不是 Assets/ 下的游戏代码中。
    ///
    /// 设计要点：
    ///   - <b>纯静态访问器，惰性查找</b>：每个属性 getter 调用一次 <see cref="ComponentRegistry.GetComponent{T}"/>
    ///     （Dictionary 查表，单帧热度可忽略）。<b>无需 Awake/Init 步骤</b>——业务直接调即可，
    ///     不存在"Init 早于使用"的时序坑。
    ///   - <b>可空契约（刻意设计）</b>：当对应 Component 不在任何已加载场景中时，访问器返回 <c>null</c>
    ///     （区别于 <c>Framework.GetModule&lt;T&gt;()</c> 缺失即抛 FrameworkException）。这便于"可选组件"以
    ///     <c>if (GameEntry.X != null)</c> / <c>GameEntry.X?.Foo()</c> 优雅降级；<b>必需</b>组件请在启动流程
    ///     显式校验其存在性（如 Launcher 对 UI/Resource 的存在性检查），不要依赖访问器抛异常。
    ///   - <b>无 MonoBehaviour 依赖</b>：GameEntry 不是组件，不需要挂在场景物体上。
    ///     场景 root 只需放 EjoyFramework.Core.prefab 实例（自带 BaseComponent + 所有子 Component）。
    ///   - <b>游戏自定义组件</b>放在游戏自己的静态类里（如 <c>EjoyGame.GameComponents</c>），
    ///     与本 framework GameEntry 共存而不耦合。
    ///
    /// 用法示例：
    /// <code>
    /// using EjoyFramework.Core.Unity;
    ///
    /// GameEntry.UI.OpenUIForm("Assets/UI/MainMenu.prefab", "Window");
    /// GameEntry.Resource.LoadAsset(name, callbacks);
    /// GameEntry.Event.Subscribe(eventId, handler);
    /// </code>
    ///
    /// 业务避免在热路径（每帧调用 N 次）里反复 <c>GameEntry.UI</c>；
    /// 必要时在 caller 内 cache 一次局部变量即可。
    /// </summary>
    public static class GameEntry
    {
        /// <summary>Tries to resolve an optional Unity framework component.</summary>
        public static bool TryGetComponent<T>(out T component) where T : GameFrameworkComponent
        {
            return ComponentRegistry.TryGetComponent(out component);
        }

        /// <summary>Resolves a required Unity framework component or throws a <see cref="FrameworkException"/>.</summary>
        public static T RequireComponent<T>() where T : GameFrameworkComponent
        {
            return ComponentRegistry.RequireComponent<T>();
        }

        /// <summary>基础组件（FrameRate / GameSpeed / DontDestroyOnLoad 等全局设置）。</summary>
        public static BaseComponent Base => ComponentRegistry.GetComponent<BaseComponent>();

        /// <summary>全局配置（key-value 读写）。</summary>
        public static ConfigComponent Config => ComponentRegistry.GetComponent<ConfigComponent>();

        /// <summary>数据表（CSV/TSV/JSON 表驱动）。</summary>
        public static DataTableComponent DataTable => ComponentRegistry.GetComponent<DataTableComponent>();

        /// <summary>层级数据节点（运行时配置树）。</summary>
        public static DataNodeComponent DataNode => ComponentRegistry.GetComponent<DataNodeComponent>();

        /// <summary>运行时调试器（仅 Editor 与 Development Build 可用）。</summary>
        public static DebuggerComponent Debugger => ComponentRegistry.GetComponent<DebuggerComponent>();

        /// <summary>实体管理（show/hide/attach）。</summary>
        public static EntityComponent Entity => ComponentRegistry.GetComponent<EntityComponent>();

        /// <summary>事件分发（pub/sub）。</summary>
        public static EventComponent Event => ComponentRegistry.GetComponent<EventComponent>();

        /// <summary>有限状态机 / 流程图。</summary>
        public static FsmComponent Fsm => ComponentRegistry.GetComponent<FsmComponent>();

        /// <summary>多语言。</summary>
        public static LocalizationComponent Localization => ComponentRegistry.GetComponent<LocalizationComponent>();

        /// <summary>网络通道。</summary>
        public static NetworkComponent Network => ComponentRegistry.GetComponent<NetworkComponent>();

        /// <summary>对象池。</summary>
        public static ObjectPoolComponent ObjectPool => ComponentRegistry.GetComponent<ObjectPoolComponent>();

        /// <summary>游戏流程（基于 FSM）。</summary>
        public static ProcedureComponent Procedure => ComponentRegistry.GetComponent<ProcedureComponent>();

        /// <summary>资源加载（AssetBundle / EditorSimulation）。</summary>
        public static ResourceComponent Resource => ComponentRegistry.GetComponent<ResourceComponent>();

        /// <summary>场景加载与切换。</summary>
        public static SceneComponent Scene => ComponentRegistry.GetComponent<SceneComponent>();

        /// <summary>玩家设置（PlayerPrefs 包装 + 类型化 API）。</summary>
        public static SettingComponent Setting => ComponentRegistry.GetComponent<SettingComponent>();

        /// <summary>音频（音效 / 音乐 / 语音）。</summary>
        public static SoundComponent Sound => ComponentRegistry.GetComponent<SoundComponent>();

        /// <summary>UI（窗体 / Group / MVVM 视图）。</summary>
        public static UIComponent UI => ComponentRegistry.GetComponent<UIComponent>();

        /// <summary>性能监控（采样 / 分析 / 预警 / 自适应画质）。</summary>
        public static PerformanceComponent Performance => ComponentRegistry.GetComponent<PerformanceComponent>();

        /// <summary>定时器 / 调度（延时 / 重复 / 帧定时，handle 取消，scaled/unscaled，可暂停）。</summary>
        public static TimerComponent Timer => ComponentRegistry.GetComponent<TimerComponent>();

        /// <summary>服务器时间同步（校正本地时钟偏移，供每日重置 / 限时活动 / 冷却防作弊）。</summary>
        public static ServerTimeComponent ServerTime => ComponentRegistry.GetComponent<ServerTimeComponent>();

        /// <summary>红点 / 通知树（path-keyed，父结点聚合子结点计数）。</summary>
        public static RedDotComponent RedDot => ComponentRegistry.GetComponent<RedDotComponent>();

        /// <summary>GameObject 生成池（子弹 / 特效 / 敌人复用，避免 Instantiate/Destroy GC 尖刺）。</summary>
        public static SpawnPoolComponent SpawnPool => ComponentRegistry.GetComponent<SpawnPoolComponent>();

        /// <summary>世界分区流送组件。</summary>
        public static WorldStreamingComponent WorldStreaming => ComponentRegistry.GetComponent<WorldStreamingComponent>();

        /// <summary>性能遥测（帧时间 / 内存 / 热电 / 加载耗时，会话采样、离线批次上报）。</summary>
        public static TelemetryComponent Telemetry => ComponentRegistry.GetComponent<TelemetryComponent>();

        /// <summary>画质：设备分级、档位旋钮、自动画质与动态分辨率。</summary>
        public static QualityComponent Quality => ComponentRegistry.GetComponent<QualityComponent>();

        /// <summary>运行时性能覆盖层（帧时间 / 画质 / 内存 / 流送 / 崩溃遥测）。</summary>
        public static PerfOverlayComponent PerfOverlay => ComponentRegistry.GetComponent<PerfOverlayComponent>();

        /// <summary>HTTP/REST 客户端（登录 / 排行榜 / 邮件 / 抽卡校验，带重试 / 鉴权头 / Awaitable）。</summary>
        public static HttpComponent Http => ComponentRegistry.GetComponent<HttpComponent>();

        /// <summary>内购（IAP）：抽象 + 可注入平台后端 + 收据校验钩子。</summary>
        public static PurchaseComponent Purchase => ComponentRegistry.GetComponent<PurchaseComponent>();

        /// <summary>诊断（崩溃上报 + 日志 sink / 远程日志 / 文件日志）。</summary>
        public static EjoyFramework.Core.Diagnostics.DiagnosticsComponent Diagnostics => ComponentRegistry.GetComponent<EjoyFramework.Core.Diagnostics.DiagnosticsComponent>();

        /// <summary>推送通知（本地排程 + 远程 token，抽象 + 可注入平台 helper）。</summary>
        public static NotificationComponent Notification => ComponentRegistry.GetComponent<NotificationComponent>();
    }
}
