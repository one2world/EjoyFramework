//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using EjoyFramework.Core.Performance;
using NUnit.Framework;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// ObjectTracker（弱引用对象类型统计 / 泄漏检测诊断器）单测。
    /// 覆盖：关闭即空操作、普通对象存活统计、Unity 对象原生存活/已毁(悬垂引用)判定、创建堆栈捕获、
    /// 按类型聚合、MaxEntries 上限、Clear、以及 GC 回收后 Sweep。
    /// 注意：ObjectTracker 为静态，[SetUp]/[TearDown] 必须复位全局状态以隔离用例。
    /// </summary>
    public class ObjectTrackerTests
    {
        // 通过 Func 控制分类的假探针：避免依赖真实 UnityEngine.Object（核心层测试）。
        private sealed class FakeInspector : IUnityObjectInspector
        {
            public Func<Type, bool> UnityType = _ => false;
            public Func<object, bool> NativeAlive = _ => true;
            public bool IsUnityObjectType(Type type) => UnityType(type);
            public bool IsNativeAlive(object obj) => NativeAlive(obj);
        }

        private sealed class FakeUnityObject { }   // 充当「Unity 对象」类型的替身
        private sealed class PlainModel { }        // 普通对象

        [SetUp]
        public void SetUp()
        {
            ObjectTracker.Clear();
            ObjectTracker.Enabled = false;
            ObjectTracker.CaptureStackByDefault = false;
            ObjectTracker.MaxEntries = 100000;
            ObjectTracker.SetUnityObjectInspector(null);
        }

        [TearDown]
        public void TearDown()
        {
            ObjectTracker.Clear();
            ObjectTracker.Enabled = false;
            ObjectTracker.CaptureStackByDefault = false;
            ObjectTracker.MaxEntries = 100000;
            ObjectTracker.SetUnityObjectInspector(null);
        }

        [Test]
        public void Track_Disabled_IsNoOp()
        {
            ObjectTracker.Enabled = false;
            object o = new PlainModel();
            ObjectTracker.Track(o);
            Assert.AreEqual(0, ObjectTracker.TrackedCount);
            Assert.AreEqual(0, ObjectTracker.Snapshot().Length);
        }

        [Test]
        public void Track_PlainObject_ShowsAlive()
        {
            ObjectTracker.Enabled = true;
            object o = new PlainModel();
            ObjectTracker.Track(o, "p1");

            ObjectTrackerStats[] stats = ObjectTracker.Snapshot();
            Assert.AreEqual(1, stats.Length);
            Assert.AreEqual(typeof(PlainModel).FullName, stats[0].TypeName);
            Assert.IsFalse(stats[0].IsUnityObject);
            Assert.AreEqual(1, stats[0].Alive);
            Assert.AreEqual(0, stats[0].UnityNativeDestroyed);

            ObjectTrackerRecord[] recs = ObjectTracker.Inspect(typeof(PlainModel).FullName);
            Assert.AreEqual(1, recs.Length);
            Assert.AreEqual(TrackedObjectLiveness.Alive, recs[0].Liveness);
            Assert.AreEqual("p1", recs[0].Tag);
            GC.KeepAlive(o);
        }

        [Test]
        public void Track_UnityObject_NativeAlive_Classified()
        {
            ObjectTracker.Enabled = true;
            ObjectTracker.SetUnityObjectInspector(new FakeInspector
            {
                UnityType = t => t == typeof(FakeUnityObject),
                NativeAlive = _ => true,
            });

            object uo = new FakeUnityObject();
            ObjectTracker.Track(uo);

            ObjectTrackerStats[] stats = ObjectTracker.Snapshot();
            Assert.AreEqual(1, stats.Length);
            Assert.IsTrue(stats[0].IsUnityObject);
            Assert.AreEqual(1, stats[0].Alive);
            Assert.AreEqual(1, stats[0].UnityNativeAlive);
            Assert.AreEqual(0, stats[0].UnityNativeDestroyed);

            ObjectTrackerRecord[] recs = ObjectTracker.Inspect();
            Assert.AreEqual(TrackedObjectLiveness.UnityNativeAlive, recs[0].Liveness);
            GC.KeepAlive(uo);
        }

        [Test]
        public void Track_UnityObject_NativeDestroyedButReferenced_DetectedAsLeak()
        {
            ObjectTracker.Enabled = true;
            object destroyed = new FakeUnityObject();
            ObjectTracker.SetUnityObjectInspector(new FakeInspector
            {
                UnityType = t => t == typeof(FakeUnityObject),
                // 模拟 Unity 原生已 Destroy：该实例「对象判空」为真（原生死），但托管引用仍在。
                NativeAlive = o => !ReferenceEquals(o, destroyed),
            });

            ObjectTracker.Track(destroyed, "dangling");

            ObjectTrackerStats[] stats = ObjectTracker.Snapshot();
            Assert.AreEqual(1, stats.Length);
            Assert.IsTrue(stats[0].IsUnityObject);
            Assert.AreEqual(1, stats[0].Alive);
            Assert.AreEqual(0, stats[0].UnityNativeAlive);
            Assert.AreEqual(1, stats[0].UnityNativeDestroyed, "已 Destroy 却仍被引用应记为悬垂泄漏");

            ObjectTrackerRecord[] recs = ObjectTracker.Inspect();
            Assert.AreEqual(TrackedObjectLiveness.UnityNativeDestroyed, recs[0].Liveness);
            Assert.AreEqual("dangling", recs[0].Tag);
            GC.KeepAlive(destroyed);
        }

        [Test]
        public void Track_CaptureStack_RecordHasStack()
        {
            ObjectTracker.Enabled = true;
            object o = new PlainModel();
            ObjectTracker.Track(o, captureStack: true);

            ObjectTrackerRecord[] recs = ObjectTracker.Inspect();
            Assert.AreEqual(1, recs.Length);
            Assert.IsNotNull(recs[0].CreationStack);
            Assert.IsNotEmpty(recs[0].CreationStack);
            GC.KeepAlive(o);
        }

        [Test]
        public void Track_NoStack_RecordStackNull()
        {
            ObjectTracker.Enabled = true;
            object o = new PlainModel();
            ObjectTracker.Track(o, captureStack: false);

            ObjectTrackerRecord[] recs = ObjectTracker.Inspect();
            Assert.IsNull(recs[0].CreationStack);
            GC.KeepAlive(o);
        }

        [Test]
        public void Snapshot_AggregatesByType()
        {
            ObjectTracker.Enabled = true;
            object a1 = new PlainModel();
            object a2 = new PlainModel();
            object b1 = new FakeUnityObject();
            ObjectTracker.Track(a1);
            ObjectTracker.Track(a2);
            ObjectTracker.Track(b1);

            ObjectTrackerStats[] stats = ObjectTracker.Snapshot();
            Assert.AreEqual(2, stats.Length); // 两个类型
            foreach (ObjectTrackerStats s in stats)
            {
                if (s.TypeName == typeof(PlainModel).FullName) Assert.AreEqual(2, s.Alive);
                else if (s.TypeName == typeof(FakeUnityObject).FullName) Assert.AreEqual(1, s.Alive);
                else Assert.Fail("unexpected type " + s.TypeName);
            }
            GC.KeepAlive(a1); GC.KeepAlive(a2); GC.KeepAlive(b1);
        }

        [Test]
        public void MaxEntries_CapsTracking()
        {
            ObjectTracker.Enabled = true;
            ObjectTracker.MaxEntries = 2;
            object o1 = new PlainModel(), o2 = new PlainModel(), o3 = new PlainModel();
            ObjectTracker.Track(o1);
            ObjectTracker.Track(o2);
            ObjectTracker.Track(o3); // 超出上限，忽略
            Assert.AreEqual(2, ObjectTracker.TrackedCount);
            GC.KeepAlive(o1); GC.KeepAlive(o2); GC.KeepAlive(o3);
        }

        [Test]
        public void Clear_EmptiesAll()
        {
            ObjectTracker.Enabled = true;
            object o = new PlainModel();
            ObjectTracker.Track(o);
            Assert.AreEqual(1, ObjectTracker.TrackedCount);
            ObjectTracker.Clear();
            Assert.AreEqual(0, ObjectTracker.TrackedCount);
            GC.KeepAlive(o);
        }

        [Test]
        public void Sweep_RemovesGarbageCollected()
        {
            ObjectTracker.Enabled = true;
            TrackTemporary(); // 在独立方法内登记一个无外部强引用的对象
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int removed = ObjectTracker.Sweep();
            Assert.GreaterOrEqual(removed, 1, "无强引用的对象经 GC 后应被 Sweep 清理");
            Assert.AreEqual(0, ObjectTracker.TrackedCount);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TrackTemporary()
        {
            object o = new PlainModel();
            ObjectTracker.Track(o);
            // o 离开作用域，无外部强引用 → 可被 GC 回收
        }
    }
}
