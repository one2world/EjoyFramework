//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Crafting;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Crafting
{
    /// <summary>
    /// 针对 <see cref="CraftingService"/> 校验、原子扣除/产出与事件派发的单元测试。
    /// </summary>
    [TestFixture]
    public class CraftingServiceTests
    {
        private static RecipeBook BookWith(Recipe recipe)
        {
            RecipeBook book = new RecipeBook();
            book.Add(recipe);
            return book;
        }

        private static Recipe PlankRecipe(string station = null)
        {
            Recipe.Builder builder = Recipe.Create("plank")
                .AddInput("log", 2)
                .AddOutput("plank", 4);
            if (station != null)
            {
                builder.AtStation(station);
            }

            return builder.Build();
        }

        [Test]
        public void Constructor_NullBook_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CraftingService(null));
        }

        [Test]
        public void Parameterless_StartsWithEmptyBook()
        {
            CraftingService service = new CraftingService();

            Assert.IsNotNull(service.Book);
            Assert.AreEqual(0, service.Book.Count);
        }

        [Test]
        public void SetRecipeBook_InjectsBook_AndCraftsAgainstIt()
        {
            CraftingService service = new CraftingService();
            service.SetRecipeBook(BookWith(PlankRecipe()));
            FakeItemSource source = new FakeItemSource().With("log", 2);

            Assert.IsTrue(service.CanCraft("plank", source));
        }

        [Test]
        public void SetRecipeBook_Null_Throws()
        {
            CraftingService service = new CraftingService();

            Assert.Throws<ArgumentNullException>(() => service.SetRecipeBook(null));
        }

        [Test]
        public void Shutdown_ClearsBookAndUnsubscribesEvent()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe()));
            bool fired = false;
            service.OnCrafted += (svc, recipe) => fired = true;

            service.Shutdown();

            // 关闭后配方表清空，旧配方不再可制作；事件订阅亦被清除。
            Assert.AreEqual(0, service.Book.Count);
            FakeItemSource source = new FakeItemSource().With("log", 2);
            Assert.AreEqual(CraftOutcome.UnknownRecipe, service.Craft("plank", source));
            Assert.IsFalse(fired);
        }

        [Test]
        public void CanCraft_True_WhenIngredientsSufficient()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe()));
            FakeItemSource source = new FakeItemSource().With("log", 2);

            Assert.IsTrue(service.CanCraft("plank", source));
        }

        [Test]
        public void CanCraft_False_WhenIngredientsInsufficient()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe()));
            FakeItemSource source = new FakeItemSource().With("log", 1);

            Assert.IsFalse(service.CanCraft("plank", source));
        }

        [Test]
        public void CanCraft_True_WhenStationMatches()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe("sawmill")));
            FakeItemSource source = new FakeItemSource().With("log", 2);

            Assert.IsTrue(service.CanCraft("plank", source, "sawmill"));
        }

        [Test]
        public void CanCraft_False_WhenStationMismatch()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe("sawmill")));
            FakeItemSource source = new FakeItemSource().With("log", 2);

            // 材料充足但工作台不符 —— 仍不可制作。
            Assert.IsFalse(service.CanCraft("plank", source, "forge"));
            Assert.IsFalse(service.CanCraft("plank", source, null));
        }

        [Test]
        public void Craft_Success_ConsumesInputsAddsOutputsFiresEvent()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe()));
            FakeItemSource source = new FakeItemSource().With("log", 5);

            Recipe crafted = null;
            ICraftingService sender = null;
            service.OnCrafted += (svc, recipe) =>
            {
                sender = svc;
                crafted = recipe;
            };

            CraftOutcome outcome = service.Craft("plank", source);

            Assert.AreEqual(CraftOutcome.Success, outcome);
            Assert.AreEqual(3, source.GetCount("log"));   // 5 - 2
            Assert.AreEqual(4, source.GetCount("plank")); // +4
            Assert.AreSame(service, sender);
            Assert.IsNotNull(crafted);
            Assert.AreEqual("plank", crafted.Id);
        }

        [Test]
        public void Craft_MissingIngredients_LeavesSourceUntouched()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe()));
            FakeItemSource source = new FakeItemSource().With("log", 1); // 需要 2，只有 1

            bool fired = false;
            service.OnCrafted += (svc, recipe) => fired = true;

            CraftOutcome outcome = service.Craft("plank", source);

            Assert.AreEqual(CraftOutcome.MissingIngredients, outcome);
            Assert.AreEqual(1, source.GetCount("log")); // 未被扣除
            Assert.AreEqual(0, source.GetCount("plank"));
            Assert.IsFalse(fired);
        }

        [Test]
        public void Craft_PartiallyAffordable_IsAtomic_NoInputConsumed()
        {
            // 多输入配方：第一项买得起、第二项买不起 —— 必须整体放弃，绝不扣掉第一项。
            Recipe recipe = Recipe.Create("alloy")
                .AddInput("copper", 2)
                .AddInput("tin", 3)
                .AddOutput("bronze", 1)
                .Build();
            CraftingService service = new CraftingService(BookWith(recipe));
            FakeItemSource source = new FakeItemSource().With("copper", 10).With("tin", 1);

            CraftOutcome outcome = service.Craft("alloy", source);

            Assert.AreEqual(CraftOutcome.MissingIngredients, outcome);
            Assert.AreEqual(10, source.GetCount("copper")); // 第一项未被提前扣除
            Assert.AreEqual(1, source.GetCount("tin"));
            Assert.AreEqual(0, source.GetCount("bronze"));
        }

        [Test]
        public void Craft_UnknownRecipe_ReturnsUnknownRecipe()
        {
            CraftingService service = new CraftingService(new RecipeBook());
            FakeItemSource source = new FakeItemSource().With("log", 99);

            CraftOutcome outcome = service.Craft("ghost", source);

            Assert.AreEqual(CraftOutcome.UnknownRecipe, outcome);
        }

        [Test]
        public void Craft_WrongStation_ReturnsWrongStationAndUntouched()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe("sawmill")));
            FakeItemSource source = new FakeItemSource().With("log", 5);

            CraftOutcome outcome = service.Craft("plank", source, "forge");

            Assert.AreEqual(CraftOutcome.WrongStation, outcome);
            Assert.AreEqual(5, source.GetCount("log")); // 未被改动
        }

        [Test]
        public void Craft_NullSource_Throws()
        {
            CraftingService service = new CraftingService(BookWith(PlankRecipe()));

            Assert.Throws<ArgumentNullException>(() => service.Craft("plank", null));
        }
    }
}
