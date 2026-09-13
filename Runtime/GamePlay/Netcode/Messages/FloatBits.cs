//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Runtime.InteropServices;

namespace EjoyFramework.GamePlay.Netcode.Messages
{
    /// <summary>
    /// <see cref="float"/> 与其 32 位整型位模式之间的无损互转工具。
    ///
    /// 不使用 <see cref="System.BitConverter"/>，因其字节序依赖运行平台（<c>IsLittleEndian</c>），
    /// 难以保证跨端一致。这里通过显式布局的 union（<see cref="FloatUInt"/>）只做「重解释位模式」，
    /// 字节序则完全交由 <see cref="NetWriter"/>/<see cref="NetReader"/> 的小端拆装字节逻辑控制，
    /// 从而保证序列化结果与平台无关、可精确往返。
    /// </summary>
    internal static class FloatBits
    {
        /// <summary>
        /// float 与 uint 共享同一段内存的联合体，用于无损读取/写入位模式。
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatUInt
        {
            [FieldOffset(0)]
            public float AsFloat;

            [FieldOffset(0)]
            public uint AsUInt;
        }

        /// <summary>
        /// 取得 <paramref name="value"/> 的 32 位无符号位模式。
        /// </summary>
        public static uint SingleToUInt32(float value)
        {
            FloatUInt u = default;
            u.AsFloat = value;
            return u.AsUInt;
        }

        /// <summary>
        /// 将 32 位无符号位模式重解释为 <see cref="float"/>。
        /// </summary>
        public static float UInt32ToSingle(uint bits)
        {
            FloatUInt u = default;
            u.AsUInt = bits;
            return u.AsFloat;
        }
    }
}
