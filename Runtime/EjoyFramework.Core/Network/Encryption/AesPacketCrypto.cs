//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Security.Cryptography;

namespace EjoyFramework.Core.Network.Encryption
{
    /// <summary>
    /// 网络 packet AES-256-CBC + HMAC-SHA256 加密层（Encrypt-then-MAC）。
    ///
    /// 协议（出站）：[IV(16)][ciphertext][hmac(32)]
    /// HMAC 覆盖 IV+ciphertext，防止篡改。入站先验 HMAC，再解密。
    ///
    /// 性能：复用 <see cref="Aes"/>/<see cref="HMACSHA256"/> 实例，避免每包 <c>Aes.Create()</c>（实测慢 10–50×）。
    /// 线程安全：Encrypt（发送线程）与 Decrypt（接收线程）可能并发，故所有用到共享 Aes/HMAC 的操作在 <see cref="m_Lock"/> 下串行。
    /// </summary>
    public sealed class AesPacketCrypto : IDisposable
    {
        private const int IV_LEN = 16, MAC_LEN = 32;

        private readonly byte[] m_Key;
        private readonly byte[] m_HmacKey;
        private readonly object m_Lock = new object();
        private readonly Aes m_Aes;
        private readonly HMACSHA256 m_Hmac;
        private bool m_Disposed;

        /// <summary>encryption key 32 字节，hmac key 32 字节（建议从握手协商）。</summary>
        public AesPacketCrypto(byte[] key32, byte[] hmac32)
        {
            if (key32 == null || key32.Length != 32) throw new FrameworkException("AesPacketCrypto: key must be 32 bytes.");
            if (hmac32 == null || hmac32.Length != 32) throw new FrameworkException("AesPacketCrypto: hmacKey must be 32 bytes.");
            m_Key = (byte[])key32.Clone();
            m_HmacKey = (byte[])hmac32.Clone();

            m_Aes = Aes.Create();
            m_Aes.Key = m_Key;
            m_Aes.Mode = CipherMode.CBC;
            m_Aes.Padding = PaddingMode.PKCS7;
            m_Hmac = new HMACSHA256(m_HmacKey);
        }

        public byte[] Encrypt(byte[] plain)
        {
            if (plain == null) throw new FrameworkException("AesPacketCrypto: plain is null.");
            lock (m_Lock)
            {
                if (m_Disposed) throw new FrameworkException("AesPacketCrypto is disposed.");

                m_Aes.GenerateIV();
                byte[] iv = m_Aes.IV;

                byte[] cipher;
                using (var ms = new MemoryStream())
                using (var enc = m_Aes.CreateEncryptor())
                using (var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
                {
                    cs.Write(plain, 0, plain.Length);
                    cs.FlushFinalBlock();
                    cipher = ms.ToArray();
                }

                // [IV][cipher]
                byte[] ivCipher = new byte[iv.Length + cipher.Length];
                Buffer.BlockCopy(iv, 0, ivCipher, 0, iv.Length);
                Buffer.BlockCopy(cipher, 0, ivCipher, iv.Length, cipher.Length);

                // HMAC 覆盖 [IV][cipher]
                byte[] mac = m_Hmac.ComputeHash(ivCipher);

                byte[] output = new byte[ivCipher.Length + mac.Length];
                Buffer.BlockCopy(ivCipher, 0, output, 0, ivCipher.Length);
                Buffer.BlockCopy(mac, 0, output, ivCipher.Length, mac.Length);
                return output;
            }
        }

        public byte[] Decrypt(byte[] envelope)
        {
            if (envelope == null || envelope.Length < IV_LEN + MAC_LEN)
                throw new FrameworkException("AesPacketCrypto: envelope too short.");

            int macStart = envelope.Length - MAC_LEN;
            byte[] mac = new byte[MAC_LEN];
            Buffer.BlockCopy(envelope, macStart, mac, 0, MAC_LEN);

            byte[] ivCipher = new byte[macStart];
            Buffer.BlockCopy(envelope, 0, ivCipher, 0, macStart);

            byte[] iv = new byte[IV_LEN];
            Buffer.BlockCopy(envelope, 0, iv, 0, IV_LEN);

            lock (m_Lock)
            {
                if (m_Disposed) throw new FrameworkException("AesPacketCrypto is disposed.");

                byte[] expectedMac = m_Hmac.ComputeHash(ivCipher);
                if (!ConstantTimeEquals(mac, expectedMac))
                    throw new FrameworkException("AesPacketCrypto: HMAC mismatch (tampered or wrong key).");

                m_Aes.IV = iv;
                using (var ms = new MemoryStream())
                using (var dec = m_Aes.CreateDecryptor())
                using (var cs = new CryptoStream(new MemoryStream(envelope, IV_LEN, macStart - IV_LEN), dec, CryptoStreamMode.Read))
                {
                    cs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        public void Dispose()
        {
            lock (m_Lock)
            {
                if (m_Disposed) return;
                m_Disposed = true;
                try { m_Aes.Dispose(); } catch (Exception ex) { FrameworkLog.Warning("AesPacketCrypto Aes dispose threw: {0}", ex); }
                try { m_Hmac.Dispose(); } catch (Exception ex) { FrameworkLog.Warning("AesPacketCrypto Hmac dispose threw: {0}", ex); }
                Array.Clear(m_Key, 0, m_Key.Length);
                Array.Clear(m_HmacKey, 0, m_HmacKey.Length);
            }
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
