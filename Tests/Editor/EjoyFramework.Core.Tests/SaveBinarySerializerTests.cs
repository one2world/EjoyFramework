//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using EjoyFramework.Core.Save;
using EjoyFramework.Core.Serialization;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// SaveManager 可选二进制序列化路径单测。
    /// 覆盖：注入 ISaveBinarySerializer 后 Write/Load 往返一致 / envelope.Format==1 /
    ///       不注入时默认 JSON 路径不受影响（Format==0） / Format==1 但缺二进制序列化器视为损坏 /
    ///       二进制路径同样受加密链路保护。
    /// 使用内存 storage helper + 直白 JSON serializer（信封外层用），与 SaveManagerTests 一致；无 Unity 运行时依赖。
    /// </summary>
    public class SaveBinarySerializerTests
    {
        private SaveManager m_SM;
        private InMemoryStorage m_Storage;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_SM = new SaveManager();
            m_Storage = new InMemoryStorage();
            m_SM.SetStorageHelper(m_Storage);
            m_SM.SetSerializer(new TrivialJsonSerializer());   // 信封外层 JSON 始终需要
            m_SM.SetCurrentVersion(1);
        }

        [Test]
        public void BinarySerializer_Write_Then_Load_RoundTrip()
        {
            m_SM.SetBinarySerializer(new TestDataBinarySerializer());

            var data = new TestData { Hp = 1234, Name = "BinaryHero" };
            var meta = new SaveSlotMetadata { PlayerName = "Alice", Level = 9 };
            Assert.IsTrue(m_SM.WriteSlot(1, data, meta));

            var loaded = m_SM.LoadSlot<TestData>(1);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1234, loaded.Hp);
            Assert.AreEqual("BinaryHero", loaded.Name);
        }

        [Test]
        public void BinaryPath_SetsEnvelopeFormat_1()
        {
            m_SM.SetBinarySerializer(new TestDataBinarySerializer());
            m_SM.WriteSlot(1, new TestData { Hp = 7, Name = "X" }, new SaveSlotMetadata());

            Assert.AreEqual(1, m_Storage.PeekEnvelopeFormat(1), "注入二进制序列化器后信封 Format 应为 1");
        }

        [Test]
        public void DefaultJsonPath_SetsEnvelopeFormat_0_When_No_BinarySerializer()
        {
            // 不注入二进制序列化器 → 默认 JSON 路径，Format 应为 0（向后兼容）。
            m_SM.WriteSlot(1, new TestData { Hp = 100, Name = "Hero" }, new SaveSlotMetadata());

            Assert.AreEqual(0, m_Storage.PeekEnvelopeFormat(1), "未注入二进制序列化器时应保持 JSON 路径 Format=0");

            var loaded = m_SM.LoadSlot<TestData>(1);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(100, loaded.Hp);
            Assert.AreEqual("Hero", loaded.Name);
        }

        [Test]
        public void BinaryPayload_IsNotPlainJson()
        {
            // 二进制载荷被 Base64 编码塞进 DataJson，名字字段不应以可读 JSON 文本出现在 DataJson 的明文里。
            m_SM.SetBinarySerializer(new TestDataBinarySerializer());
            m_SM.WriteSlot(1, new TestData { Hp = 1, Name = "SecretField" }, new SaveSlotMetadata());

            // DataJson 是 Base64，不应直接包含字段名的 JSON 形态。
            string dataJson = m_Storage.PeekEnvelopeDataJson(1);
            StringAssert.DoesNotContain("\"Name\"", dataJson, "二进制载荷不应携带 JSON 字段名");
        }

        [Test]
        public void BinaryFormat_Without_BinarySerializer_TreatedAsCorrupt()
        {
            // 先以二进制写入。
            m_SM.SetBinarySerializer(new TestDataBinarySerializer());
            m_SM.WriteSlot(1, new TestData { Hp = 55, Name = "Y" }, new SaveSlotMetadata());

            // 用一个全新的、未注入二进制序列化器的 SaveManager 读取同一存档。
            var reader = new SaveManager();
            reader.SetStorageHelper(m_Storage);
            reader.SetSerializer(new TrivialJsonSerializer());
            reader.SetCurrentVersion(1);

            string err = null;
            reader.SaveCorrupted += (slot, msg) => err = msg;
            var loaded = reader.LoadSlot<TestData>(1);

            Assert.IsNull(loaded, "Format=1 但无二进制序列化器应返回 default");
            Assert.IsNotNull(err, "应触发 SaveCorrupted");
            StringAssert.Contains("binary", err);
        }

        [Test]
        public void BinaryPath_RoundTrip_With_Encryption()
        {
            // 二进制载荷同样经过 HMAC framing + 加密链路；加解密对二进制路径透明。
            m_SM.SetBinarySerializer(new TestDataBinarySerializer());
            m_SM.SetCryptoHelper(new XorCrypto(0x5A));

            var data = new TestData { Hp = 321, Name = "Encrypted" };
            m_SM.WriteSlot(1, data, new SaveSlotMetadata());

            var payload = m_Storage.PeekPayload(1);
            string plaintext = Encoding.UTF8.GetString(payload);
            StringAssert.DoesNotContain("DataJson", plaintext, "加密后 payload 不应出现信封字段名");

            var loaded = m_SM.LoadSlot<TestData>(1);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(321, loaded.Hp);
            Assert.AreEqual("Encrypted", loaded.Name);
        }

        [Test]
        public void BinaryPayload_V1ToV2_UsesBinaryMigrationChain()
        {
            m_SM.SetBinarySerializer(new TestDataV1BinarySerializer());
            Assert.IsTrue(m_SM.WriteSlot(1,
                new TestDataV1 { Hp = 77, Name = "Legacy" },
                new SaveSlotMetadata()));

            var migrator = new SaveMigrator();
            migrator.RegisterBinary(1, bytes =>
            {
                ByteBuffer source = ByteBuffer.Acquire(bytes);
                ByteBuffer target = ByteBuffer.Acquire();
                try
                {
                    target.WriteInt(source.ReadInt());
                    target.WriteString(source.ReadString());
                    target.WriteString("migrated");
                    return target.ToArray();
                }
                finally
                {
                    source.Release();
                    target.Release();
                }
            });

            m_SM.SetMigrator(migrator);
            m_SM.SetBinarySerializer(new TestDataV2BinarySerializer());
            m_SM.SetCurrentVersion(2);

            TestDataV2 loaded = m_SM.LoadSlot<TestDataV2>(1);

            Assert.IsNotNull(loaded, "二进制旧存档应走 byte[] 迁移链，而不是把 Base64 交给 JSON 迁移器。 ");
            Assert.AreEqual(77, loaded.Hp);
            Assert.AreEqual("Legacy", loaded.Name);
            Assert.AreEqual("migrated", loaded.NewField);
        }

        // ===== 测试数据类型 =====

        [Serializable]
        public sealed class TestData
        {
            public int Hp;
            public string Name;
        }

        [Serializable]
        public sealed class TestDataV1
        {
            public int Hp;
            public string Name;
        }

        [Serializable]
        public sealed class TestDataV2
        {
            public int Hp;
            public string Name;
            public string NewField;
        }

        // ===== Test helpers =====

        /// <summary>
        /// 极简的 ByteBuffer 二进制序列化器：仅认识 TestData，按 [Hp:int][Name:string] 线序读写。
        /// 模拟代码生成器为业务 DTO 产出的二进制读写。
        /// </summary>
        private sealed class TestDataBinarySerializer : ISaveBinarySerializer
        {
            public void Serialize(object obj, ByteBuffer buffer)
            {
                var d = (TestData)obj;
                buffer.WriteInt(d.Hp);
                buffer.WriteString(d.Name);
            }

            public object Deserialize(Type type, ByteBuffer buffer)
            {
                if (type != typeof(TestData))
                    throw new InvalidOperationException("TestDataBinarySerializer only handles TestData, got " + type);
                int hp = buffer.ReadInt();
                string name = buffer.ReadString();
                return new TestData { Hp = hp, Name = name };
            }
        }

        private sealed class TestDataV1BinarySerializer : ISaveBinarySerializer
        {
            public void Serialize(object obj, ByteBuffer buffer)
            {
                var data = (TestDataV1)obj;
                buffer.WriteInt(data.Hp);
                buffer.WriteString(data.Name);
            }

            public object Deserialize(Type type, ByteBuffer buffer)
            {
                return new TestDataV1
                {
                    Hp = buffer.ReadInt(),
                    Name = buffer.ReadString(),
                };
            }
        }

        private sealed class TestDataV2BinarySerializer : ISaveBinarySerializer
        {
            public void Serialize(object obj, ByteBuffer buffer)
            {
                var data = (TestDataV2)obj;
                buffer.WriteInt(data.Hp);
                buffer.WriteString(data.Name);
                buffer.WriteString(data.NewField);
            }

            public object Deserialize(Type type, ByteBuffer buffer)
            {
                return new TestDataV2
                {
                    Hp = buffer.ReadInt(),
                    Name = buffer.ReadString(),
                    NewField = buffer.ReadString(),
                };
            }
        }

        /// <summary>JsonUtility 委托（信封外层与默认 JSON 路径用）。</summary>
        private sealed class TrivialJsonSerializer : ISaveSerializer
        {
            public string Serialize(object obj) => UnityEngine.JsonUtility.ToJson(obj);
            public T Deserialize<T>(string text) => UnityEngine.JsonUtility.FromJson<T>(text);
            public object Deserialize(Type type, string text) => UnityEngine.JsonUtility.FromJson(text, type);
        }

        /// <summary>异或字节流（仅测试用，不是真加密）。</summary>
        private sealed class XorCrypto : ISaveCryptoHelper
        {
            private readonly byte m_Key;
            public XorCrypto(byte key) { m_Key = key; }
            private byte[] Xor(byte[] x)
            {
                var r = new byte[x.Length];
                for (int i = 0; i < x.Length; i++) r[i] = (byte)(x[i] ^ m_Key);
                return r;
            }
            public byte[] Encrypt(byte[] plain) => Xor(plain);
            public byte[] Decrypt(byte[] cipher) => Xor(cipher);
        }

        /// <summary>内存版 storage：dict slotId → bytes，并能窥探信封内字段（仅明文、未加密时）。</summary>
        private sealed class InMemoryStorage : ISaveStorageHelper
        {
            private readonly Dictionary<int, byte[]> m_Map = new Dictionary<int, byte[]>();

            public bool Exists(int slotId) => m_Map.ContainsKey(slotId);
            public byte[] Read(int slotId) => m_Map.TryGetValue(slotId, out var b) ? b : null;
            public void Write(int slotId, byte[] payload) => m_Map[slotId] = payload;
            public bool Delete(int slotId) => m_Map.Remove(slotId);
            public int[] EnumerateSlotIds()
            {
                var arr = new int[m_Map.Count];
                int i = 0;
                foreach (var k in m_Map.Keys) arr[i++] = k;
                return arr;
            }

            public byte[] PeekPayload(int slotId) => m_Map[slotId];

            /// <summary>从明文 payload 中解出信封 JSON（剥掉 HMAC 框架头：MAGIC(4)+frameVer(1)+hmacLen(1)+hmac）。</summary>
            private string PeekEnvelopeJson(int slotId)
            {
                byte[] framed = m_Map[slotId];
                // 头部：'E''S''V''1' + frameVer(1) + hmacLen(1)
                int headerLen = 4 + 2;
                int macLen = framed[5];
                int dataOffset = headerLen + macLen;
                return Encoding.UTF8.GetString(framed, dataOffset, framed.Length - dataOffset);
            }

            /// <summary>窥探信封 Format 字段（JsonUtility 反序列化；旧存档无此字段时为 0）。</summary>
            public int PeekEnvelopeFormat(int slotId)
            {
                var probe = UnityEngine.JsonUtility.FromJson<EnvelopeProbe>(PeekEnvelopeJson(slotId));
                return probe.Format;
            }

            /// <summary>窥探信封 DataJson 字段。</summary>
            public string PeekEnvelopeDataJson(int slotId)
            {
                var probe = UnityEngine.JsonUtility.FromJson<EnvelopeProbe>(PeekEnvelopeJson(slotId));
                return probe.DataJson;
            }

            /// <summary>信封字段探针（仅取测试关心的字段；SaveEnvelope 本身是 internal）。</summary>
            [Serializable]
            private sealed class EnvelopeProbe
            {
                public int Format;
                public string DataJson;
            }
        }
    }
}
