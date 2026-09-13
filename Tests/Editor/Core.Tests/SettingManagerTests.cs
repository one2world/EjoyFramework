//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Setting;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// SettingManager 单测（Phase 17）。
    /// 覆盖：基础读写 / 类型互通 (bool/int/float/string/object) / Save 成功失败路径 /
    /// SaveFailed 事件 / HasSetting / Remove / RemoveAll / Helper-less fallback cache。
    /// </summary>
    public class SettingManagerTests
    {
        private SettingManager m_SM;
        private MockSettingHelper m_Helper;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            // SetObject<T>/GetObject<T> 走 Utility.Json，需要 helper 注入
            Utility.Json.SetJsonHelper(new TestJsonHelper());
            m_SM = new SettingManager();
            m_Helper = new MockSettingHelper();
            m_SM.SetHelper(m_Helper);
        }

        [TearDown]
        public void TearDown()
        {
            m_SM.Shutdown();
        }

        [Test]
        public void SetString_GetString_RoundTrip()
        {
            m_SM.SetString("nickname", "Alice");
            Assert.AreEqual("Alice", m_SM.GetString("nickname"));
        }

        [Test]
        public void SetInt_GetInt_UsesInvariantCulture()
        {
            m_SM.SetInt("score", 12345);
            Assert.AreEqual(12345, m_SM.GetInt("score"));
        }

        [Test]
        public void SetFloat_GetFloat_PreservesValue()
        {
            m_SM.SetFloat("volume", 0.75f);
            Assert.AreEqual(0.75f, m_SM.GetFloat("volume"), 0.0001f);
        }

        [Test]
        public void SetBool_GetBool_RoundTrip()
        {
            m_SM.SetBool("muted", true);
            Assert.IsTrue(m_SM.GetBool("muted"));
            m_SM.SetBool("muted", false);
            Assert.IsFalse(m_SM.GetBool("muted"));
        }

        [Test]
        public void GetWithDefault_ReturnsDefault_WhenAbsent()
        {
            Assert.AreEqual(42, m_SM.GetInt("missing", 42));
            Assert.AreEqual("fallback", m_SM.GetString("missing", "fallback"));
            Assert.AreEqual(3.14f, m_SM.GetFloat("missing", 3.14f), 0.0001f);
            Assert.IsTrue(m_SM.GetBool("missing", true));
        }

        [Test]
        public void HasSetting_True_AfterSet_False_AfterRemove()
        {
            m_SM.SetString("k", "v");
            Assert.IsTrue(m_SM.HasSetting("k"));
            Assert.IsTrue(m_SM.RemoveSetting("k"));
            Assert.IsFalse(m_SM.HasSetting("k"));
        }

        [Test]
        public void RemoveAllSettings_ClearsHelperBackedStore()
        {
            m_SM.SetString("a", "1");
            m_SM.SetString("b", "2");
            m_SM.RemoveAllSettings();
            Assert.IsFalse(m_SM.HasSetting("a"));
            Assert.IsFalse(m_SM.HasSetting("b"));
        }

        [Test]
        public void Save_Success_ReturnsTrue_NoEvent()
        {
            int failureFires = 0;
            m_SM.SaveFailed += _ => failureFires++;

            m_SM.SetString("k", "v");
            Assert.IsTrue(m_SM.Save());
            Assert.AreEqual(0, failureFires);
            Assert.AreEqual(1, m_Helper.SaveCalls);
        }

        [Test]
        public void Save_HelperThrows_ReturnsFalse_TriggersSaveFailed()
        {
            m_Helper.ThrowOnSave = true;
            string capturedError = null;
            m_SM.SaveFailed += err => capturedError = err;

            Assert.IsFalse(m_SM.Save());
            Assert.IsNotNull(capturedError);
            StringAssert.Contains("disk full", capturedError);
        }

        [Test]
        public void Save_WithoutHelper_ReturnsTrue_NoOp()
        {
            var bare = new SettingManager();
            // 没注入 Helper
            Assert.IsTrue(bare.Save());
        }

        [Test]
        public void HelperLess_Cache_FallbackForSetGetRemove()
        {
            // 不注入 helper → 使用内存 cache
            var bare = new SettingManager();
            Framework.MarkMainThread();
            bare.SetString("memOnly", "value");
            Assert.IsTrue(bare.HasSetting("memOnly"));
            Assert.AreEqual("value", bare.GetString("memOnly"));
            Assert.IsTrue(bare.RemoveSetting("memOnly"));
            Assert.IsFalse(bare.HasSetting("memOnly"));
        }

        [Test]
        public void Object_RoundTrip_ViaJson()
        {
            // SetObject<T>/GetObject<T> 走 Utility.Json 序列化
            // Helper 后端只看到 JSON string，依然能往返
            var payload = new TestPayload { Id = 7, Name = "Hero" };
            m_SM.SetObject("p", payload);

            var back = m_SM.GetObject<TestPayload>("p");
            Assert.IsNotNull(back);
            Assert.AreEqual(7, back.Id);
            Assert.AreEqual("Hero", back.Name);
        }

        [Test]
        public void Count_Tracks_Backend_Items()
        {
            Assert.AreEqual(0, m_SM.Count);
            m_SM.SetString("a", "1");
            m_SM.SetString("b", "2");
            Assert.AreEqual(2, m_SM.Count);
            m_SM.RemoveSetting("a");
            Assert.AreEqual(1, m_SM.Count);
        }

        [System.Serializable]
        private sealed class TestPayload
        {
            public int Id;
            public string Name;
        }

        /// <summary>简易 JsonHelper：UnityEngine.JsonUtility 委托。</summary>
        private sealed class TestJsonHelper : Utility.Json.IJsonHelper
        {
            public string ToJson(object obj) => UnityEngine.JsonUtility.ToJson(obj);
            public T ToObject<T>(string json) => UnityEngine.JsonUtility.FromJson<T>(json);
            public object ToObject(Type objectType, string json) => UnityEngine.JsonUtility.FromJson(json, objectType);
        }

        /// <summary>Mock ISettingHelper：内存 Dictionary 后端 + 可配置抛 Save 异常。</summary>
        private sealed class MockSettingHelper : ISettingHelper
        {
            private readonly Dictionary<string, string> m_Store = new Dictionary<string, string>();
            public int SaveCalls;
            public bool ThrowOnSave;

            public int Count => m_Store.Count;
            public bool Has(string name) => m_Store.ContainsKey(name);
            public bool TryGet(string name, out string value) => m_Store.TryGetValue(name, out value);
            public void Set(string name, string value) => m_Store[name] = value;
            public bool Remove(string name) => m_Store.Remove(name);
            public void RemoveAll() => m_Store.Clear();

            public void Save()
            {
                SaveCalls++;
                if (ThrowOnSave) throw new InvalidOperationException("disk full");
            }

            public IEnumerable<string> Keys() => m_Store.Keys;
        }
    }
}
