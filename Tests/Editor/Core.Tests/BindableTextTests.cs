//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core;
using EjoyFramework.Core.UI.Mvvm;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;

namespace EjoyFramework.Tests
{
    public class BindableTextTests
    {
        private sealed class Vm : BindableObject
        {
            public Vm()
            {
                Hp = CreateText("Hp");
            }

            public BindableText Hp { get; }
        }

        [Test]
        public void Set_String_UpdatesContentAndNotifiesOnce()
        {
            var text = new BindableText();
            int count = 0;
            string lastName = null;
            text.PropertyChanged += (sender, name) => { count++; lastName = name; };

            Assert.IsTrue(text.Set("Hello"));
            Assert.AreEqual(5, text.Length);
            Assert.AreEqual("Hello", text.ToString());
            Assert.AreEqual(1, count);
            Assert.AreEqual(BindableText.ValuePropertyName, lastName);

            // 内容未变不应再通知，否则 TMP 每帧白白重排版。
            Assert.IsFalse(text.Set("Hello"));
            Assert.AreEqual(1, count);
        }

        [Test]
        public void SetValue_Int_DoesNotNotifyWhenUnchanged()
        {
            var text = new BindableText();
            int count = 0;
            text.PropertyChanged += (sender, name) => count++;

            Assert.IsTrue(text.SetValue(120));
            Assert.AreEqual("120", text.ToString());
            Assert.AreEqual(1, count);

            Assert.IsFalse(text.SetValue(120));
            Assert.AreEqual(1, count);

            Assert.IsTrue(text.SetValue(99));
            Assert.AreEqual("99", text.ToString());
            Assert.AreEqual(2, text.Length);
            Assert.AreEqual(2, count);
        }

        [Test]
        public void SetValue_HonorsFormatAndFloat()
        {
            var text = new BindableText();
            text.SetValue(7, "D3");
            Assert.AreEqual("007", text.ToString());

            text.SetValue(1.5f, "F1");
            Assert.AreEqual("1.5", text.ToString());

            text.SetValue(2.25, "F2");
            Assert.AreEqual("2.25", text.ToString());
        }

        [Test]
        public void Set_TempText_CopiesContentWithoutString()
        {
            var text = new BindableText();
            using (var t = TempText.Rent(32))
            {
                t.Append("HP ").Append(30).Append('/').Append(100);
                Assert.IsTrue(text.Set(t));
            }

            Assert.AreEqual("HP 30/100", text.ToString());
            Assert.AreEqual(9, text.Length);
        }

        [Test]
        public void Set_LongerThanCapacity_GrowsAndKeepsContent()
        {
            var text = new BindableText(4);
            string longValue = new string('x', 300);
            Assert.IsTrue(text.Set(longValue));
            Assert.AreEqual(300, text.Length);
            Assert.AreEqual(longValue, text.ToString());
            Assert.GreaterOrEqual(text.Buffer.Length, 300);
        }

        [Test]
        public void Buffer_ReflectsContentForDirectDownload()
        {
            var text = new BindableText();
            text.Set("AB");

            // UI 层直通路径读取的正是这两个值。
            Assert.AreEqual('A', text.Buffer[0]);
            Assert.AreEqual('B', text.Buffer[1]);
            Assert.AreEqual(2, text.Length);
        }

        [Test]
        public void Buffer_IsNeverNullBeforeFirstWrite()
        {
            var text = new BindableText();
            Assert.IsNotNull(text.Buffer);
            Assert.AreEqual(0, text.Length);
            Assert.IsTrue(text.IsEmpty);
            Assert.AreEqual(string.Empty, text.ToString());
        }

        [Test]
        public void Clear_NotifiesOnlyWhenNotEmpty()
        {
            var text = new BindableText();
            int count = 0;
            text.PropertyChanged += (sender, name) => count++;

            Assert.IsFalse(text.Clear());
            Assert.AreEqual(0, count);

            text.Set("abc");
            Assert.IsTrue(text.Clear());
            Assert.IsTrue(text.IsEmpty);
            Assert.AreEqual(2, count);
        }

        [Test]
        public void ForceNotify_RaisesEvenWhenUnchanged()
        {
            var text = new BindableText();
            int count = 0;
            text.PropertyChanged += (sender, name) => count++;

            text.ForceNotify();
            Assert.AreEqual(1, count);
        }

