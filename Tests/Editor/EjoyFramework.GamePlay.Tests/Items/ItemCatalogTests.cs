//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Items;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Items
{
    /// <summary>
    /// 针对 <see cref="ItemCatalog"/> 与 <see cref="ItemDefinition"/> 的单元测试。
    /// </summary>
    [TestFixture]
    public class ItemCatalogTests
    {
        [Test]
        public void Register_Then_GetAndTryGet()
        {
            ItemCatalog catalog = new ItemCatalog();
            ItemDefinition sword = new ItemDefinition("sword", 1, "weapon");
            catalog.Register(sword);

            Assert.AreEqual(1, catalog.Count);
            Assert.AreSame(sword, catalog.Get("sword"));

            ItemDefinition fetched;
            Assert.IsTrue(catalog.TryGet("sword", out fetched));
            Assert.AreSame(sword, fetched);
        }

        [Test]
        public void Get_Missing_ReturnsNull()
        {
            ItemCatalog catalog = new ItemCatalog();

            Assert.IsNull(catalog.Get("missing"));

            ItemDefinition fetched;
            Assert.IsFalse(catalog.TryGet("missing", out fetched));
            Assert.IsNull(fetched);
        }

        [Test]
        public void Register_Duplicate_Throws()
        {
            ItemCatalog catalog = new ItemCatalog();
            catalog.Register(new ItemDefinition("gold", 999));

            Assert.Throws<ArgumentException>(() => catalog.Register(new ItemDefinition("gold", 1)));
        }

        [Test]
        public void Register_Null_Throws()
        {
            ItemCatalog catalog = new ItemCatalog();
            Assert.Throws<ArgumentNullException>(() => catalog.Register(null));
        }

        [Test]
        public void ItemDefinition_NonPositiveMaxStack_NormalizedToUnlimited()
        {
            ItemDefinition zero = new ItemDefinition("a", 0);
            ItemDefinition negative = new ItemDefinition("b", -5);

            Assert.AreEqual(int.MaxValue, zero.MaxStack);
            Assert.AreEqual(int.MaxValue, negative.MaxStack);
        }

        [Test]
        public void ItemDefinition_EmptyId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ItemDefinition(null));
            Assert.Throws<ArgumentException>(() => new ItemDefinition(string.Empty));
        }

        [Test]
        public void ItemDefinition_StoresCategoryAndPayload()
        {
            object payload = new object();
            ItemDefinition def = new ItemDefinition("potion", 99, "consumable", payload);

            Assert.AreEqual("potion", def.Id);
            Assert.AreEqual(99, def.MaxStack);
            Assert.AreEqual("consumable", def.Category);
            Assert.AreSame(payload, def.Payload);
        }
    }
}
