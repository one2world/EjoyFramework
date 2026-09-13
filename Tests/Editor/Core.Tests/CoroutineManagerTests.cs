//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Coroutines;
using EjoyFramework.Core.Unity.Resource;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// CoroutineManager 单元测试。
    /// 用一个手动驱动的 FakeHelper（每次 Tick() 推进所有协程一次）模拟 Unity StartCoroutine 的行为。
    /// </summary>
    public class CoroutineManagerTests
    {
        private sealed class FakeHelper : ICoroutineHelper, ICoroutineHelperStatus
        {
            private readonly List<RoutineState> m_Routines = new List<RoutineState>();
            public bool IsAvailable { get; set; } = true;
            public int StopCount { get; private set; }
            public int RoutineCount { get { return m_Routines.Count; } }
            private sealed class RoutineState
            {
                public IEnumerator Routine;
                public bool Done;
            }

            public object StartCoroutine(IEnumerator routine)
            {
                var s = new RoutineState { Routine = routine };
                m_Routines.Add(s);
                return s;
            }

            public void StopCoroutine(object cookie)
            {
                StopCount++;
                if (cookie is RoutineState s) { s.Done = true; m_Routines.Remove(s); }
            }

            /// <summary>推进所有协程一帧。</summary>
            public void Tick()
            {
                for (int i = m_Routines.Count - 1; i >= 0; i--)
                {
                    var s = m_Routines[i];
                    if (s.Done) { m_Routines.RemoveAt(i); continue; }
                    bool more;
                    try { more = s.Routine.MoveNext(); }
                    catch { more = false; }
                    if (!more) { s.Done = true; m_Routines.RemoveAt(i); }
                }
            }
        }

        private static ICoroutineManager NewManager()
        {
            var t = typeof(ICoroutineManager).Assembly.GetType("EjoyFramework.Core.Coroutines.CoroutineManager");
            return (ICoroutineManager)Activator.CreateInstance(t, true);
        }

        private static IEnumerator CountTo(int n, Action onStep = null)
        {
            for (int i = 0; i < n; i++)
            {
                onStep?.Invoke();
                yield return null;
            }
        }

        private static IEnumerator Forever() { while (true) yield return null; }

        [Test]
        public void Run_RegistersAndReportsInSnapshot()
        {
            var helper = new FakeHelper();
            var cm = NewManager();
            cm.SetHelper(helper);

            var owner = new object();
            var h = cm.Run(CountTo(3), "test/loop", owner);

            Assert.IsTrue(h.IsValid);
            Assert.AreEqual(1, cm.RunningCount);
            var snap = cm.GetAllRunning();
            Assert.AreEqual(1, snap.Length);
            Assert.AreEqual("test/loop", snap[0].Tag);
        }

        [Test]
        public void Routine_AutoUnregistersOnCompletion()
        {
            var helper = new FakeHelper();
            var cm = NewManager();
            cm.SetHelper(helper);

            int steps = 0;
            cm.Run(CountTo(3, () => steps++), "auto/done");
            Assert.AreEqual(1, cm.RunningCount);

            helper.Tick(); // wrapper: try { MoveNext } yield → step 1
            helper.Tick();
            helper.Tick();
            helper.Tick(); // 第 4 次 MoveNext: inner 完成 → wrapper yield break → finally 移除
            // 给 wrapper 多余一帧以让 finally 跑完
            helper.Tick();

            Assert.AreEqual(0, cm.RunningCount, "routine should auto-unregister after completion");
            Assert.AreEqual(3, steps);
        }

        [Test]
        public void Stop_RemovesFromRegistry()
        {
            var helper = new FakeHelper();
            var cm = NewManager();
            cm.SetHelper(helper);

            var h = cm.Run(Forever(), "infinite");
            Assert.AreEqual(1, cm.RunningCount);

            Assert.IsTrue(cm.Stop(h));
            Assert.AreEqual(0, cm.RunningCount);
            Assert.IsFalse(cm.Stop(h), "Second stop should be no-op");
        }

        [Test]
        public void StopByOwner_StopsAllRoutinesOfThatOwner()
        {
            var helper = new FakeHelper();
            var cm = NewManager();
            cm.SetHelper(helper);

            var ownerA = new object();
            var ownerB = new object();
            cm.Run(Forever(), "a1", ownerA);
            cm.Run(Forever(), "a2", ownerA);
            cm.Run(Forever(), "b1", ownerB);
            Assert.AreEqual(3, cm.RunningCount);

            int stopped = cm.StopByOwner(ownerA);
            Assert.AreEqual(2, stopped);
            Assert.AreEqual(1, cm.RunningCount);
            Assert.AreEqual("b1", cm.GetAllRunning()[0].Tag);
        }

        [Test]
        public void StopByTag_StopsMatchingTags()
        {
            var helper = new FakeHelper();
            var cm = NewManager();
            cm.SetHelper(helper);

            cm.Run(Forever(), "load");
            cm.Run(Forever(), "load");
            cm.Run(Forever(), "other");
            Assert.AreEqual(2, cm.StopByTag("load"));
            Assert.AreEqual(1, cm.RunningCount);
        }

        [Test]
        public void StopAll_ClearsRegistry()
        {
            var helper = new FakeHelper();
            var cm = NewManager();
            cm.SetHelper(helper);

            cm.Run(Forever(), "1");
            cm.Run(Forever(), "2");
            cm.Run(Forever(), "3");
            int stopped = cm.StopAll();
            Assert.AreEqual(3, stopped);
            Assert.AreEqual(0, cm.RunningCount);
        }

        [Test]
        public void Run_WithoutHelper_Throws()
        {
            var cm = NewManager();
            Assert.Throws<FrameworkException>(() => cm.Run(Forever(), "nope"));
        }

        [Test]
        public void Run_NullRoutine_Throws()
        {
            var cm = NewManager();
            cm.SetHelper(new FakeHelper());
            Assert.Throws<FrameworkException>(() => cm.Run(null, "nope"));
        }

        [Test]
        public void HasHelper_TrueAfterSetHelper_FalseBefore()
        {
            var cm = NewManager();
            Assert.IsFalse(cm.HasHelper, "Fresh manager has no helper yet.");
            cm.SetHelper(new FakeHelper());
            Assert.IsTrue(cm.HasHelper, "SetHelper should flip HasHelper to true.");
        }

        [Test]
        public void HasHelper_FalseWhenHelperReportsUnavailable()
        {
            var helper = new FakeHelper();
            var cm = NewManager();
            cm.SetHelper(helper);

            helper.IsAvailable = false;

            Assert.IsFalse(cm.HasHelper, "A helper whose Unity host was destroyed must not remain logically available.");
        }

        [Test]
        public void SetHelper_ReplacingUnavailableHelper_ClearsOldRunningEntries()
        {
            var oldHelper = new FakeHelper();
            var replacement = new FakeHelper();
            var cm = NewManager();
            cm.SetHelper(oldHelper);
            cm.Run(Forever(), "old-host");
            oldHelper.IsAvailable = false;

            cm.SetHelper(replacement);

            Assert.AreEqual(1, oldHelper.StopCount);
            Assert.AreEqual(0, cm.RunningCount, "Cookies from the destroyed host must not survive helper replacement.");
            cm.Run(Forever(), "new-host");
            Assert.AreEqual(1, replacement.RoutineCount);
        }

        [Test]
        public void AssetBundleLoader_Shutdown_StopsOwnedCoroutines()
        {
            var cm = NewManager();
            cm.SetHelper(new FakeHelper());
            var loader = new AssetBundleLoader(cm);
            cm.Run(Forever(), "AssetBundleLoader", loader);

            loader.Shutdown();

            Assert.AreEqual(0, cm.RunningCount, "Shutdown must stop load and delayed-unload routines before clearing loader state.");
        }

        [Test]
        public void Run_WithoutHelper_ErrorMentionsCoroutineComponent()
        {
            // Lock in the actionable error contract — the message must guide users to the fix
            // (add CoroutineComponent to the scene's framework GameObject) rather than just saying
            // "helper is not set." Regressions here cause hours of head-scratching in production.
            var cm = NewManager();
            var ex = Assert.Throws<FrameworkException>(() => cm.Run(Forever(), "nope"));
            StringAssert.Contains("CoroutineComponent", ex.Message);
            StringAssert.Contains("EjoyFramework/Core/Coroutine", ex.Message);
        }
    }
}
