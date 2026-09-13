//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Performance
{
    /// <summary>
    /// 性能等级（自适应画质调节的基准档位）。
    /// 等级编号与 Unity QualitySettings.SetQualityLevel 的整数参数对齐：
    /// 0 最低画质，4 最高画质。
    /// </summary>
    public enum PerformanceLevel
    {
        Lowest  = 0,
        Low     = 1,
        Medium  = 2,
        High    = 3,
        Highest = 4,
    }
}
