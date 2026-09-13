//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.AntiCheat
{
    /// <summary>
    /// 混淆布尔：以混淆字节存储（true=1, false=0 经 XOR 加密），并通过明文蜜罐字节提供篡改检测。
    /// 可隐式转换为 <see cref="bool"/>，作为其直接替代品使用。
    /// </summary>
    /// <remarks>
    /// 内部以 <see cref="byte"/> 形式参与异或运算，避免明文 bool 直接出现在内存中。
    /// 默认构造时按 false 处理且不报告篡改。
    /// </remarks>
    public struct ObscuredBool : IEquatable<ObscuredBool>
    {
        /// <summary>本实例的混淆密钥（仅使用低 8 位作用于字节）。</summary>
        internal byte m_Key;

        /// <summary>加密后的隐藏字节（明文字节 ^ 密钥）。</summary>
        internal byte m_Hidden;

        /// <summary>明文蜜罐字节副本，用于篡改检测。</summary>
        private byte m_FakeByte;

        /// <summary>是否已显式初始化。默认构造时为 false。</summary>
        private bool m_Inited;

        /// <summary>
        /// 以指定明文值构造混淆布尔。
        /// </summary>
        /// <param name="value">明文布尔值。</param>
        public ObscuredBool(bool value)
        {
            byte plain = value ? (byte)1 : (byte)0;
            m_Key = NextKeyByte();
            m_Hidden = (byte)(plain ^ m_Key);
            m_FakeByte = plain;
            m_Inited = true;
        }

        /// <summary>
        /// 明文值。读取解密、写入重新加密并刷新蜜罐（写入清除篡改状态）。未初始化读取为 false。
        /// </summary>
        public bool Value
        {
            get
            {
                if (!m_Inited)
                {
                    return false;
                }

                return (byte)(m_Hidden ^ m_Key) != 0;
            }
            set
            {
                byte plain = value ? (byte)1 : (byte)0;
                if (!m_Inited)
                {
                    m_Key = NextKeyByte();
                    m_Inited = true;
                }

                m_Hidden = (byte)(plain ^ m_Key);
                m_FakeByte = plain;
            }
        }

        /// <summary>
        /// 是否被篡改：解密后的字节与蜜罐字节不一致时为 true。未初始化恒为 false。
        /// </summary>
        public bool IsTampered
        {
            get
            {
                if (!m_Inited)
                {
                    return false;
                }

                return (byte)(m_Hidden ^ m_Key) != m_FakeByte;
            }
        }

        /// <summary>
        /// 从 <see cref="bool"/> 隐式构造混淆布尔。
        /// </summary>
        /// <param name="value">明文布尔值。</param>
        public static implicit operator ObscuredBool(bool value)
        {
            return new ObscuredBool(value);
        }

        /// <summary>
        /// 隐式解密为 <see cref="bool"/>。
        /// </summary>
        /// <param name="obscured">混淆布尔。</param>
        public static implicit operator bool(ObscuredBool obscured)
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
        /// 按解密后的明文值比较相等性。
        /// </summary>
        /// <param name="other">另一个混淆布尔。</param>
        /// <returns>明文值相等则为 true。</returns>
        public bool Equals(ObscuredBool other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// 与 <see cref="ObscuredBool"/> 或装箱 <see cref="bool"/> 比较相等性。
        /// </summary>
        /// <param name="obj">比较对象。</param>
        /// <returns>明文值相等则为 true。</returns>
        public override bool Equals(object obj)
        {
            if (obj is ObscuredBool other)
            {
                return Equals(other);
            }

            if (obj is bool plain)
            {
                return Value == plain;
            }

            return false;
        }

        /// <summary>
        /// 基于解密后的明文值返回哈希码，与 <see cref="bool"/> 的哈希一致。
        /// </summary>
        /// <returns>明文值的哈希码。</returns>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>
        /// 派生一个非零的字节密钥，复用 <see cref="ObscuredCrypto.NextKey"/> 的滚动计数器。
        /// </summary>
        /// <returns>非零字节密钥。</returns>
        private static byte NextKeyByte()
        {
            int key = ObscuredCrypto.NextKey();
            byte b = (byte)(key & 0xFF);
            // 保证非零，避免隐藏字节等于明文字节的退化情况。
            return b == 0 ? (byte)0xA5 : b;
        }
    }
}
