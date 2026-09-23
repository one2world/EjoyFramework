//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Quality
{
    /// <summary>设备档位。数值即默认画质级别（0..4），写入遥测 <c>TelemetryKind.Tier</c> 的 I0。</summary>
    public enum DeviceTier
    {
        Minimum = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Ultra = 4,
    }

    /// <summary>档位来源。</summary>
    public enum DeviceTierReason
    {
        /// <summary>硬件评分落入阈值区间。</summary>
        Score = 0,

        /// <summary>命中机型 / GPU / CPU 覆盖规则（精确知识，优先于评分，不受上限规则约束）。</summary>
        Override = 1,

        /// <summary>评分结果被上限规则（如内存不足）压低。</summary>
        Capped = 2,

        /// <summary>玩家 / 业务手动指定。</summary>
        UserOverride = 3,
    }

    /// <summary>用于分级的设备信息（Unity 层从 SystemInfo 填充；测试可直接构造）。</summary>
    public struct DeviceProfile
    {
        public bool IsMobile;
        public string DeviceModel;
        public string GpuName;
        public string CpuName;
        public string OperatingSystem;
        public int CpuCores;
        public int CpuFrequencyMHz;
        public int SystemMemoryMB;
        public int GraphicsMemoryMB;

        /// <summary>Unity graphicsShaderLevel：35 = ES3.0，45 = ES3.1，50 = DX11/Metal/Vulkan 完整特性。</summary>
        public int GraphicsShaderLevel;
    }

    /// <summary>分级结果。</summary>
    public struct DeviceTierResult
    {
        public DeviceTier Tier;
        public DeviceTierReason Reason;

        /// <summary>硬件评分 0..1（Override / UserOverride 时仍会计算，便于后台校准）。</summary>
        public float Score;

        /// <summary>命中的规则所在行号（规则文本 1 起；无规则命中为 0）。</summary>
        public int RuleLine;
    }
}
