//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Telemetry;

namespace EjoyFramework.Core.Quality
{
    /// <summary>画质变化种类（位标记，传给 <see cref="IQualityApplier"/>）。</summary>
    [Flags]
    public enum QualityChange
    {
        None = 0,

        /// <summary>档位变化：所有旋钮都可能变了。</summary>
        Level = 1,

        /// <summary>仅动态渲染缩放变化（高频、便宜路径）。</summary>
        RenderScale = 2,

        /// <summary>设备档位变化（首次分级 / 手动覆盖）。</summary>
        Tier = 4,

        /// <summary>首次应用 / 强制全量应用。</summary>
        All = Level | RenderScale | Tier,
    }

    /// <summary>档位变化原因（写入遥测 <c>TelemetryKind.QualityChange</c> 的 I2）。</summary>
    public enum QualityChangeCause
    {
        Initial = 0,
        AutoDown = 1,
        AutoUp = 2,
        User = 3,
        Thermal = 4,
        Tier = 5,
    }

    /// <summary>画质应用器：把旋钮值落到引擎 / 系统。主线程调用。</summary>
    public interface IQualityApplier
    {
        void OnQualityChanged(IQualityManager quality, QualityChange change);
    }

    /// <summary>
    /// 画质管理：设备分级 → 默认档位 → 旋钮表 → 应用器；运行期自动画质（<see cref="AutoQualityController"/>）
    /// 在 [0, 上限] 内调档位与动态分辨率。
    ///
    /// 上限 = min(玩家选择的档位（未选 = 设备档位）, 热 / 电量上限)。玩家选了档位时起点即该档；自动画质仍可在其下调（保帧率），
    /// 不需要时关 <see cref="AutoAdjust"/>。加载 / 过场期间用 <see cref="SuspendAuto"/>/<see cref="ResumeAuto"/> 排除非代表性帧。
    /// </summary>
    public interface IQualityManager
    {
        // ---- 分级 ----

        /// <summary>用分级器对设备分级并以其档位为起点（通常启动时调用一次）。</summary>
        DeviceTierResult ClassifyDevice(in DeviceProfile profile, DeviceTierClassifier classifier);

        /// <summary>手动指定设备档位（null 取消，回到分级结果）。</summary>
        void SetTierOverride(DeviceTier? tier);

        DeviceTierResult Tier { get; }

        // ---- 档位 ----

        /// <summary>当前生效档位 0..4。</summary>
        int Level { get; }

        /// <summary>玩家选择的档位；-1 = 自动（跟随设备档位）。</summary>
        int UserLevel { get; }

        /// <summary>玩家在设置里选档位（-1 = 自动）。立即生效并作为新上限。</summary>
        void SetUserLevel(int level);

        /// <summary>热 / 电量上限（-1 = 无）。当前档位高于上限时立即降档。</summary>
        void SetThermalCap(int maxLevel);

        /// <summary>当前允许的最高档位。</summary>
        int MaxLevel { get; }

        // ---- 自动画质 ----

        bool AutoAdjust { get; set; }

        /// <summary>自动画质是否同时调动态分辨率。默认 true。</summary>
        bool DynamicResolution { get; set; }

        AutoQualityController Controller { get; }

        /// <summary>
        /// 喂一帧给自动画质（Unity 层每帧调用）。<paramref name="frameWorkMs"/> 应是本帧**实际工作耗时**
        /// （max(CPU 主线程, GPU)，来自 FrameTimingManager），而非墙钟帧间隔——帧率被 targetFrameRate / 垂直同步封顶时，
        /// 墙钟看不到富余，永远不会升档。拿不到工作耗时时退化为墙钟（只会降不会越过帧率上限升）。
        /// </summary>
        void ReportFrame(float frameWorkMs, float deltaSeconds);

        void SuspendAuto();

        void ResumeAuto();

        /// <summary>当前渲染缩放（= 档位表 RenderScale 与动态分辨率二者较小值）。</summary>
        float RenderScale { get; }

        // ---- 旋钮 ----

        /// <summary>定义或替换旋钮（每档一个值，长度须为 5）。</summary>
        void DefineKnob(int knobId, string name, float[] valuesPerLevel);

        /// <summary>当前档位的旋钮值（RenderScale 返回 <see cref="RenderScale"/>）。未定义抛异常。</summary>
        float GetKnob(int knobId);

        /// <summary>指定档位的旋钮值。</summary>
        float GetKnobAt(int knobId, int level);

        bool HasKnob(int knobId);

        // ---- 应用 ----

        void AddApplier(IQualityApplier applier);

        bool RemoveApplier(IQualityApplier applier);

        /// <summary>强制对所有应用器全量应用一次。</summary>
        void ApplyAll();

        void SetTelemetry(ITelemetryManager telemetry);

        /// <summary>自启动以来档位变化次数。</summary>
        int LevelChangeCount { get; }
    }
}
