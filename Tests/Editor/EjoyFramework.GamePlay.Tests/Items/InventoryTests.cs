//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Items;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Items
{
    /// <summary>
    /// 针对 <see cref="Inventory"/> 加入/移除/堆叠/容量/事件语义的单元测试。
    /// </summary>
    [TestFixture]
    public class InventoryTests
    {
        private static ItemCatalog MakeCatalog(int maxStack)
        {
            ItemCatalog catalog = new ItemCatalog();
            catalog.Register(new ItemDefinition("x", maxStack));
            return catalog;
        }

        [Test]
        public void AddItem_IntoEmpty_CountAndHasCorrect()
        {
            Inventory inv = new Inventory(MakeCatalog(10));

            int leftover = inv.AddItem("x", 7);

            Assert.AreEqual(0, leftover);
            Assert.AreEqual(7, inv.GetCount("x"));
            Assert.IsTrue(inv.Has("x"));
            Assert.IsTrue(inv.Has("x", 7));
            Assert.IsFalse(inv.Has("x", 8));
            Assert.AreEqual(1, inv.UsedSlots);
        }

        [Test]
        public void AddItem_StacksUpToMaxStack_AllocatesMultipleSlots()
        {
            Inventory inv = new Inventory(MakeCatalog(10));

            int leftover = inv.AddItem("x", 25);

            Assert.AreEqual(0, leftover);
            Assert.AreEqual(25, inv.GetCount("x"));
            // 10/10/5 三个槽位。
            Assert.AreEqual(3, inv.UsedSlots);
            Assert.AreEqual(10, inv.Slots[0].Count);
            Assert.AreEqual(10, inv.Slots[1].Count);
            Assert.AreEqual(5, inv.Slots[2].Count);
        }

        [Test]
        public void AddItem_FillsPartialStackFirst()
        {
            Inventory inv = new Inventory(MakeCatalog(10));

            inv.AddItem("x", 5);
            inv.AddItem("x", 8);

            // 先把第一槽填满到 10，再开新槽放 3。
            Assert.AreEqual(2, inv.UsedSlots);
            Assert.AreEqual(10, inv.Slots[0].Count);
            Assert.AreEqual(3, inv.Slots[1].Count);
            Assert.AreEqual(13, inv.GetCount("x"));
        }

        [Test]
        public void AddItem_FixedCapacityOverflow_ReturnsLeftover()
        {
            Inventory inv = new Inventory(MakeCatalog(10), 2);

            int leftover = inv.AddItem("x", 25);

            // 容量 2 槽、每槽 10，仅能存 20，剩 5 放不下。
            Assert.AreEqual(5, leftover);
            Assert.AreEqual(20, inv.GetCount("x"));
            Assert.AreEqual(2, inv.UsedSlots);
        }

        [Test]
        public void AddItem_NonPositiveCount_NoChange()
        {
            Inventory inv = new Inventory(MakeCatalog(10));
            int changedCalls = 0;
            inv.OnChanged += _ => changedCalls++;

            // 加入 0 个：返回 0 剩余、不改动、不触发事件。
            Assert.AreEqual(0, inv.AddItem("x", 0));
            // 加入负数：同样视为无操作。
            Assert.AreEqual(0, inv.AddItem("x", -3));

            Assert.AreEqual(0, inv.GetCount("x"));
            Assert.AreEqual(0, inv.UsedSlots);
            Assert.AreEqual(0, changedCalls);
        }

        [Test]
        public void RemoveItem_AllOrNothing_InsufficientRemovesNothing()
        {
            Inventory inv = new Inventory(MakeCatalog(10));
            inv.AddItem("x", 15);

            bool removed = inv.RemoveItem("x", 20);

            Assert.IsFalse(removed);
            Assert.AreEqual(15, inv.GetCount("x"));
            Assert.AreEqual(2, inv.UsedSlots);
        }

        [Test]
        public void RemoveItem_ExactRemove_FreesSlots()
        {
            Inventory inv = new Inventory(MakeCatalog(10));
            inv.AddItem("x", 25); // 10/10/5 三槽

            bool removed = inv.RemoveItem("x", 15);

            Assert.IsTrue(removed);
            Assert.AreEqual(10, inv.GetCount("x"));
            // 移除 15：清空末尾 5 的槽，再从 10 的槽扣 10 清空，剩一个满槽。
            Assert.AreEqual(1, inv.UsedSlots);
            Assert.AreEqual(10, inv.Slots[0].Count);
        }

        [Test]
        public void RemoveItem_RemoveAll_EmptiesInventory()
        {
            Inventory inv = new Inventory(MakeCatalog(10));
            inv.AddItem("x", 25);

            Assert.IsTrue(inv.RemoveItem("x", 25));
            Assert.AreEqual(0, inv.GetCount("x"));
            Assert.AreEqual(0, inv.UsedSlots);
        }

        [Test]
        public void UnlimitedStack_WhenItemNotInCatalog()
        {
            // 无目录：物品视为无限堆叠，集中到单槽。
            Inventory inv = new Inventory(null);

            int leftover = inv.AddItem("anything", 1000);

            Assert.AreEqual(0, leftover);
            Assert.AreEqual(1000, inv.GetCount("anything"));
            Assert.AreEqual(1, inv.UsedSlots);
        }

        [Test]
        public void UnlimitedStack_WhenItemMissingFromCatalog()
        {
            // 有目录但该物品未注册：仍视为无限堆叠。
            Inventory inv = new Inventory(MakeCatalog(10));

            int leftover = inv.AddItem("unregistered", 1000);

            Assert.AreEqual(0, leftover);
            Assert.AreEqual(1000, inv.GetCount("unregistered"));
            Assert.AreEqual(1, inv.UsedSlots);
        }

        [Test]
        public void OnChanged_FiresOnAddAndRemove_NotOnNoOp()
        {
            Inventory inv = new Inventory(MakeCatalog(10));
            int changedCalls = 0;
            inv.OnChanged += _ => changedCalls++;

            inv.AddItem("x", 5);
            Assert.AreEqual(1, changedCalls);

            inv.RemoveItem("x", 5);
            Assert.AreEqual(2, changedCalls);

            // 移除不存在的物品：无变化，不触发。
            Assert.IsFalse(inv.RemoveItem("absent", 1));
            Assert.AreEqual(2, changedCalls);

            // 加入 0 个：无变化，不触发。
            inv.AddItem("x", 0);
            Assert.AreEqual(2, changedCalls);
        }

        [Test]
        public void OnChanged_FiresOncePerMutatingCall()
        {
            Inventory inv = new Inventory(MakeCatalog(10));
            int changedCalls = 0;
            inv.OnChanged += _ => changedCalls++;

            // 一次跨多槽的加入只触发一次。
            inv.AddItem("x", 25);
            Assert.AreEqual(1, changedCalls);

            // 一次跨多槽的移除只触发一次。
            inv.RemoveItem("x", 25);
            Assert.AreEqual(2, changedCalls);
        }

        [Test]
        public void Clear_EmptiesAndFiresOnce_NoOpWhenEmpty()
        {
            Inventory inv = new Inventory(MakeCatalog(10));
            int changedCalls = 0;
            inv.OnChanged += _ => changedCalls++;

            inv.AddItem("x", 5);
            changedCalls = 0;

            inv.Clear();
            Assert.AreEqual(0, inv.UsedSlots);
            Assert.AreEqual(1, changedCalls);

            // 已空再清：不触发。
            inv.Clear();
            Assert.AreEqual(1, changedCalls);
        }

        [Test]
        public void Capacity_ReportedCorrectly()
        {
            Assert.AreEqual(0, new Inventory(null).Capacity);
            Assert.AreEqual(0, new Inventory(null, -3).Capacity);
            Assert.AreEqual(4, new Inventory(null, 4).Capacity);
        }

        [Test]
        public void Has_ZeroOrNegativeCount_AlwaysTrue()
        {
            Inventory inv = new Inventory(MakeCatalog(10));
            Assert.IsTrue(inv.Has("x", 0));
            Assert.IsTrue(inv.Has("missing", 0));
        }
    }
}