        [Test]
        public void CreateText_ForwardsChangeAsOwnerPropertyName()
        {
            var vm = new Vm();
            string lastName = null;
            int count = 0;
            ((IBindable)vm).PropertyChanged += (sender, name) => { count++; lastName = name; };

            vm.Hp.SetValue(42);
            Assert.AreEqual(1, count);
            Assert.AreEqual("Hp", lastName);
            Assert.AreEqual("42", vm.Hp.ToString());

            vm.Hp.SetValue(42);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void SetValue_Int_DoesNotAllocateGcMemoryInSteadyState()
        {
            var text = new BindableText(64);

            // 订阅一个空 handler，把 FastEvent 的通知链路也纳入零分配断言——真实场景下总有 binder 在听。
            text.PropertyChanged += (sender, name) => { };

            TestDelegate body = () =>
            {
                for (int i = 0; i < 64; i++)
                {
                    text.SetValue(i);
                }
            };

            // 预热必须作用于"同一个委托实例"：首次调用会触发该 lambda 自身的 JIT 与缓冲/事件路径的
            // 初始化分配。预热方法体里另写一遍的等价循环不起作用——那是另一处 IL。
            for (int i = 0; i < 8; i++)
            {
                body();
            }

            Assert.That(body, NUnit.Framework.Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void SetValue_AfterGrow_StillComparesTailCorrectly()
        {
            // 让 Length 逼近容量，逼 TryFormat 在尾部空间不足时走 Grow，再验证 Commit 的尾部比较与前移没有串位。
            var text = new BindableText(64);
            text.Set(new string('y', 60));

            int count = 0;
            text.PropertyChanged += (sender, name) => count++;

            Assert.IsTrue(text.SetValue(1234567890123L));
            Assert.AreEqual("1234567890123", text.ToString());
            Assert.AreEqual(13, text.Length);
            Assert.AreEqual(1, count);

            // 扩容后旧内容不得残留影响比较：同值不通知，异值通知。
            Assert.IsFalse(text.SetValue(1234567890123L));
            Assert.AreEqual(1, count);
            Assert.IsTrue(text.SetValue(1234567890124L));
            Assert.AreEqual("1234567890124", text.ToString());
            Assert.AreEqual(2, count);
        }

        [Test]
        public void Set_ShrinkingAcrossTiers_KeepsContentExact()
        {
            // 跨档位收缩：缓冲仍是扩容后的大块，Length 必须正确收窄，尾部残字不得泄漏到内容里。
            var text = new BindableText();
            text.Set(new string('a', 500));
            Assert.AreEqual(500, text.Length);

            Assert.IsTrue(text.Set("ab"));
            Assert.AreEqual(2, text.Length);
            Assert.AreEqual("ab", text.ToString());
            Assert.IsTrue(text.AsSpan().SequenceEqual("ab".AsSpan()));

            Assert.IsFalse(text.Set("ab"));

            Assert.IsTrue(text.SetValue(5));
            Assert.AreEqual("5", text.ToString());
            Assert.AreEqual(1, text.Length);
        }

        [Test]
        public void AsSpan_MatchesContent()
        {
            var text = new BindableText();
            text.Set("span");
            Assert.IsTrue(text.AsSpan().SequenceEqual("span".AsSpan()));
            Assert.IsTrue(text.Equals("span".AsSpan()));
            Assert.IsFalse(text.Equals("spa".AsSpan()));
        }

        // ===== WS5-M1：泛型 SetValue（经 TextFormatter，不装箱） =====

        private enum Rank
        {
            Bronze,
            Silver,
            Gold,
        }

        [Test]
        public void SetValue_Generic_FormatsEnumsAndOtherTypes()
        {
            var text = new BindableText();
            int count = 0;
            text.PropertyChanged += (sender, name) => count++;

            Assert.IsTrue(text.SetValue(Rank.Gold));
            Assert.AreEqual("Gold", text.ToString());
            Assert.IsFalse(text.SetValue(Rank.Gold), "unchanged content does not notify");
            Assert.AreEqual(1, count);

            Assert.IsTrue(text.SetValue(true));
            Assert.AreEqual("True", text.ToString());
            Assert.IsTrue(text.SetValue(4000000000u));
            Assert.AreEqual("4000000000", text.ToString());
            Assert.IsTrue(text.SetValue(Rank.Silver, "D"));
            Assert.AreEqual("1", text.ToString());
            Assert.AreEqual(4, count);
        }

        [Test]
        public void SetValue_Generic_IsAllocationFreeInSteadyState()
        {
            var text = new BindableText(64);
            text.PropertyChanged += (sender, name) => { };
            int i = 0;
            ZeroAlloc.Assert(() =>
            {
                text.SetValue((Rank)(i % 3));
                text.SetValue(i * 0.25, "F2");
                i++;
            });
        }
    }
}
