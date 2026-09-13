//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using EjoyFramework.Core;
using EjoyFramework.Core.Save;
using EjoyFramework.Core.Unity;
using NUnit.Framework;

namespace EjoyFramework.Tests
{
    public sealed class SaveCodecTests
    {
        private SaveManager m_Manager;
        private InMemoryStorage m_Storage;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Manager = new SaveManager();
            m_Storage = new InMemoryStorage();
            m_Manager.SetStorageHelper(m_Storage);
            m_Manager.SetSerializer(new JsonUtilitySerializer());
            m_Manager.SetCodec(new NewtonsoftJsonSaveCodec());
            m_Manager.SetCurrentVersion(1);
        }

        [Test]
        public void ESV2_NewtonsoftCodec_RoundTripsDictionaryAndNestedCollections()
        {
            var data = new DictionarySave
            {
                HeroLevels = new Dictionary<string, int> { ["nezha"] = 7, ["houyi"] = 4 },
                Inventories = new Dictionary<string, List<ItemSave>>
                {
                    ["bag"] = new List<ItemSave>
                    {
                        new ItemSave { Id = "coin", Count = 1200 },
                        new ItemSave { Id = "shard", Count = 86 },
                    },
                },
            };

            Assert.IsTrue(m_Manager.WriteSlot(1, data, new SaveSlotMetadata { PlayerName = "守关人" }));
            DictionarySave loaded = m_Manager.LoadSlot<DictionarySave>(1);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(7, loaded.HeroLevels["nezha"]);
            Assert.AreEqual(2, loaded.Inventories["bag"].Count);
            Assert.AreEqual("shard", loaded.Inventories["bag"][1].Id);
        }

        [Test]
        public void ESV2_UsesBinaryEnvelopeAndPersistsCodecId()
        {
            m_Manager.WriteSlot(1, new DictionarySave(), new SaveSlotMetadata());

            byte[] payload = m_Storage.Read(1);
            CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("ESV2"), new ArraySegment<byte>(payload, 0, 4));
            StringAssert.DoesNotContain("DataJson", Encoding.UTF8.GetString(payload));

            SaveSlot[] slots = m_Manager.GetAllSlots();
            Assert.AreEqual(1, slots.Length);
            Assert.AreEqual(NewtonsoftJsonSaveCodec.CodecId, slots[0].CodecId);
        }

        [Test]
        public void ESV2_MetadataCanBeListedWithoutBusinessCodec()
        {
            m_Manager.WriteSlot(3, new DictionarySave(), new SaveSlotMetadata { PlayerName = "Alice", Level = 9 });

            var metadataReader = new SaveManager();
            metadataReader.SetStorageHelper(m_Storage);
            SaveSlot[] slots = metadataReader.GetAllSlots();

            Assert.AreEqual(1, slots.Length);
            Assert.AreEqual("Alice", slots[0].Metadata.PlayerName);
            Assert.AreEqual(9, slots[0].Metadata.Level);
        }

        [Test]
        public void ESV1_RemainsReadableAfterConfiguringESV2Codec()
        {
            var legacyWriter = new SaveManager();
            legacyWriter.SetStorageHelper(m_Storage);
            legacyWriter.SetSerializer(new JsonUtilitySerializer());
            legacyWriter.WriteSlot(5, new LegacySave { Hp = 88 }, new SaveSlotMetadata());

            LegacySave loaded = m_Manager.LoadSlot<LegacySave>(5);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(88, loaded.Hp);
        }

        [Serializable]
        private sealed class DictionarySave
        {
            public Dictionary<string, int> HeroLevels = new Dictionary<string, int>();
            public Dictionary<string, List<ItemSave>> Inventories = new Dictionary<string, List<ItemSave>>();
        }

        [Serializable]
        private sealed class ItemSave
        {
            public string Id;
            public int Count;
        }

        [Serializable]
        private sealed class LegacySave
        {
            public int Hp;
        }

        private sealed class JsonUtilitySerializer : ISaveSerializer
        {
            public string Serialize(object obj) { return UnityEngine.JsonUtility.ToJson(obj); }
            public T Deserialize<T>(string text) { return UnityEngine.JsonUtility.FromJson<T>(text); }
            public object Deserialize(Type type, string text) { return UnityEngine.JsonUtility.FromJson(text, type); }
        }

        private sealed class InMemoryStorage : ISaveStorageHelper
        {
            private readonly Dictionary<int, byte[]> m_Data = new Dictionary<int, byte[]>();

            public bool Exists(int slotId) { return m_Data.ContainsKey(slotId); }
            public byte[] Read(int slotId) { return m_Data.TryGetValue(slotId, out byte[] value) ? value : null; }
            public void Write(int slotId, byte[] payload) { m_Data[slotId] = payload; }
            public bool Delete(int slotId) { return m_Data.Remove(slotId); }
            public int[] EnumerateSlotIds()
            {
                var result = new int[m_Data.Count];
                m_Data.Keys.CopyTo(result, 0);
                return result;
            }
        }
    }
}
