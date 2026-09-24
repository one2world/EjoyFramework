//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Quality;
using EjoyFramework.Core.Streaming;
using EjoyFramework.Core.Telemetry;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 画质组件：启动时读 SystemInfo 做设备分级（可配规则 TextAsset），恢复玩家上次选的档位，挂上内置应用器
    /// （<see cref="UnityQualityApplier"/>、世界流送半径），每帧把 <b>FrameTimingManager 的工作耗时</b> 喂给自动画质。
    ///
    /// 工作耗时 = max(主线程耗时 − Present 等待, 渲染线程耗时, GPU 耗时)。需要 Player Settings 勾选 Frame Timing Stats，
    /// 平台不支持或未开启时退化为墙钟帧间隔（此时帧率被上限封住，不会越过帧率档位升档）。
    ///
    /// 与 <see cref="PerformanceComponent"/> 的"自适应等级 + 切 Unity 质量档"是两套方案，同时开启会互相打架——本组件启动时检测并警告。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Quality")]
    public sealed class QualityComponent : GameFrameworkComponent
    {
        private const string PrefsUserLevel = "EjoyFramework.Quality.UserLevel";

        [Header("设备分级")]
        [Tooltip("分级规则文本（override / cap / thresholds，语法见 DeviceTierClassifier）。可空 = 纯硬件评分。")]
        [SerializeField] private TextAsset m_TierRules;

        [Header("自动画质")]
        [SerializeField] private bool m_AutoAdjust = true;

        [SerializeField] private bool m_DynamicResolution = true;

        [Range(0.5f, 1f)]
        [SerializeField] private float m_MinRenderScale = 0.7f;

        [Header("应用")]
        [Tooltip("把阴影距离 / LOD 偏置 / 贴图 mip 下限 / 目标帧率写入 QualitySettings / Application。")]
        [SerializeField] private bool m_ApplyUnitySettings = true;

        [Tooltip("用 ScalableBufferManager 做动态分辨率（内置管线 / 支持的平台）。HDRP / URP 请关掉并注册管线自己的应用器。")]
        [SerializeField] private bool m_ApplyRenderScaleViaScalableBuffer = true;

        [Tooltip("档位变化时写入 IWorldStreamingManager.RadiusScale。")]
        [SerializeField] private bool m_BindWorldStreaming = true;

        [Tooltip("把玩家选择的档位存到 PlayerPrefs，下次启动恢复。")]
        [SerializeField] private bool m_PersistUserLevel = true;

        private IQualityManager m_Quality;
        private UnityQualityApplier m_UnityApplier;
        private StreamingQualityApplier m_StreamingApplier;
        private FrameWorkTimeSampler m_WorkTime;

        protected override void Awake()
        {
            base.Awake();
            m_Quality = Framework.GetModule<IQualityManager>();
            m_Quality.AutoAdjust = m_AutoAdjust;
            m_Quality.Controller.MinRenderScale = m_MinRenderScale;
            m_Quality.DynamicResolution = m_DynamicResolution;

            DeviceTierClassifier classifier = new DeviceTierClassifier();
            if (m_TierRules != null)
            {
                try
                {
                    classifier.LoadRules(m_TierRules.text);
                }
                catch (FrameworkException ex)
                {
                    // 规则是内容数据：出错时不阻断启动，退回纯评分，并明确报错
                    Log.Error("QualityComponent：分级规则 '{0}' 解析失败，已忽略全部规则：{1}", m_TierRules.name, ex.Message);
                    classifier.Clear();
                }
            }

            DeviceProfile profile = ReadDeviceProfile();
            DeviceTierResult tier = m_Quality.ClassifyDevice(in profile, classifier);
            Log.Info("QualityComponent：设备分级 {0}（{1}，评分 {2:F2}）", tier.Tier, tier.Reason, tier.Score);

            if (m_PersistUserLevel)
            {
                int saved = PlayerPrefs.GetInt(PrefsUserLevel, -1);
                if (saved >= 0 && saved < QualityKnobs.LevelCount) m_Quality.SetUserLevel(saved);
            }

            if (m_ApplyUnitySettings || m_ApplyRenderScaleViaScalableBuffer)
            {
                m_UnityApplier = new UnityQualityApplier();
                m_UnityApplier.ApplyShadowDistance = m_ApplyUnitySettings;
                m_UnityApplier.ApplyLodBias = m_ApplyUnitySettings;
                m_UnityApplier.ApplyTextureMipLimit = m_ApplyUnitySettings;
                m_UnityApplier.ApplyTargetFrameRate = m_ApplyUnitySettings;
                m_UnityApplier.ApplyRenderScale = m_ApplyRenderScaleViaScalableBuffer;
                m_Quality.AddApplier(m_UnityApplier);
            }

            m_WorkTime = new FrameWorkTimeSampler();
        }

        private void Start()
        {
            // 其它组件（流送 / 遥测 / 性能）都 Awake 之后再接线
            if (m_BindWorldStreaming && Framework.HasModule<IWorldStreamingManager>())
            {
                m_StreamingApplier = new StreamingQualityApplier(Framework.GetModule<IWorldStreamingManager>());
                m_Quality.AddApplier(m_StreamingApplier);
            }

            if (Framework.HasModule<ITelemetryManager>()) m_Quality.SetTelemetry(Framework.GetModule<ITelemetryManager>());

            PerformanceComponent perf = ComponentRegistry.GetComponent<PerformanceComponent>();
            if (perf != null && perf.AppliesUnityQualityLevels && m_AutoAdjust)
            {
                Log.Warning("QualityComponent：PerformanceComponent 也在按自适应等级切 Unity 质量档，两套自动画质会互相覆盖，请关掉其中一个。");
            }

            m_Quality.ApplyAll();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float work;
            if (!m_WorkTime.TrySample(out work)) work = dt * 1000f;   // 不支持时退化为墙钟
            m_Quality.ReportFrame(work, dt);
        }

        protected override void OnDestroy()
        {
            try
            {
                if (m_Quality != null)
                {
                    if (m_UnityApplier != null) m_Quality.RemoveApplier(m_UnityApplier);
                    if (m_StreamingApplier != null) m_Quality.RemoveApplier(m_StreamingApplier);
                }
            }
            finally
            {
                base.OnDestroy();
            }
        }

        // ================================================================
        //  公开 API
        // ================================================================

        public IQualityManager Quality { get { return m_Quality; } }

        public int Level { get { return m_Quality.Level; } }

        public DeviceTierResult Tier { get { return m_Quality.Tier; } }

        public float RenderScale { get { return m_Quality.RenderScale; } }

        /// <summary>玩家在设置里选档（-1 = 自动），按配置持久化。</summary>
        public void SetUserLevel(int level)
        {
            m_Quality.SetUserLevel(level);
            if (m_PersistUserLevel)
            {
                PlayerPrefs.SetInt(PrefsUserLevel, level);
                PlayerPrefs.Save();
            }
        }

        /// <summary>从 SystemInfo 读取分级用的设备信息。</summary>
        public static DeviceProfile ReadDeviceProfile()
        {
            DeviceProfile p;
            p.IsMobile = Application.isMobilePlatform;
            p.DeviceModel = SystemInfo.deviceModel;
            p.GpuName = SystemInfo.graphicsDeviceName;
            p.CpuName = SystemInfo.processorType;
            p.OperatingSystem = SystemInfo.operatingSystem;
            p.CpuCores = SystemInfo.processorCount;
            p.CpuFrequencyMHz = SystemInfo.processorFrequency;
            p.SystemMemoryMB = SystemInfo.systemMemorySize;
            p.GraphicsMemoryMB = SystemInfo.graphicsMemorySize;
            p.GraphicsShaderLevel = SystemInfo.graphicsShaderLevel;
            return p;
        }
    }
}
