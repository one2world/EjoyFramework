//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Coroutines;
using EjoyFramework.Core.Localization;
using System;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 基础组件。BaseComponent 必须先于其他依赖框架的 MonoBehaviour Update。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIResolutionAdapter))]
    [DefaultExecutionOrder(-10000)]
    [AddComponentMenu("EjoyFramework/Core/Base")]
    public sealed class BaseComponent : GameFrameworkComponent
    {
        private const int DefaultDpi = 96;

        private float m_GameSpeedBeforePause = 1f;

        [SerializeField]
        private bool m_EditorResourceMode = true;

        [SerializeField]
        private Language m_EditorLanguage = Language.Unspecified;

        [SerializeField]
        private string m_LogHelperTypeName = "EjoyFramework.Core.Unity.DefaultLogHelper";

        [SerializeField]
        private string m_JsonHelperTypeName = "EjoyFramework.Core.Unity.DefaultJsonHelper";

        [SerializeField]
        private int m_FrameRate = 60;

        [SerializeField]
        private float m_GameSpeed = 1f;

        [SerializeField]
        private bool m_RunInBackground = true;

        [SerializeField]
        private bool m_NeverSleep = true;

        [SerializeField]
        [Tooltip("Framework GameObject 在场景切换时不销毁。关闭时业务自己确保 framework 容器存活。")]
        private bool m_DontDestroyOnLoad = true;

        /// <summary>
        /// 获取或设置是否使用编辑器资源模式。
        /// </summary>
        public bool EditorResourceMode
        {
            get { return m_EditorResourceMode; }
            set { m_EditorResourceMode = value; }
        }

        /// <summary>
        /// 获取或设置编辑器语言。
        /// </summary>
        public Language EditorLanguage
        {
            get { return m_EditorLanguage; }
            set { m_EditorLanguage = value; }
        }

        /// <summary>
        /// 获取或设置游戏帧率。
        /// </summary>
        public int FrameRate
        {
            get { return m_FrameRate; }
            set { Application.targetFrameRate = m_FrameRate = value; }
        }

        /// <summary>
        /// 获取或设置游戏速度。
        /// </summary>
        public float GameSpeed
        {
            get { return m_GameSpeed; }
            set { Time.timeScale = m_GameSpeed = value >= 0f ? value : 0f; }
        }

        /// <summary>
        /// 获取游戏是否暂停。
        /// </summary>
        public bool IsGamePaused
        {
            get { return m_GameSpeed <= 0f; }
        }

        /// <summary>
        /// 获取是否正常游戏速度。
        /// </summary>
        public bool IsNormalGameSpeed
        {
            get { return m_GameSpeed == 1f; }
        }

        /// <summary>
        /// 获取或设置 Framework GameObject 在场景切换时是否不销毁。
        /// 仅在 Awake 阶段生效；运行时切换需要业务自管。
        /// </summary>
        public bool DontDestroyOnLoad
        {
            get { return m_DontDestroyOnLoad; }
            set { m_DontDestroyOnLoad = value; }
        }

        /// <summary>
        /// 获取或设置是否允许后台运行。
        /// </summary>
        public bool RunInBackground
        {
            get { return m_RunInBackground; }
            set { Application.runInBackground = m_RunInBackground = value; }
        }

        /// <summary>
        /// 获取或设置是否禁止休眠。
        /// </summary>
        public bool NeverSleep
        {
            get { return m_NeverSleep; }
            set
            {
                m_NeverSleep = value;
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }

        /// <summary>
        /// 尽早标记主线程：在任何场景加载 / MonoBehaviour.Awake 之前由 Unity 在主线程调用。
        /// 关闭"Awake 之前后台线程触碰框架时 EnsureMainThread 被静默放行"的窗口（core 层 noEngineReferences，
        /// 无法自带 RuntimeInitializeOnLoadMethod，故由 Unity 层在此提前标记）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void MarkMainThreadEarly()
        {
            Framework.MarkMainThread();
        }

        /// <summary>
        /// 游戏框架组件初始化。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            // 标记当前线程为主线程，供 Layer 1 EnsureMainThread 自检使用（BeforeSceneLoad 已提前标记，此处冗余兜底）。
            Framework.MarkMainThread();

            // 场景切换时保持 framework 容器存活：业务把所有 *Component 挂在此 GameObject 或其子节点。
            // 仅对 scene-root GameObject 有意义；嵌套在父对象下的子 GameObject 调用此 API 会被忽略。
            if (m_DontDestroyOnLoad)
            {
                if (transform.parent != null)
                {
                    Log.Warning("BaseComponent: DontDestroyOnLoad ignored because GameObject '{0}' has a parent. " +
                        "Move it to the scene root or unparent it before Awake.", gameObject.name);
                }
                else
                {
                    UnityEngine.Object.DontDestroyOnLoad(gameObject);
                }
            }

            InitLogHelper();
            InitJsonHelper();

            // Self-bootstrap the framework's coroutine helper before anything else.
            // BaseComponent has [DefaultExecutionOrder(-10000)] + DontDestroyOnLoad — it is the
            // canonical lifecycle anchor for the framework. Hosting the helper here means any
            // async loader (Resource / Scene / Entity) can fire LoadAssetAsync from the first
            // frame onward without depending on Unity's unspecified Awake-order across siblings.
            // CoroutineComponent (if also present) only fills in if no helper is set yet — it
            // does NOT override what we install here, so the host MonoBehaviour stays the one
            // with the strongest lifetime guarantees.
            EnsureCoroutineHelper();

            Log.Info("EjoyGame Framework Version: 1.0.0");
            Log.Info("Unity Version: {0}", Application.unityVersion);

            m_EditorResourceMode &= Application.isEditor;
            if (m_EditorResourceMode)
            {
                Log.Info("During this run, EjoyGame Framework will use editor resource files.");
            }

            Application.targetFrameRate = m_FrameRate;
            Time.timeScale = m_GameSpeed;
            Application.runInBackground = m_RunInBackground;
            Screen.sleepTimeout = m_NeverSleep ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            Application.lowMemory += OnLowMemory;
        }

        // True only while the application is actually quitting. Guards OnDestroy from tearing
        // down the framework on a mere scene unload (when DontDestroyOnLoad is disabled).
        private static bool s_Quitting;

        /// <summary>
        /// 进入 Play Mode 时重置静态退出标记。关闭 Domain Reload（Enter Play Mode Options）时，
        /// 静态字段不会随域重载清零；上一次退出设置的 s_Quitting=true 会残留到第二次 Play，
        /// 导致 OnDestroy 误判为"正在退出"而提前 Shutdown 框架。SubsystemRegistration 是最早的运行时
        /// 初始化时机，每次进入 Play Mode 必触发（无论是否启用 Domain Reload），在此清零最稳妥。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetQuittingFlag()
        {
            s_Quitting = false;
        }

        private void Start()
        {
            // Unity guarantees that every scene Awake has completed before the first Start.
            // All XxxComponent adapters must inject their helpers during Awake, so this is the
            // single composition-root boundary shared by Editor and Player builds.
            Framework.ValidateModuleConfigurations();
        }

        private void Update()
        {
            Framework.Update(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            Framework.LateUpdate(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            Framework.FixedUpdate(Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);
        }

        private void OnApplicationQuit()
        {
            s_Quitting = true;
            Application.lowMemory -= OnLowMemory;
            StopAllCoroutines();
        }

        protected override void OnDestroy()
        {
            // 注销自身：DontDestroyOnLoad 关闭时场景卸载会销毁本组件，必须从 ComponentRegistry 移除，
            // 以免残留失效实例。BaseComponent override 基类模板，经 base.OnDestroy() 完成注销。
            // 契约：注销先于下方 Framework.Shutdown()，故任何框架模块的 Shutdown() 不得依赖
            // ComponentRegistry 取回本组件（已核实 Framework.Shutdown 仅遍历模块、不回查注册表）。
            base.OnDestroy();

            // Only shut the framework down when the app is genuinely quitting. On a scene unload
            // with DontDestroyOnLoad disabled, this MonoBehaviour is destroyed but the framework
            // must stay alive — calling Shutdown() here would wipe every module mid-run.
            // The DontDestroyOnLoad owning root survives scene changes, so its OnDestroy only ever
            // fires at quit time anyway; the s_Quitting gate makes that explicit and also protects
            // non-DDoL hosts from the same hazard.
            if (s_Quitting || m_DontDestroyOnLoad)
            {
                Framework.Shutdown();

                // 真退出（genuine quit）才整体清空注册表：Player 构建无 Domain Reload，
                // 必须在此手动重置静态状态，对齐 Editor InitializeOnEnterPlayMode 的清空行为。
                // 仅在 s_Quitting（真退出）时清空；DontDestroyOnLoad 宿主的 OnDestroy 本就只在退出时触发。
                if (s_Quitting)
                {
                    ComponentRegistry.Clear();
                }
            }
        }

        /// <summary>
        /// 暂停游戏。
        /// </summary>
        public void PauseGame()
        {
            if (IsGamePaused)
            {
                return;
            }

            m_GameSpeedBeforePause = GameSpeed;
            GameSpeed = 0f;
        }

        /// <summary>
        /// 恢复游戏。
        /// </summary>
        public void ResumeGame()
        {
            if (!IsGamePaused)
            {
                return;
            }

            GameSpeed = m_GameSpeedBeforePause;
        }

        /// <summary>
        /// 重置为正常游戏速度。
        /// </summary>
        public void ResetNormalGameSpeed()
        {
            if (IsNormalGameSpeed)
            {
                return;
            }

            GameSpeed = 1f;
        }

        private void EnsureCoroutineHelper()
        {
            ICoroutineManager cm;
            try { cm = Framework.GetModule<ICoroutineManager>(); }
            catch (Exception ex)
            {
                // Lazy-create can throw if module type lookup fails (assembly stripping etc).
                // Surface clearly — without a coroutine helper, every async loader is dead.
                Log.Fatal("BaseComponent: cannot resolve ICoroutineManager: {0}", ex);
                return;
            }
            if (cm == null) { Log.Fatal("BaseComponent: ICoroutineManager is null."); return; }
            if (cm.HasHelper) return;       // Already set (e.g. by an explicit Configure path).
            cm.SetHelper(new UnityCoroutineHelper(this));
        }

        private void InitLogHelper()
        {
            if (string.IsNullOrEmpty(m_LogHelperTypeName))
            {
                return;
            }

            // 经生成的工厂表创建（零反射）；Inspector 仍存类型全名。Editor 未登记时 GeneratedHelperFactory 内部反射兜底。
            FrameworkLog.ILogHelper logHelper = GeneratedHelperFactory.Create(m_LogHelperTypeName, null) as FrameworkLog.ILogHelper;
            if (logHelper == null)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "Can not create log helper '{0}'. Run EjoyFramework/Core/CodeGen/Generate Helper Factories and ensure the type implements ILogHelper.",
                    m_LogHelperTypeName));
            }

            FrameworkLog.SetLogHelper(logHelper);
        }

        private void InitJsonHelper()
        {
            if (string.IsNullOrEmpty(m_JsonHelperTypeName))
            {
                return;
            }

            Utility.Json.IJsonHelper jsonHelper = GeneratedHelperFactory.Create(m_JsonHelperTypeName, null) as Utility.Json.IJsonHelper;
            if (jsonHelper == null)
            {
                UnityEngine.Debug.LogError(Utility.Text.Format(
                    "Can not create JSON helper '{0}'. Run EjoyFramework/Core/CodeGen/Generate Helper Factories.",
                    m_JsonHelperTypeName));
                return;
            }

            Utility.Json.SetJsonHelper(jsonHelper);
        }

        private void OnLowMemory()
        {
            Log.Info("Low memory reported...");

            ObjectPoolComponent objectPoolComponent = ComponentRegistry.GetComponent<ObjectPoolComponent>();
            if (objectPoolComponent != null)
            {
                objectPoolComponent.ReleaseAllUnused();
            }
        }
    }
}
