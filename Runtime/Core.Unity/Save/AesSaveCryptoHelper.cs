//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.IO;
using System.Security.Cryptography;
using EjoyFramework.Core.Save;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// AES-256-CBC 加密 helper。
    /// 每次加密时随机生成 16 字节 IV，拼接在 cipher 前面：output = IV(16) || encrypted。
    /// </summary>
    public sealed class AesSaveCryptoHelper : ISaveCryptoHelper
    {
        private readonly byte[] m_Key;   // 32 bytes for AES-256

        public AesSaveCryptoHelper(byte[] key32Bytes)
        {
            if (key32Bytes == null || key32Bytes.Length != 32)
                throw new FrameworkException("AesSaveCryptoHelper requires a 32-byte key.");
            m_Key = (byte[])key32Bytes.Clone();
        }

        /// <summary>从任意字符串派生 32 字节 key（PBKDF2-HMAC-SHA256，10k 轮）。salt 固定（业务可改）。</summary>
        public static byte[] DeriveKey(string passphrase, byte[] salt = null)
        {
            byte[] s = salt ?? System.Text.Encoding.UTF8.GetBytes("EjoyFramework.Core.Save.v1");
            using (var pbkdf2 = new Rfc2898DeriveBytes(passphrase, s, 10000, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(32);
            }
        }

        public byte[] Encrypt(byte[] plain)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = m_Key;
                aes.GenerateIV();
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var ms = new MemoryStream())
                {
                    ms.Write(aes.IV, 0, aes.IV.Length);
                    using (var enc = aes.CreateEncryptor())
                    using (var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
                    {
                        cs.Write(plain, 0, plain.Length);
                    }
                    return ms.ToArray();
                }
            }
        }

        public byte[] Decrypt(byte[] cipher)
        {
            if (cipher == null || cipher.Length < 16)
                throw new FrameworkException("AesSaveCryptoHelper.Decrypt: payload too short.");
            byte[] iv = new byte[16];
            System.Buffer.BlockCopy(cipher, 0, iv, 0, 16);
            using (var aes = Aes.Create())
            {
                aes.Key = m_Key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var ms = new MemoryStream())
                using (var dec = aes.CreateDecryptor())
                using (var cs = new CryptoStream(new MemoryStream(cipher, 16, cipher.Length - 16), dec, CryptoStreamMode.Read))
                {
                    cs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
    }
}
