//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.RedDot;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// RedDotManager 单测。
    /// 覆盖：叶子计数向上传播、内部节点聚合、IsActive、订阅触发新值、清零联动、退订、兄弟独立。
    /// RedDotManager 为 internal，经 InternalsVisibleTo("EjoyFramework.Tests") 可见。
    /// </summary>
    public class RedDotManagerTests
    {
        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void SetLeafCount_PropagatesToParentAggregate()
        {
            var m = new RedDotManager();
            m.SetLeafCount("Mail/System", 2);

            Assert.AreEqual(2, m.GetCount("Mail/System"));
            Assert.AreEqual(2, m.GetCount("Mail"), "父节点应聚合后代叶子计数");
        }

        [Test]
        public void GetCount_InternalNode_IsSumOfDescendantLeaves()
        {
            var m = new RedDotManager();
            m.SetLeafCount("Mail/System", 2);
            m.SetLeafCount("Mail/Friend", 1);
            m.SetLeafCount("Mail/Friend/Pending", 3);

            // Friend = own(1) + Pending(3) = 4
            Assert.AreEqual(4, m.GetCount("Mail/Friend"));
            // Mail = System(2) + Friend(4) = 6
            Assert.AreEqual(6, m.GetCount("Mail"));
        }

        [Test]
        public void GetCount_UnknownPath_ReturnsZero()
        {
            var m = new RedDotManager();
            Assert.AreEqual(0, m.GetCount("Does/Not/Exist"));
        }

        [Test]
        public void IsActive_ReflectsEffectiveCount()
        {
            var m = new RedDotManager();
            Assert.IsFalse(m.IsActive("Shop"));

            m.SetLeafCount("Shop/Gift", 1);
            Assert.IsTrue(m.IsActive("Shop"));
            Assert.IsTrue(m.IsActive("Shop/Gift"));
        }

        [Test]
        public void Subscribe_FiresWithCorrectNewValue_OnChange()
        {
            var m = new RedDotManager();
            var leafValues = new List<int>();
            var parentValues = new List<int>();
            m.Subscribe("Mail/System", v => leafValues.Add(v));
            m.Subscribe("Mail", v => parentValues.Add(v));

            m.SetLeafCount("Mail/System", 5);

            Assert.AreEqual(new[] { 5 }, leafValues.ToArray(), "叶子订阅者收到新有效计数");
            Assert.AreEqual(new[] { 5 }, parentValues.ToArray(), "父订阅者收到聚合后的新值");
        }

        [Test]
        public void Subscribe_DoesNotFire_WhenEffectiveCountUnchanged()
        {
            var m = new RedDotManager();
            m.SetLeafCount("A/X", 3);

            int fires = 0;
            m.Subscribe("A", v => fires++);

            // 重新设置为相同值：A 的聚合不变，不应触发。
            m.SetLeafCount("A/X", 3);
            Assert.AreEqual(0, fires);

            // 真正变化时触发。
            m.SetLeafCount("A/X", 4);
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void SetLeafToZero_ClearsAncestors_AndFires()
        {
            var m = new RedDotManager();
            m.SetLeafCount("Mail/System", 2);

            var parentValues = new List<int>();
            m.Subscribe("Mail", v => parentValues.Add(v));

            m.SetLeafCount("Mail/System", 0);

            Assert.AreEqual(0, m.GetCount("Mail/System"));
            Assert.AreEqual(0, m.GetCount("Mail"), "叶子归零后祖先应清零");
            Assert.IsFalse(m.IsActive("Mail"));
            Assert.AreEqual(new[] { 0 }, parentValues.ToArray(), "清零应触发祖先订阅者携带 0");
        }

        [Test]
        public void Unsubscribe_StopsFiring()
        {
            var m = new RedDotManager();
            int fires = 0;
            System.Action<int> handler = v => fires++;
            m.Subscribe("Mail", handler);

            m.SetLeafCount("Mail/System", 1);
            Assert.AreEqual(1, fires);

            m.Unsubscribe("Mail", handler);
            m.SetLeafCount("Mail/System", 2);
            Assert.AreEqual(1, fires, "退订后不应再触发");
        }

        [Test]
        public void Siblings_AreIndependent()
        {
            var m = new RedDotManager();
            var aFires = new List<int>();
            var bFires = new List<int>();
            m.Subscribe("Root/A", v => aFires.Add(v));
            m.Subscribe("Root/B", v => bFires.Add(v));

            m.SetLeafCount("Root/A", 2);

            Assert.AreEqual(2, m.GetCount("Root/A"));
            Assert.AreEqual(0, m.GetCount("Root/B"), "兄弟节点不受影响");
            Assert.AreEqual(new[] { 2 }, aFires.ToArray());
            CollectionAssert.IsEmpty(bFires, "未变化的兄弟订阅者不应触发");
            Assert.AreEqual(2, m.GetCount("Root"), "父聚合仅含 A");
        }

        [Test]
        public void PathNormalization_TrimsSlashesAndWhitespace()
        {
            var m = new RedDotManager();
            m.SetLeafCount("/Mail/System/", 3);

            Assert.AreEqual(3, m.GetCount("Mail/System"));
            Assert.AreEqual(3, m.GetCount("/Mail/System/"));
            Assert.AreEqual(3, m.GetCount("Mail//System"), "连续 '/' 应被规整");
        }

        [Test]
        public void Clear_ResetsTree()
        {
            var m = new RedDotManager();
            int fires = 0;
            m.Subscribe("Mail", v => fires++);
            m.SetLeafCount("Mail/System", 4);
            Assert.AreEqual(1, fires);

            m.Clear();

            Assert.AreEqual(0, m.GetCount("Mail"));
            Assert.AreEqual(0, m.GetCount("Mail/System"));

            // Clear 移除了订阅者：再次设值不触发旧订阅。
            m.SetLeafCount("Mail/System", 5);
            Assert.AreEqual(1, fires, "Clear 后旧订阅不应再触发");
        }

        // ===== WS5-M1：切片规整、订阅语义与零分配 =====

        [Test]
        public void PathNormalization_TrimsSegmentWhitespace()
        {
            var m = new RedDotManager();
            m.SetLeafCount(" Mail / System ", 2);
            Assert.AreEqual(2, m.GetCount("Mail/System"));
            Assert.AreEqual(2, m.GetCount("Mail"));
            Assert.AreEqual(2, m.GetCount("  Mail  "));
            Assert.AreEqual(0, m.GetCount("///"), "no valid segment");
            Assert.AreEqual(0, m.GetCount(" "));
        }

        [Test]
        public void LongPaths_UseThePooledBuffer()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 60; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append("Segment");
            }

            string deep = sb.ToString();   // 479 字符，超过栈缓冲 256
            var m = new RedDotManager();
            m.SetLeafCount("/" + deep + "/", 3);
            Assert.AreEqual(3, m.GetCount(deep));
            Assert.AreEqual(3, m.GetCount("//" + deep.Replace("/", "//")));
            Assert.AreEqual(3, m.GetCount("Segment"));
        }

        [Test]
        public void Subscribe_SameHandlerTwice_BehavesLikeMulticastDelegate()
        {
            var m = new RedDotManager();
            int fires = 0;
            System.Action<int> handler = v => fires++;
            m.Subscribe("Mail", handler);
            m.Subscribe("Mail", handler);
            m.SetLeafCount("Mail/A", 1);
            Assert.AreEqual(2, fires);

            m.Unsubscribe("Mail", handler);   // 与多播委托 -= 一样只移除一次
            m.SetLeafCount("Mail/A", 2);
            Assert.AreEqual(3, fires);

            m.Unsubscribe("Mail", handler);
            m.SetLeafCount("Mail/A", 3);
            Assert.AreEqual(3, fires);
        }

        [Test]
        public void Subscriber_UnsubscribingDuringCallback_DoesNotSkipOthers()
        {
            var m = new RedDotManager();
            var calls = new List<string>();
            System.Action<int> first = null;
            first = v =>
            {
                calls.Add("first");
                m.Unsubscribe("Mail", first);
            };
            m.Subscribe("Mail", first);
            m.Subscribe("Mail", v => calls.Add("second"));

            m.SetLeafCount("Mail/A", 1);
            CollectionAssert.AreEqual(new[] { "first", "second" }, calls, "the round in progress uses a snapshot");

            m.SetLeafCount("Mail/A", 2);
            CollectionAssert.AreEqual(new[] { "first", "second", "second" }, calls);
        }

        [Test]
        public void ThrowingSubscriber_DoesNotBlockOthers()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                var m = new RedDotManager();
                int fires = 0;
                m.Subscribe("Mail", v => { throw new System.InvalidOperationException("boom"); });
                m.Subscribe("Mail", v => fires++);
                m.SetLeafCount("Mail/A", 1);
                Assert.AreEqual(1, fires);
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void HotPath_IsAllocationFree()
        {
            var m = new RedDotManager();
            m.SetLeafCount("Mail/System", 1);
            m.Subscribe("Mail", v => { });
            int i = 0;
            int sink = 0;
            ZeroAlloc.Assert(() =>
            {
                m.SetLeafCount("Mail/System", ++i & 7);
                sink += m.GetCount("Mail") + m.GetCount("/Mail//System/");
                sink += m.IsActive(" Mail ") ? 1 : 0;
            });
            Assert.GreaterOrEqual(sink, 0);
        }
    }
}
