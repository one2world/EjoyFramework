//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.AntiCheat
{
    /// <summary>
    /// 混淆整数：以 XOR 加密其后备存储，使内存扫描器无法直接搜索到明文值，
    /// 同时通过一份明文“蜜罐”副本提供篡改检测。可隐式转换为 <see cref="int"/>，
    /// 因此可作为 <see cref="int"/> 的直接替代品参与算术（如 <c>obscuredInt + 5</c>）。
    /// </summary>
    /// <remarks>
    /// 存储模型：
    /// <list type="bullet">
    /// <item><description><c>m_Hidden = value ^ m_Key</c>：加密后的隐藏值，是内存中实际可见的字节。</description></item>
    /// <item><description><c>m_Fake</c>：与隐藏值并列保存的明文蜜罐副本，仅用于篡改检测。</description></item>
    /// </list>
    /// 篡改检测原理：外部内存编辑器通常只会修改它“看得见”的 <c>m_Hidden</c>，
    /// 这会导致解密结果 <c>m_Hidden ^ m_Key</c> 与蜜罐 <c>m_Fake</c> 不一致，
    /// 于是 <see cref="IsTampered"/> 返回 true。
    /// 默认构造（未经构造函数初始化）时所有字段为 0，<c>m_Inited</c> 为 false，
    /// 此时一律按 0 处理且不报告篡改。
    /// </remarks>
    public struct ObscuredInt : IEquatable<ObscuredInt>
    {
        /// <summary>本实例的混淆密钥。测试通过 internal 可见性断言隐藏值确实偏离明文。</summary>
        internal int m_Key;

        /// <summary>加密后的隐藏值（明文 ^ 密钥）。</summary>
        internal int m_Hidden;

        /// <summary>明文蜜罐副本，用于篡改检测。</summary>
        private int m_Fake;

        /// <summary>是否已显式初始化。默认构造时为 false，按 0 处理且不报告篡改。</summary>
        private bool m_Inited;

        /// <summary>
        /// 以指定明文值构造混淆整数。
        /// </summary>
        /// <param name="value">明文整数值。</param>
        public ObscuredInt(int value)
        {
            m_Key = ObscuredCrypto.NextKey();
            m_Hidden = value ^ m_Key;
            m_Fake = value;
            m_Inited = true;
        }

        /// <summary>
        /// 明文值。读取时解密（<c>m_Hidden ^ m_Key</c>）；写入时重新加密并刷新蜜罐，
        /// 因此写入也会清除既有的篡改状态。未初始化实例读取为 0。
        /// </summary>
        public int Value
        {
            get
            {
                if (!m_Inited)
                {
                    return 0;
                }

                return m_Hidden ^ m_Key;
            }
            set
            {
                if (!m_Inited)
                {
                    m_Key = ObscuredCrypto.NextKey();
                    m_Inited = true;
                }

                m_Hidden = value ^ m_Key;
                m_Fake = value;
            }
        }

        /// <summary>
        /// 是否被篡改：当解密后的隐藏值与明文蜜罐副本不一致时为 true。
        /// 未初始化实例恒为 false（视为干净的 0）。
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
        /// 从 <see cref="int"/> 隐式构造混淆整数。
        /// </summary>
        /// <param name="value">明文整数值。</param>
        public static implicit operator ObscuredInt(int value)
        {
            return new ObscuredInt(value);
        }

        /// <summary>
        /// 隐式解密为 <see cref="int"/>。
        /// </summary>
        /// <param name="obscured">混淆整数。</param>
        public static implicit operator int(ObscuredInt obscured)
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
        /// <param name="other">另一个混淆整数。</param>
        /// <returns>明文值相等则为 true。</returns>
        public bool Equals(ObscuredInt other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// 与 <see cref="ObscuredInt"/> 或装箱 <see cref="int"/> 比较相等性。
        /// </summary>
        /// <param name="obj">比较对象。</param>
        /// <returns>明文值相等则为 true。</returns>
        public override bool Equals(object obj)
        {
            if (obj is ObscuredInt other)
            {
                return Equals(other);
            }

            if (obj is int plain)
            {
                return Value == plain;
            }

            return false;
        }

        /// <summary>
        /// 基于解密后的明文值返回哈希码，与 <see cref="int"/> 的哈希一致。
        /// </summary>
        /// <returns>明文值的哈希码。</returns>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}
