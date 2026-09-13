//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.AntiCheat
{
    /// <summary>
    /// 混淆 64 位整数：以 XOR 加密其后备存储，并通过明文蜜罐副本提供篡改检测。
    /// 可隐式转换为 <see cref="long"/>，作为其直接替代品参与算术。
    /// </summary>
    /// <remarks>
    /// 存储与篡改检测模型同 <see cref="ObscuredInt"/>，仅密钥与值为 64 位。
    /// 默认构造时按 0 处理且不报告篡改。
    /// </remarks>
    public struct ObscuredLong : IEquatable<ObscuredLong>
    {
        /// <summary>本实例的 64 位混淆密钥。</summary>
        internal long m_Key;

        /// <summary>加密后的隐藏值（明文 ^ 密钥）。</summary>
        internal long m_Hidden;

        /// <summary>明文蜜罐副本，用于篡改检测。</summary>
        private long m_Fake;

        /// <summary>是否已显式初始化。默认构造时为 false。</summary>
        private bool m_Inited;

        /// <summary>
        /// 以指定明文值构造混淆长整数。
        /// </summary>
        /// <param name="value">明文长整数值。</param>
        public ObscuredLong(long value)
        {
            m_Key = ObscuredCrypto.NextKeyLong();
            m_Hidden = value ^ m_Key;
            m_Fake = value;
            m_Inited = true;
        }

        /// <summary>
        /// 明文值。读取解密、写入重新加密并刷新蜜罐（写入清除篡改状态）。未初始化读取为 0。
        /// </summary>
        public long Value
        {
            get
            {
                if (!m_Inited)
                {
                    return 0L;
                }

                return m_Hidden ^ m_Key;
            }
            set
            {
                if (!m_Inited)
                {
                    m_Key = ObscuredCrypto.NextKeyLong();
                    m_Inited = true;
                }

                m_Hidden = value ^ m_Key;
                m_Fake = value;
            }
        }

        /// <summary>
        /// 是否被篡改：解密值与蜜罐不一致时为 true。未初始化恒为 false。
        /// </summary>
        public bool IsTampered
        {
            get
            {
                if (!m_Inited)
                {
                    return false;
                }

                return (m_Hidden ^ m_Key) != m_Fake;
            }
        }

        /// <summary>
        /// 从 <see cref="long"/> 隐式构造混淆长整数。
        /// </summary>
        /// <param name="value">明文长整数值。</param>
        public static implicit operator ObscuredLong(long value)
        {
            return new ObscuredLong(value);
        }

        /// <summary>
        /// 隐式解密为 <see cref="long"/>。
        /// </summary>
        /// <param name="obscured">混淆长整数。</param>
        public static implicit operator long(ObscuredLong obscured)
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
        /// <param name="other">另一个混淆长整数。</param>
        /// <returns>明文值相等则为 true。</returns>
        public bool Equals(ObscuredLong other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// 与 <see cref="ObscuredLong"/> 或装箱 <see cref="long"/> 比较相等性。
        /// </summary>
        /// <param name="obj">比较对象。</param>
        /// <returns>明文值相等则为 true。</returns>
        public override bool Equals(object obj)
        {
            if (obj is ObscuredLong other)
            {
                return Equals(other);
            }

            if (obj is long plain)
            {
                return Value == plain;
            }

            return false;
        }

        /// <summary>
        /// 基于解密后的明文值返回哈希码，与 <see cref="long"/> 的哈希一致。
        /// </summary>
        /// <returns>明文值的哈希码。</returns>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}
