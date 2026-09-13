//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Security.Cryptography;

namespace EjoyFramework.Core.Security
{
    /// <summary>
    /// 内存加密 int：抵御 Cheat Engine 类内存扫描修改。
    ///
    /// 威胁模型（请如实对待）：这是 anti-casual-cheat 混淆，不是密码学保护。
    /// 拥有完整内存访问与调试器的攻击者最终仍能还原值；目标只是挡住通过简单
    /// 内存搜索/diff 来定位并改写明文整数的业余作弊者。
    ///
    /// 设计：cipher = plain ^ perInstanceKey ^ perProcessSalt。
    /// perProcessSalt 用 <see cref="RandomNumberGenerator"/> 生成且**不随结构存储**，
    /// 因此仅靠相邻的 m_Key 无法还原明文，削弱内存 diff 攻击。
    ///
    /// 默认值约定：<see cref="SecureKeyStore.NextKey"/> 永不返回 0，故 m_Key==0 可靠地表示
    /// “未初始化/default”。default(SecureInt) 安全地读作 0（而非把 ProcessSalt 当成垃圾明文），
    /// 这样金钱/血量等字段即使保持默认也不会读出脏数据；首次写入时再惰性分配 key 并加密，
    /// 不削弱已构造实例的混淆强度。
    /// </summary>
    public struct SecureInt
    {
        // 每个实例随机 XOR key，让相同明文有不同 cipher；真正的混淆密钥还叠加未存储的 per-process salt
        private int m_Encrypted;
        private int m_Key;

        public SecureInt(int value)
        {
            m_Key = SecureKeyStore.NextKey();
            m_Encrypted = value ^ m_Key ^ SecureKeyStore.ProcessSalt;
        }

        public int Value
        {
            // m_Key==0 表示 default(SecureInt)，未加密过，直接读作 0
            get => m_Key == 0 ? 0 : m_Encrypted ^ m_Key ^ SecureKeyStore.ProcessSalt;
            set
            {
                // 对 default 实例首次写入时惰性分配 per-instance key，保证后续解密正确
                if (m_Key == 0)
                {
                    m_Key = SecureKeyStore.NextKey();
                }
                m_Encrypted = value ^ m_Key ^ SecureKeyStore.ProcessSalt;
            }
        }

        // 重新生成 key（业务可在关键时刻调用，让 cheat 表失效）
        public void Rekey()
        {
            int plain = Value;
            m_Key = SecureKeyStore.NextKey();
            m_Encrypted = plain ^ m_Key ^ SecureKeyStore.ProcessSalt;
        }

        public static implicit operator int(SecureInt s) => s.Value;
        public static implicit operator SecureInt(int v) => new SecureInt(v);

        public override string ToString() => Value.ToString();
    }

    /// <summary>
    /// 内存加密 float。威胁模型同 <see cref="SecureInt"/>（anti-casual-cheat，非密码学）。
    /// 默认值约定同 <see cref="SecureInt"/>：default(SecureFloat) 安全地读作 0f（而非解码垃圾比特），
    /// 首次写入时再惰性分配 key 并加密。
    /// </summary>
    public struct SecureFloat
    {
        private int m_EncryptedBits;
        private int m_Key;

        public SecureFloat(float value)
        {
            m_Key = SecureKeyStore.NextKey();
            m_EncryptedBits = BitConverter.SingleToInt32Bits(value) ^ m_Key ^ SecureKeyStore.ProcessSalt;
        }

        public float Value
        {
            // m_Key==0 表示 default(SecureFloat)，未加密过，直接读作 0f
            get => m_Key == 0
                ? 0f
                : BitConverter.Int32BitsToSingle(m_EncryptedBits ^ m_Key ^ SecureKeyStore.ProcessSalt);
            set
            {
                // 对 default 实例首次写入时惰性分配 per-instance key，保证后续解密正确
                if (m_Key == 0)
                {
                    m_Key = SecureKeyStore.NextKey();
                }
                m_EncryptedBits = BitConverter.SingleToInt32Bits(value) ^ m_Key ^ SecureKeyStore.ProcessSalt;
            }
        }

        public static implicit operator float(SecureFloat s) => s.Value;
        public static implicit operator SecureFloat(float v) => new SecureFloat(v);

        public override string ToString() => Value.ToString();
    }

    /// <summary>
    /// 进程级密钥存储：提供一个加密随机的、不随各 SecureInt/SecureFloat 结构存储的 per-process salt，
    /// 以及更强随机来源派生的 per-instance key。salt 与值分离存放，削弱内存 diff 攻击。
    /// </summary>
    internal static class SecureKeyStore
    {
        // 用 CSPRNG 生成的进程级 salt：集中存放一份，绝不随结构落到值旁边
        internal static readonly int ProcessSalt = GenerateProcessSalt();

        // per-instance key 用更好播种的 System.Random（线程安全包装）
        [ThreadStatic] private static Random t_Rng;

        private static int GenerateProcessSalt()
        {
            var buf = new byte[4];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buf);
            int s = BitConverter.ToInt32(buf, 0);
            return s == 0 ? unchecked((int)0x9E3779B1) : s;
        }

        public static int NextKey()
        {
            var rng = t_Rng;
            if (rng == null)
            {
                // 每线程用 CSPRNG 字节播种，避免 TickCount 可预测性
                var seed = new byte[4];
                using (var csp = RandomNumberGenerator.Create()) csp.GetBytes(seed);
                rng = t_Rng = new Random(BitConverter.ToInt32(seed, 0));
            }
            int k = rng.Next(int.MinValue, int.MaxValue);
            return k == 0 ? 1 : k;
        }
    }
}
