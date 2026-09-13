//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Units;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Units
{
    /// <summary>
    /// 针对引擎无关核心 <see cref="ItemPickup"/> 的单元测试：覆盖内容物累加、范围判定（边界包含）、
    /// 一次性拾取与事件触发、幂等性、对象池重置、平方距离计算，以及 AutoCollect 标志暴露。
    /// </summary>
    [TestFixture]
    public class ItemPickupTests
    {
        private const float Delta = 1e-4f;

        // ---------------------------------------------------------------
        // 内容物累加
        // ---------------------------------------------------------------

        [Test]
        public void AddContent_AccumulatesSameId_AndReflectsInContents()
        {
            ItemPickup pickup = new ItemPickup(5f);

            pickup.AddContent("gold", 10);
            pickup.AddContent("potion", 2);
            pickup.AddContent("gold", 5);

            IReadOnlyList<ItemContent> contents = pickup.Contents;
            Assert.AreEqual(2, contents.Count, "相同 Id 应累加而非新增重复条目。");

            Assert.AreEqual("gold", contents[0].ItemId);
            Assert.AreEqual(15, contents[0].Count);
            Assert.AreEqual("potion", contents[1].ItemId);
            Assert.AreEqual(2, contents[1].Count);
        }

        [Test]
        public void AddContent_DistinctIds_AppendSeparateEntries()
        {
            ItemPickup pickup = new ItemPickup(1f);

            pickup.AddContent("a", 1);
            pickup.AddContent("b", 1);
            pickup.AddContent("c", 1);

            Assert.AreEqual(3, pickup.Contents.Count);
        }

        // ---------------------------------------------------------------
        // 范围判定（边界包含）
        // ---------------------------------------------------------------

        [Test]
        public void IsInRange_WithinRadius_ReturnsTrue()
        {
            ItemPickup pickup = new ItemPickup(5f);

            // 距离 3-4-5 直角三角形：dist = 5，正好等于半径 → 边界包含。
            Assert.IsTrue(pickup.IsInRange(0f, 0f, 3f, 4f));
        }

        [Test]
        public void IsInRange_BoundaryInclusive()
        {
            ItemPickup pickup = new ItemPickup(2f);

            // 拾取者正好在半径 2 处（沿 X 轴）→ 应判定为在范围内。
            Assert.IsTrue(pickup.IsInRange(0f, 0f, 2f, 0f));
        }

        [Test]
        public void IsInRange_OutsideRadius_ReturnsFalse()
        {
            ItemPickup pickup = new ItemPickup(5f);

            // 距离 = 5.0001 > 5 → 范围外。
            Assert.IsFalse(pickup.IsInRange(0f, 0f, 5.0001f, 0f));
        }

        [Test]
        public void IsInRange_RespectsItemOffset()
        {
            ItemPickup pickup = new ItemPickup(1f);

            // 拾取物位于 (10, 10)，拾取者位于 (10.5, 10) → 距离 0.5 <= 1。
            Assert.IsTrue(pickup.IsInRange(10f, 10f, 10.5f, 10f));
            // 拾取者位于 (12, 10) → 距离 2 > 1。
            Assert.IsFalse(pickup.IsInRange(10f, 10f, 12f, 10f));
        }

        // ---------------------------------------------------------------
        // 平方距离
        // ---------------------------------------------------------------

        [Test]
        public void SqrDistanceTo_ComputesSquaredEuclidean()
        {
            ItemPickup pickup = new ItemPickup(1f);

            // (3,4) 距 (0,0)：3^2 + 4^2 = 25。
            Assert.AreEqual(25f, pickup.SqrDistanceTo(0f, 0f, 3f, 4f), Delta);

            // 带偏移：item(1,1) → collector(4,5)：dx=3, dy=4 → 25。
            Assert.AreEqual(25f, pickup.SqrDistanceTo(1f, 1f, 4f, 5f), Delta);

            // 同点距离为 0。
            Assert.AreEqual(0f, pickup.SqrDistanceTo(7f, 7f, 7f, 7f), Delta);
        }

        // ---------------------------------------------------------------
        // 一次性拾取 + 事件
        // ---------------------------------------------------------------

        [Test]
        public void TryCollect_GrantsContents_AndFiresOnCollectedOnce()
        {
            ItemPickup pickup = new ItemPickup(5f);
            pickup.AddContent("gold", 100);
            pickup.AddContent("gem", 3);

            int collectedCount = 0;
            ItemPickup eventArg = null;
            pickup.OnCollected += p =>
            {
                collectedCount++;
                eventArg = p;
            };

            bool ok = pickup.TryCollect(out IReadOnlyList<ItemContent> granted);

            Assert.IsTrue(ok);
            Assert.IsTrue(pickup.IsCollected);
            Assert.AreEqual(1, collectedCount, "OnCollected 应只触发一次。");
            Assert.AreSame(pickup, eventArg, "事件应携带拾取物自身。");

            Assert.AreEqual(2, granted.Count);
            Assert.AreEqual("gold", granted[0].ItemId);
            Assert.AreEqual(100, granted[0].Count);
            Assert.AreEqual("gem", granted[1].ItemId);
            Assert.AreEqual(3, granted[1].Count);
        }

        [Test]
        public void TryCollect_SecondCall_IsIdempotent_ReturnsFalseAndEmpty()
        {
            ItemPickup pickup = new ItemPickup(5f);
            pickup.AddContent("gold", 50);

            int collectedCount = 0;
            pickup.OnCollected += p => collectedCount++;

            Assert.IsTrue(pickup.TryCollect(out _));

            bool second = pickup.TryCollect(out IReadOnlyList<ItemContent> granted);

            Assert.IsFalse(second, "已拾取后再次拾取应返回 false。");
            Assert.IsNotNull(granted);
            Assert.AreEqual(0, granted.Count, "幂等空操作应输出空列表。");
            Assert.AreEqual(1, collectedCount, "第二次拾取不应再次触发事件。");
        }

        // ---------------------------------------------------------------
        // 对象池重置
        // ---------------------------------------------------------------

        [Test]
        public void Reset_UnCollects_AllowingCollectAgain()
        {
            ItemPickup pickup = new ItemPickup(5f);
            pickup.AddContent("gold", 10);

            Assert.IsTrue(pickup.TryCollect(out _));
            Assert.IsTrue(pickup.IsCollected);

            pickup.Reset();

            Assert.IsFalse(pickup.IsCollected, "Reset 后应解除已拾取标记。");

            bool again = pickup.TryCollect(out IReadOnlyList<ItemContent> granted);
            Assert.IsTrue(again, "Reset 后应可再次拾取。");
            Assert.AreEqual(1, granted.Count, "Reset 保留内容物。");
            Assert.AreEqual(10, granted[0].Count);
        }

        // ---------------------------------------------------------------
        // 标志暴露 / 属性
        // ---------------------------------------------------------------

        [Test]
        public void AutoCollect_FlagIsExposed()
        {
            ItemPickup autoOn = new ItemPickup(3f, autoCollect: true);
            ItemPickup autoOff = new ItemPickup(3f, autoCollect: false);

            Assert.IsTrue(autoOn.AutoCollect);
            Assert.IsFalse(autoOff.AutoCollect);

            // 默认构造（缺省参数）应为 true。
            ItemPickup defaultAuto = new ItemPickup(3f);
            Assert.IsTrue(defaultAuto.AutoCollect);
        }

        [Test]
        public void InteractRange_IsReadWrite_AndAffectsRangeCheck()
        {
            ItemPickup pickup = new ItemPickup(1f);
            Assert.AreEqual(1f, pickup.InteractRange, Delta);

            // 1 半径：(2,0) 在范围外。
            Assert.IsFalse(pickup.IsInRange(0f, 0f, 2f, 0f));

            pickup.InteractRange = 3f;
            Assert.AreEqual(3f, pickup.InteractRange, Delta);

            // 扩大到 3 后 (2,0) 进入范围。
            Assert.IsTrue(pickup.IsInRange(0f, 0f, 2f, 0f));
        }

        [Test]
        public void Fresh_Pickup_IsNotCollected_AndHasEmptyContents()
        {
            ItemPickup pickup = new ItemPickup(4f);

            Assert.IsFalse(pickup.IsCollected);
            Assert.IsNotNull(pickup.Contents);
            Assert.AreEqual(0, pickup.Contents.Count);
            Assert.AreEqual(4f, pickup.InteractRange, Delta);
        }
    }
}
