//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace EjoyFramework.GamePlay.AntiCheat
{
    /// <summary>
    /// 混淆值类型共用的内部工具：密钥派生与 float &lt;-&gt; int32 比特位互转（不使用 unsafe）。
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// 1. 密钥派生使用一个静态滚动计数器与固定常量异或，保证每个实例拥有不同密钥，
    ///    且不依赖任何随机数源（不使用 <c>System.Random</c> 或 <c>UnityEngine.Random</c>），
    ///    因此完全确定、引擎无关。任意密钥下加解密的往返一致性都必须成立。
    /// 2. float 的混淆作用于其 IEEE-754 比特位（int32），而非浮点算术本身，
    ///    以避免精度漂移；NaN / 无穷的比特位也能被精确还原。
    /// </remarks>
    internal static class ObscuredCrypto
    {
        /// <summary>
        /// 固定进程常量，参与密钥派生。任意非零常量均可，仅用于让隐藏字段偏离明文。
        /// </summary>
        internal const int KeyConstant = unchecked((int)0x5A17C0DEu);

        /// <summary>
        /// long 版本的固定常量，用于派生 64 位密钥。
        /// </summary>
        internal const long KeyConstantLong = unchecked((long)0x5A17C0DE_C0FFEE17uL);

        /// <summary>
        /// 滚动计数器，每次派生密钥时自增，确保相邻实例密钥不同。线程安全自增。
        /// </summary>
        private static int s_RollingCounter;

        /// <summary>
        /// 派生一个 32 位密钥。结果恒为非零，避免出现 XOR 后隐藏值等于明文的退化情况。
        /// </summary>
        /// <returns>非零的 32 位密钥。</returns>
        internal static int NextKey()
        {
            int counter = Interlocked.Increment(ref s_RollingCounter);
            // 将计数器扰动后与常量异或，得到分散且确定的密钥。
            int scrambled = counter * unchecked((int)0x9E3779B1u);
            int key = scrambled ^ KeyConstant;
            // 保证非零：若恰好为 0，退化为常量本身（常量非零）。
            return key == 0 ? KeyConstant : key;
        }

        /// <summary>
        /// 派生一个 64 位密钥。结果恒为非零。
        /// </summary>
        /// <returns>非零的 64 位密钥。</returns>
        internal static long NextKeyLong()
        {
            int counter = Interlocked.Increment(ref s_RollingCounter);
            long scrambled = counter * unchecked((long)0x9E3779B97F4A7C15uL);
            long key = scrambled ^ KeyConstantLong;
            return key == 0L ? KeyConstantLong : key;
        }

        /// <summary>
        /// 将 float 转换为其 IEEE-754 的 32 位整数比特表示（不使用 unsafe）。
        /// </summary>
        /// <param name="value">浮点值。</param>
        /// <returns>对应的 32 位比特位。</returns>
        internal static int FloatToBits(float value)
        {
            return FloatIntUnion.FromFloat(value).IntValue;
        }

        /// <summary>
        /// 将 32 位整数比特还原为 float（不使用 unsafe）。
        /// </summary>
        /// <param name="bits">IEEE-754 的 32 位比特位。</param>
        /// <returns>对应的浮点值。</returns>
        internal static float BitsToFloat(int bits)
        {
            return FloatIntUnion.FromInt(bits).FloatValue;
        }

        /// <summary>
        /// 显式布局的联合体，在 float 与 int 之间做按位重解释。等价于
        /// <c>BitConverter.SingleToInt32Bits</c> / <c>Int32BitsToSingle</c>，
        /// 但不依赖目标框架是否提供这两个 API，且不使用 unsafe。
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatIntUnion
        {
            [FieldOffset(0)]
            private float m_Float;

            [FieldOffset(0)]
            private int m_Int;

            internal float FloatValue
            {
                get { return m_Float; }
            }

            internal int IntValue
            {
                get { return m_Int; }
            }

            internal static FloatIntUnion FromFloat(float value)
            {
                FloatIntUnion union = default;
                union.m_Float = value;
                return union;
            }

            internal static FloatIntUnion FromInt(int value)
            {
                FloatIntUnion union = default;
                union.m_Int = value;
                return union;
            }
        }
    }
}
