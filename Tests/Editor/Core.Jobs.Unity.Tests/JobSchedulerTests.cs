//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core;
using EjoyFramework.Core.Jobs.Unity;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace EjoyFramework.Core.Jobs.Unity.Tests
{
    /// <summary>
    /// <see cref="JobSchedulerManager"/> / <see cref="JobUtility"/> / <see cref="JobScope"/> 的 EditMode 测试。
    /// 用真实小 Job 验证封装行为（填充/拷贝/合并/延迟完成/作用域释放）。
    /// </summary>
    [TestFixture]
    public class JobSchedulerTests
    {
        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void Fill_SetsAllElements()
        {
            var arr = new NativeArray<int>(1000, Allocator.Persistent);
            try
            {
                JobUtility.Fill(arr, 7).Complete();
                for (int i = 0; i < arr.Length; i++)
                {
                    Assert.AreEqual(7, arr[i]);
                }
            }
            finally { arr.Dispose(); }
        }

        [Test]
        public void Copy_CopiesAllElements()
        {
            var src = new NativeArray<int>(512, Allocator.Persistent);
            var dst = new NativeArray<int>(512, Allocator.Persistent);
            try
            {
                JobUtility.Fill(src, 42).Complete();
                JobUtility.Copy(src, dst).Complete();
                for (int i = 0; i < dst.Length; i++)
                {
                    Assert.AreEqual(42, dst[i]);
                }
            }
            finally { src.Dispose(); dst.Dispose(); }
        }

        [Test]
        public void Copy_LengthMismatch_Throws()
        {
            var src = new NativeArray<int>(4, Allocator.Persistent);
            var dst = new NativeArray<int>(8, Allocator.Persistent);
            try
            {
                Assert.Throws<FrameworkException>(() => JobUtility.Copy(src, dst));
            }
            finally { src.Dispose(); dst.Dispose(); }
        }

        [Test]
        public void Combine_Params_CompletesAllDependencies()
        {
            var a = new NativeArray<int>(256, Allocator.Persistent);
            var b = new NativeArray<int>(256, Allocator.Persistent);
            var c = new NativeArray<int>(256, Allocator.Persistent);
            try
            {
                var mgr = new JobSchedulerManager();
                JobHandle ha = JobUtility.Fill(a, 1);
                JobHandle hb = JobUtility.Fill(b, 2);
                JobHandle hc = JobUtility.Fill(c, 3);
                mgr.Combine(ha, hb, hc).Complete();

                Assert.AreEqual(1, a[0]);
                Assert.AreEqual(2, b[255]);
                Assert.AreEqual(3, c[128]);
            }
            finally { a.Dispose(); b.Dispose(); c.Dispose(); }
        }

        [Test]
        public void CompleteInLateUpdate_CompletesAndClearsPending()
        {
            var arr = new NativeArray<int>(256, Allocator.Persistent);
            try
            {
                var mgr = new JobSchedulerManager();
                mgr.CompleteInLateUpdate(JobUtility.Fill(arr, 5));
                Assert.AreEqual(1, mgr.PendingCount);

                mgr.LateUpdate(0f, 0f);

                Assert.AreEqual(0, mgr.PendingCount);
                for (int i = 0; i < arr.Length; i++)
                {
                    Assert.AreEqual(5, arr[i]);
                }
            }
            finally { arr.Dispose(); }
        }

        [Test]
        public void CompleteInLateUpdate_DefaultHandle_NotRegistered()
        {
            var mgr = new JobSchedulerManager();
            mgr.CompleteInLateUpdate(default);
            Assert.AreEqual(0, mgr.PendingCount);
        }

        [Test]
        public void CompleteAll_CompletesAndClears()
        {
            var arr = new NativeArray<int>(128, Allocator.Persistent);
            try
            {
                var mgr = new JobSchedulerManager();
                mgr.CompleteInLateUpdate(JobUtility.Fill(arr, 9));
                mgr.CompleteAll();
                Assert.AreEqual(0, mgr.PendingCount);
                Assert.AreEqual(9, arr[100]);
            }
            finally { arr.Dispose(); }
        }

        [Test]
        public void JobScope_AllocatesRunsAndDisposes()
        {
            // 作用域内分配 + 调度 + CompleteOnDispose；作用域结束自动释放，不抛异常、结果正确。
            using (var scope = new JobScope(Allocator.TempJob))
            {
                var data = scope.Alloc<int>(256);
                JobHandle h = JobUtility.Fill(data, 11);
                scope.CompleteOnDispose(h);

                h.Complete(); // 作用域内读取前先完成
                Assert.AreEqual(11, data[0]);
                Assert.AreEqual(11, data[255]);
            }
            // 离开 using：data 已被作用域释放（重复 Complete 安全）。
            Assert.Pass();
        }
    }
}
