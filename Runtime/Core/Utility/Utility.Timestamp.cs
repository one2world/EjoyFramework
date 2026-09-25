//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Diagnostics;

namespace EjoyFramework.Core
{
    public static partial class Utility
    {
        /// <summary>
        /// 单调时钟工具。用于计算耗时/时间戳，替代 <c>DateTime.UtcNow</c>。
        ///
        /// 为什么不用 DateTime.UtcNow：
        ///   1) DateTime.UtcNow 受系统时钟跳变（NTP 校时、用户改表）影响，导致 Duration 出现负数或巨大跳变。
        ///   2) 自 1970 epoch 起的秒数约 1.7e9，转成 float 后尾数精度只有约 100 秒，子秒级耗时计算失真。
        /// 本工具基于 <see cref="Stopwatch"/> 高精度计数器，进程内单调递增。
        /// 注意：<see cref="Stopwatch.GetTimestamp"/> 返回的是自系统启动起算的绝对计数，数值巨大；
        /// 因此这里在类型初始化时捕获一次起点偏移 <c>s_StartTicks</c>，使 <see cref="Seconds"/>/<see cref="SecondsF"/>
        /// 从 0 起算。这样 <see cref="SecondsF"/> 的数值量级很小，float 尾数精度才真正充足，不会重蹈
        /// DateTime.UtcNow 的子秒级精度损失。
        /// 线程安全：只读静态字段 + Stopwatch.GetTimestamp（线程安全），可在任意线程调用。
        /// </summary>
        public static class Timestamp
        {
            private static readonly double s_TicksToSeconds = 1.0 / Stopwatch.Frequency;
            private static readonly long s_StartTicks = Stopwatch.GetTimestamp();

            /// <summary>
            /// 进程启动以来的单调秒数（double 精度，从 0 起算，推荐用于耗时差值）。
            /// </summary>
            public static double Seconds
            {
                get { return (Stopwatch.GetTimestamp() - s_StartTicks) * s_TicksToSeconds; }
            }

            /// <summary>
            /// 进程启动以来的单调秒数（float，兼容旧接口；从 0 起算，数值量级小，子秒精度充足）。
            /// </summary>
            public static float SecondsF
            {
                get { return (float)Seconds; }
            }

            /// <summary>
            /// 原始高精度计数（用于需要 long 时间戳的场景）。
            /// </summary>
            public static long RawTicks
            {
                get { return Stopwatch.GetTimestamp(); }
            }

            /// <summary>
            /// 将两次 <see cref="RawTicks"/> 之间的差值换算为秒。
            /// </summary>
            public static double TicksToSeconds(long elapsedTicks)
            {
                return elapsedTicks * s_TicksToSeconds;
            }
        }
    }
}
