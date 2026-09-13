//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Security
{
    /// <summary>
    /// 客户端完整性自检。
    ///
    /// 重要：CRC32 只能检测**意外损坏**，不能防篡改——攻击者改完数据后可轻易重算 CRC。
    /// 若需要防篡改（tamper-evident），请使用本类的 <see cref="Hmac"/> / <see cref="VerifyHmac"/>，
    /// 以一个不下发给客户端的密钥做 HMAC-SHA256；CRC 仅用于廉价的损坏检测。
    /// </summary>
    public static class IntegrityCheck
    {
        private static readonly uint[] s_CrcTable = BuildTable();

        public static uint Crc32(byte[] data, int offset = 0, int count = -1)
        {
            if (data == null) return 0u;
            int end = count < 0 ? data.Length : offset + count;
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < end; i++)
                crc = (crc >> 8) ^ s_CrcTable[(crc ^ data[i]) & 0xFF];
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>校验数据的 CRC 是否匹配期望值。注意：仅损坏检测，不防篡改。</summary>
        public static bool Verify(byte[] data, uint expectedCrc) => Crc32(data) == expectedCrc;

        /// <summary>用密钥计算 HMAC-SHA256（防篡改门）。key 不应下发到客户端可见处。</summary>
        public static byte[] Hmac(byte[] data, byte[] key)
        {
            if (key == null || key.Length == 0) throw new FrameworkException("IntegrityCheck.Hmac: key is required.");
            using (var hmac = new System.Security.Cryptography.HMACSHA256(key))
            {
                return hmac.ComputeHash(data ?? System.Array.Empty<byte>());
            }
        }

        /// <summary>恒定时间校验 HMAC-SHA256 是否匹配（防时序侧信道）。</summary>
        public static bool VerifyHmac(byte[] data, byte[] key, byte[] expectedMac)
        {
            if (expectedMac == null) return false;
            byte[] actual = Hmac(data, key);
            if (actual.Length != expectedMac.Length) return false;
            int diff = 0;
            for (int i = 0; i < actual.Length; i++) diff |= actual[i] ^ expectedMac[i];
            return diff == 0;
        }

        private static uint[] BuildTable()
        {
            const uint poly = 0xEDB88320u;
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++) c = (c & 1u) != 0 ? (c >> 1) ^ poly : c >> 1;
                t[i] = c;
            }
            return t;
        }
    }
}
