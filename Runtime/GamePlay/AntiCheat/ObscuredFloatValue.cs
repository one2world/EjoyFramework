//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.AntiCheat
{
    /// <summary>
    /// 混淆单精度浮点：对其 IEEE-754 的 32 位比特位做 XOR 加密（而非浮点算术），
    /// 因此可精确还原，包括分数、极值、NaN 与无穷。通过明文比特蜜罐提供篡改检测。
    /// 可隐式转换为 <see cref="float"/>，作为其直接替代品参与算术。
    /// </summary>
    /// <remarks>
    /// NaN 说明：混淆作用于比特位，往返后可得到与写入完全相同的 NaN 比特模式；
    /// 但由于 IEEE-754 规定 <c>NaN != NaN</c>，请勿用 <c>==</c> 比较两个 NaN 的明文值，
    /// 篡改检测则比较比特位，对 NaN 同样可靠。默认构造时按 0f 处理且不报告篡改。
    /// </remarks>
    public struct ObscuredFloat : IEquatable<ObscuredFloat>
    {
        /// <summary>本实例的混淆密钥（作用于比特位）。</summary>
        internal int m_Key;

        /// <summary>加密后的隐藏比特位（明文比特 ^ 密钥）。</summary>
        internal int m_Hidden;

        /// <summary>明文比特蜜罐副本，用于篡改检测。</summary>
        private int m_FakeBits;

        /// <summary>是否已显式初始化。默认构造时为 false。</summary>
        private bool m_Inited;

        /// <summary>
        /// 以指定明文值构造混淆浮点。
        /// </summary>
        /// <param name="value">明文浮点值。</param>
        public ObscuredFloat(float value)
        {
            int bits = ObscuredCrypto.FloatToBits(value);
            m_Key = ObscuredCrypto.NextKey();
            m_Hidden = bits ^ m_Key;
            m_FakeBits = bits;
            m_Inited = true;
        }

        /// <summary>
        /// 明文值。读取解密比特并还原为 float；写入将值转为比特、重新加密并刷新蜜罐
        /// （写入清除篡改状态）。未初始化读取为 0f。
        /// </summary>
        public float Value
        {
            get
            {
                if (!m_Inited)
                {
                    return 0f;
                }

                return ObscuredCrypto.BitsToFloat(m_Hidden ^ m_Key);
            }
            set
            {
                int bits = ObscuredCrypto.FloatToBits(value);
                if (!m_Inited)
                {
                    m_Key = ObscuredCrypto.NextKey();
                    m_Inited = true;
                }

                m_Hidden = bits ^ m_Key;
                m_FakeBits = bits;
            }
        }

        /// <summary>
        /// 是否被篡改：解密后的比特位与蜜罐比特位不一致时为 true。
        /// 以比特位（而非浮点值）比较，因此对 NaN 同样可靠。未初始化恒为 false。
        /// </summary>
        public bool IsTampered
        {
            get
            {
                if (!m_Inited)
                {
                    return false;
                }

                return (m_Hidden ^ m_Key) != m_FakeBits;
            }
        }

        /// <summary>
        /// 从 <see cref="float"/> 隐式构造混淆浮点。
        /// </summary>
        /// <param name="value">明文浮点值。</param>
        public static implicit operator ObscuredFloat(float value)
        {
            return new ObscuredFloat(value);
        }

        /// <summary>
        /// 隐式解密为 <see cref="float"/>。
        /// </summary>
        /// <param name="obscured">混淆浮点。</param>
        public static implicit operator float(ObscuredFloat obscured)
        {
            return obscured.Value;
        }

        /// <summary>
        /// 返回解密后明文值的字符串表示。
        /// </summary>
        /// <returns>明文值的字符串。</returns>
        public override string ToString()
        {
            return Value.ToString();
        }

        /// <summary>
        /// 按解密后的明文值比较相等性。注意 NaN 的明文比较恒为 false。
        /// </summary>
        /// <param name="other">另一个混淆浮点。</param>
        /// <returns>明文值相等则为 true。</returns>
        public bool Equals(ObscuredFloat other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// 与 <see cref="ObscuredFloat"/> 或装箱 <see cref="float"/> 比较相等性。
        /// </summary>
        /// <param name="obj">比较对象。</param>
        /// <returns>明文值相等则为 true。</returns>
        public override bool Equals(object obj)
        {
            if (obj is ObscuredFloat other)
            {
                return Equals(other);
            }

            if (obj is float plain)
            {
                return Value == plain;
            }

            return false;
        }

        /// <summary>
        /// 基于解密后的明文值返回哈希码，与 <see cref="float"/> 的哈希一致。
        /// </summary>
        /// <returns>明文值的哈希码。</returns>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}
