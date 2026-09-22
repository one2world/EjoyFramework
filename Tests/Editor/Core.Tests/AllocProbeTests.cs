//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 分配探针：记录 Unity Mono 下基础操作的分配事实，作为零 GC 断言的方法论回归。
    ///
    /// 实测结论（2026-09-21，Unity 6000.4.8f1 编辑器 Mono）：
    ///   • [ThreadStatic] 字段访问、Interlocked.Increment(long)、using readonly struct、Stack&lt;T&gt; Push/Pop/foreach、List 索引循环：稳态零分配。
    ///   • 同一代码路径在进程内**首次**执行（JIT / 泛型实例化）会被 Profiler 计为 GC.Alloc，且是进程级一次性、与测试顺序相关。
    ///     因此零分配断言必须先把**同一个委托**执行一遍再 Assert.That(body, Is.Not.AllocatingGCMemory())；
    ///     "在委托外用别的写法预热"不够——预热的必须是委托本身的代码路径。
    /// </summary>
    public sealed class AllocProbeTests
    {
        [ThreadStatic]
        private static Stack<object> ts_Probe;

        private static long s_Counter;

        private readonly struct Scope : IDisposable
        {
            private readonly object m_Payload;
            public Scope(object payload) { m_Payload = payload; }
            public void Dispose() { GC.KeepAlive(m_Payload); }
        }

        [Test]
        public void Probe_ThreadStaticFieldAccess()
        {
            ts_Probe = new Stack<object>();
            Stack<object> warm = ts_Probe;
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++)
                {
                    Stack<object> p = ts_Probe;
                    if (p == null) throw new Exception("null");
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Probe_InterlockedIncrementLong()
        {
            Interlocked.Increment(ref s_Counter);
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++) Interlocked.Increment(ref s_Counter);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Probe_UsingReadonlyStruct()
        {
            object payload = new object();
            using (new Scope(payload)) { }
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++)
                {
                    using (new Scope(payload)) { }
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Probe_StackPushPop_NoForeach()
        {
            var stack = new Stack<object>();
            object item = new object();
            stack.Push(item); stack.Pop();
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++) { stack.Push(item); stack.Pop(); }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Probe_StackForeach_Empty()
        {
            var stack = new Stack<object>();
            foreach (object o in stack) { }
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++)
                {
                    foreach (object o in stack) { if (o == null) throw new Exception(); }
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Probe_StackForeach_OneElement()
        {
            var stack = new Stack<object>();
            stack.Push(new object());
            foreach (object o in stack) { }
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++)
                {
                    foreach (object o in stack) { if (o == null) throw new Exception(); }
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Probe_ListForLoop_OneElement()
        {
            var list = new List<object>();
            list.Add(new object());
            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++)
                {
                    for (int j = 0; j < list.Count; j++) { if (list[j] == null) throw new Exception(); }
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Probe_StackPushPopForeach_WarmLambda_DoesNotAllocate()
        {
            var stack = new Stack<object>();
            object item = new object();
            TestDelegate body = () =>
            {
                for (int i = 0; i < 64; i++)
                {
                    stack.Push(item);
                    foreach (object o in stack) { if (o == null) throw new Exception(); }
                    stack.Pop();
                }
            };
            body();   // 先跑一遍同一委托：JIT 与泛型实例化在测量窗口之外完成
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }
    }
}
