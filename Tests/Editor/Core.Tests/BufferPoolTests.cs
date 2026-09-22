//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Serialization;

namespace EjoyFramework.Tests
{
    /// <summary>WS1-M3：BufferPool 分桶数组池 + ByteBuffer 接入。</summary>
    public sealed class BufferPoolTests
    {
        [SetUp]
        public void SetUp()
        {
            BufferPool<byte>.Clear();
            BufferPool<int>.Clear();
        }

        [Test]
        public void Rent_RoundsUpToBucketSize()
        {
            Assert.AreEqual(16, BufferPool<byte>.Rent(1).Length);
            Assert.AreEqual(16, BufferPool<byte>.Rent(16).Length);
            Assert.AreEqual(32, BufferPool<byte>.Rent(17).Length);
            Assert.AreEqual(128, BufferPool<byte>.Rent(100).Length);
            Assert.AreEqual(1024, BufferPool<byte>.Rent(1024).Length);
            Assert.AreEqual(0, BufferPool<byte>.Rent(0).Length);
        }

        [Test]
        public void Rent_BeyondMaxPooled_AllocatesExactAndIsNotPooled()
        {
            int size = BufferPool<byte>.MaxPooledLength + 1;
            byte[] big = BufferPool<byte>.Rent(size);
            Assert.AreEqual(size, big.Length);
            long dropped = BufferPool<byte>.DroppedCount;
            BufferPool<byte>.Return(big);
            Assert.AreEqual(dropped + 1, BufferPool<byte>.DroppedCount);
        }

        [Test]
        public void Return_ThenRent_ReusesSameArray()
        {
            byte[] a = BufferPool<byte>.Rent(100);
            a[0] = 7;
            BufferPool<byte>.Return(a);
            Assert.AreEqual(1, BufferPool<byte>.GetIdleCount(128));
            byte[] b = BufferPool<byte>.Rent(128);
            Assert.AreSame(a, b);
            Assert.AreEqual(7, b[0], "默认不清零。");
        }

        [Test]
        public void Return_WithClear_ZeroesContents()
        {
            byte[] a = BufferPool<byte>.Rent(16);
            a[3] = 9;
            BufferPool<byte>.Return(a, clearArray: true);
            Assert.AreEqual(0, BufferPool<byte>.Rent(16)[3]);
        }

        [Test]
        public void Return_ForeignSizedArray_IsIgnored()
        {
            long dropped = BufferPool<int>.DroppedCount;
            BufferPool<int>.Return(new int[100]);   // 非 2 的幂
            BufferPool<int>.Return(new int[8]);     // 小于最小桶
            Assert.AreEqual(dropped + 2, BufferPool<int>.DroppedCount);
            Assert.AreEqual(0, BufferPool<int>.GetIdleCount(128));
        }

        [Test]
        public void Return_DoubleReturn_ThrowsInEditor()
        {
            byte[] a = BufferPool<byte>.Rent(64);
            BufferPool<byte>.Return(a);
            Assert.Throws<FrameworkException>(() => BufferPool<byte>.Return(a));
        }

        [Test]
        public void MaxRetainedPerBucket_CapsIdle()
        {
            int old = BufferPool<byte>.MaxRetainedPerBucket;
            try
            {
                BufferPool<byte>.MaxRetainedPerBucket = 2;
                for (int i = 0; i < 4; i++) BufferPool<byte>.Return(new byte[256]);
                Assert.AreEqual(2, BufferPool<byte>.GetIdleCount(256));
            }
            finally
            {
                BufferPool<byte>.MaxRetainedPerBucket = old;
            }
        }

        [Test]
        public void RentOnWorkerThread_ReturnOnMain_IsSafe()
        {
            byte[] rented = null;
            var t = new Thread(() => { rented = BufferPool<byte>.Rent(512); });
            t.Start();
            t.Join();
            Assert.IsNotNull(rented);
            BufferPool<byte>.Return(rented);
            Assert.AreSame(rented, BufferPool<byte>.Rent(512));
        }

        [Test]
        public void UsingScope_Returns()
        {
            byte[] arr;
            using (BufferPool<byte>.Rent(32, out arr)) { arr[0] = 1; }
            Assert.AreEqual(1, BufferPool<byte>.GetIdleCount(32));
        }

        [Test]
        public void RentReturn_SteadyState_DoesNotAllocate()
        {
            for (int i = 0; i < 4; i++) BufferPool<byte>.Return(BufferPool<byte>.Rent(1024));

            Assert.That(() =>
            {
                for (int i = 0; i < 64; i++)
                {
                    byte[] a = BufferPool<byte>.Rent(1000);
                    a[0] = (byte)i;
                    BufferPool<byte>.Return(a);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        // ================================================================
        //  ByteBuffer 接入
        // ================================================================

        [Test]
        public void ByteBuffer_Grow_ReturnsOldBufferToPool()
        {
            ByteBuffer buffer = ByteBuffer.Acquire();
            byte[] initial = buffer.RawBuffer;
            Assert.AreEqual(1024, initial.Length);

            for (int i = 0; i < 1025; i++) buffer.WriteByte(1);   // 触发一次扩容到 2048

            Assert.AreEqual(2048, buffer.Capacity);
            Assert.AreEqual(1, BufferPool<byte>.GetIdleCount(1024), "扩容后旧的 1024 数组应回到池里。");
            Assert.AreSame(initial, BufferPool<byte>.Rent(1024));
            buffer.Release();
        }

        [Test]
        public void ByteBuffer_Wrap_NeverReturnsForeignArray()
        {
            byte[] external = new byte[128];
            ByteBuffer buffer = ByteBuffer.Acquire();
            buffer.Wrap(external);
            buffer.Clear();   // 非自有数组：丢弃而不归还
            Assert.AreEqual(0, BufferPool<byte>.GetIdleCount(128));
            Assert.AreEqual(1024, buffer.Capacity);
            buffer.Release();
        }

        [Test]
        public void ByteBuffer_WriteWithinCapacity_DoesNotAllocate()
        {
            ByteBuffer buffer = ByteBuffer.Acquire();
            buffer.WriteInt(1);
            buffer.Reset();

            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++) buffer.WriteInt(i);
                buffer.Reset();
            }, Is.Not.AllocatingGCMemory());

            buffer.Release();
        }
    }
}
