//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using EjoyFramework.Core.Save;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// SaveManager 单测（Phase 18）。
    /// 覆盖：Write/Load 往返 / 多 slot / 加密 / migration / CRC 损坏检测 /
    ///       SaveFailed / SaveCorrupted 事件 / DeleteSlot / DeleteAllSlots /
    ///       未注入 helper 抛错。
    /// 使用 in-memory storage helper + 直白 JSON serializer，无 Unity 依赖。
    /// </summary>
    public class SaveManagerTests
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
            m_SM.SetSerializer(new TrivialJsonSerializer());
            m_SM.SetCurrentVersion(1);
        }

        [Test]
        public void Write_Then_Load_RoundTrip()
        {
            var data = new TestData { Hp = 100, Name = "Hero" };
            var meta = new SaveSlotMetadata { PlayerName = "Alice", Level = 5 };
            Assert.IsTrue(m_SM.WriteSlot(1, data, meta));

            var loaded = m_SM.LoadSlot<TestData>(1);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(100, loaded.Hp);
            Assert.AreEqual("Hero", loaded.Name);
        }

        [Test]
        public void HasSlot_True_AfterWrite_False_AfterDelete()
        {
            Assert.IsFalse(m_SM.HasSlot(1));
            m_SM.WriteSlot(1, new TestData(), new SaveSlotMetadata());
            Assert.IsTrue(m_SM.HasSlot(1));
            Assert.IsTrue(m_SM.DeleteSlot(1));
            Assert.IsFalse(m_SM.HasSlot(1));
        }

        [Test]
        public void GetAllSlots_ReturnsMetadata_Without_BusinessData()
        {
            m_SM.WriteSlot(1, new TestData { Hp = 1 }, new SaveSlotMetadata { PlayerName = "A" });
            m_SM.WriteSlot(2, new TestData { Hp = 2 }, new SaveSlotMetadata { PlayerName = "B" });

            var slots = m_SM.GetAllSlots();
            Assert.AreEqual(2, slots.Length);
            // metadata 应该可读
            var names = new List<string>();
            foreach (var s in slots) names.Add(s.Metadata.PlayerName);
            CollectionAssert.Contains(names, "A");
            CollectionAssert.Contains(names, "B");
        }

        [Test]
        public void DeleteAllSlots_ClearsAll()
        {
            m_SM.WriteSlot(1, new TestData(), new SaveSlotMetadata());
            m_SM.WriteSlot(2, new TestData(), new SaveSlotMetadata());
            m_SM.WriteSlot(3, new TestData(), new SaveSlotMetadata());
            Assert.AreEqual(3, m_SM.SlotCount);
            m_SM.DeleteAllSlots();
            Assert.AreEqual(0, m_SM.SlotCount);
        }

        [Test]
        public void Encryption_RoundTrip_ProducesScrambledBytes()
        {
            m_SM.SetCryptoHelper(new XorCrypto(0x5A));
            var data = new TestData { Hp = 42, Name = "secret" };
            m_SM.WriteSlot(1, data, new SaveSlotMetadata());

            var payload = m_Storage.PeekPayload(1);
            string plaintext = Encoding.UTF8.GetString(payload);
            StringAssert.DoesNotContain("secret", plaintext, "明文'secret'不应出现在加密 payload");

            var loaded = m_SM.LoadSlot<TestData>(1);
            Assert.AreEqual("secret", loaded.Name);
        }

        [Test]
        public void CorruptedPayload_TriggersSaveCorrupted_ReturnsDefault()
        {
            m_SM.WriteSlot(1, new TestData(), new SaveSlotMetadata());
            // 模拟数据损坏：直接改 storage 字节
            m_Storage.CorruptPayload(1);

            string corruptedError = null;
            m_SM.SaveCorrupted += (slot, err) => corruptedError = err;
            var loaded = m_SM.LoadSlot<TestData>(1);

            Assert.IsNull(loaded);
            Assert.IsNotNull(corruptedError);
        }

        [Test]
        public void CrcMismatch_TriggersSaveCorrupted()
        {
            m_SM.WriteSlot(1, new TestData { Hp = 42 }, new SaveSlotMetadata());
            // 修改 envelope 内部 DataJson 但不更新 Crc → 触发 CRC mismatch
            m_Storage.TamperJsonData(1);

            string err = null;
            m_SM.SaveCorrupted += (slot, msg) => err = msg;
            var loaded = m_SM.LoadSlot<TestData>(1);
            Assert.IsNull(loaded);
            Assert.IsNotNull(err);
            StringAssert.Contains("CRC", err);
        }

        [Test]
        public void Migration_V1ToV2_AppliedOnLoad()
        {
            // 写 v1 数据
            m_SM.SetCurrentVersion(1);
            m_SM.WriteSlot(1, new TestDataV1 { OldName = "Alice", OldHp = 99 }, new SaveSlotMetadata());

            // 切到 v2 + 注册 migration
            m_SM.SetCurrentVersion(2);
            var mig = new SaveMigrator();
            mig.Register(1, json =>
            {
                var v1 = UnityEngine.JsonUtility.FromJson<TestDataV1>(json);
                var v2 = new TestDataV2 { Name = v1.OldName, Hp = v1.OldHp, NewField = "migrated" };
                return UnityEngine.JsonUtility.ToJson(v2);
            });
            m_SM.SetMigrator(mig);

            var loaded = m_SM.LoadSlot<TestDataV2>(1);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("Alice", loaded.Name);
            Assert.AreEqual(99, loaded.Hp);
            Assert.AreEqual("migrated", loaded.NewField);
        }

        [Test]
        public void Migration_MissingStep_TriggersCorrupted()
        {
            m_SM.SetCurrentVersion(1);
            m_SM.WriteSlot(1, new TestData(), new SaveSlotMetadata());
            m_SM.SetCurrentVersion(3);
            m_SM.SetMigrator(new SaveMigrator());   // 没注册任何 step

            string err = null;
            m_SM.SaveCorrupted += (s, e) => err = e;
            var loaded = m_SM.LoadSlot<TestData>(1);
            Assert.IsNull(loaded);
            Assert.IsNotNull(err);
        }

        [Test]
        public void WriteFailed_FromStorageException_FiresSaveFailed()
        {
            m_Storage.ThrowOnWrite = true;
            int failedSlot = -1;
            string failedReason = null;
            m_SM.SaveFailed += (slot, err) => { failedSlot = slot; failedReason = err; };
            Assert.IsFalse(m_SM.WriteSlot(7, new TestData(), new SaveSlotMetadata()));
            Assert.AreEqual(7, failedSlot);
            Assert.IsNotNull(failedReason);
        }

        [Test]
        public void WithoutSerializer_Throws()
        {
            var bare = new SaveManager();
            bare.SetStorageHelper(new InMemoryStorage());
            Assert.Throws<FrameworkException>(() => bare.WriteSlot(1, new TestData(), new SaveSlotMetadata()));
        }

        [Test]
        public void LoadMissingSlot_ReturnsDefault_NoEvent()
        {
            int corruptCalls = 0;
            m_SM.SaveCorrupted += (s, e) => corruptCalls++;
            var loaded = m_SM.LoadSlot<TestData>(999);
            Assert.IsNull(loaded);
            Assert.AreEqual(0, corruptCalls, "不存在的 slot 不应触发 corrupted 事件");
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
            public string OldName;
            public int OldHp;
        }

        [Serializable]
        public sealed class TestDataV2
        {
            public string Name;
            public int Hp;
            public string NewField;
        }

        // ===== Test helpers =====

        /// <summary>JsonUtility 委托。</summary>
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

        /// <summary>内存版 storage：dict slotId → bytes。</summary>
        private sealed class InMemoryStorage : ISaveStorageHelper
        {
            private readonly Dictionary<int, byte[]> m_Map = new Dictionary<int, byte[]>();
            public bool ThrowOnWrite;

            public bool Exists(int slotId) => m_Map.ContainsKey(slotId);
            public byte[] Read(int slotId) => m_Map.TryGetValue(slotId, out var b) ? b : null;
            public void Write(int slotId, byte[] payload)
            {
                if (ThrowOnWrite) throw new InvalidOperationException("simulated disk full");
                m_Map[slotId] = payload;
            }
            public bool Delete(int slotId) => m_Map.Remove(slotId);
            public int[] EnumerateSlotIds()
            {
                var arr = new int[m_Map.Count];
                int i = 0;
                foreach (var k in m_Map.Keys) arr[i++] = k;
                return arr;
            }
            public byte[] PeekPayload(int slotId) => m_Map[slotId];
            public void CorruptPayload(int slotId)
            {
                var b = m_Map[slotId];
                // 把整个 payload 替换成完全无法解析的字节
                for (int i = 0; i < b.Length; i++) b[i] = 0xFF;
            }
            public void TamperJsonData(int slotId)
            {
                // 在明文模式下篡改 envelope 内 dataJson 字段（dataJson 在外层 JSON 里是 escaped string，
                // 比如 "DataJson":"{\"Hp\":42,...}"，所以查 escaped 形式）
                var b = m_Map[slotId];
                string text = Encoding.UTF8.GetString(b);
                string tampered = text.Replace("\\\"Hp\\\":42", "\\\"Hp\\\":999");
                if (tampered == text)
                {
                    throw new InvalidOperationException("TamperJsonData: didn't find pattern; payload format changed?");
                }
                m_Map[slotId] = Encoding.UTF8.GetBytes(tampered);
            }
        }
    }
}
