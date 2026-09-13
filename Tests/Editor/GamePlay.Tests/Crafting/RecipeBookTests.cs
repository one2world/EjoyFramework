//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Crafting;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Crafting
{
    /// <summary>
    /// 针对 <see cref="RecipeBook"/> 登记、查询与 <see cref="RecipeBook.CraftableWith"/> 筛选的单元测试。
    /// </summary>
    [TestFixture]
    public class RecipeBookTests
    {
        private static Recipe MakeRecipe(string id, string input, int inCount, string station = null)
        {
            Recipe.Builder builder = Recipe.Create(id).AddOutput(id + "_out", 1);
            if (input != null)
            {
                builder.AddInput(input, inCount);
            }

            if (station != null)
            {
                builder.AtStation(station);
            }

            return builder.Build();
        }

        [Test]
        public void Add_ThenGetHasCount()
        {
            RecipeBook book = new RecipeBook();
            Recipe recipe = MakeRecipe("plank", "log", 1);

            book.Add(recipe);

            Assert.AreEqual(1, book.Count);
            Assert.IsTrue(book.Has("plank"));
            Assert.AreSame(recipe, book.Get("plank"));
        }

        [Test]
        public void Get_Missing_ReturnsNull()
        {
            RecipeBook book = new RecipeBook();

            Assert.IsNull(book.Get("nope"));
            Assert.IsFalse(book.Has("nope"));
        }

        [Test]
        public void Add_DuplicateId_Throws()
        {
            RecipeBook book = new RecipeBook();
            book.Add(MakeRecipe("plank", "log", 1));

            Assert.Throws<ArgumentException>(() => book.Add(MakeRecipe("plank", "stone", 2)));
        }

        [Test]
        public void Add_Null_Throws()
        {
            RecipeBook book = new RecipeBook();

            Assert.Throws<ArgumentNullException>(() => book.Add(null));
        }

        [Test]
        public void Recipes_EnumeratesAll()
        {
            RecipeBook book = new RecipeBook();
            book.Add(MakeRecipe("a", "x", 1));
            book.Add(MakeRecipe("b", "y", 1));

            List<Recipe> all = new List<Recipe>(book.Recipes);

            Assert.AreEqual(2, all.Count);
        }

        [Test]
        public void CraftableWith_ReturnsSatisfiableSubset()
        {
            RecipeBook book = new RecipeBook();
            book.Add(MakeRecipe("plank", "log", 2));   // 需要 log x2 —— 满足
            book.Add(MakeRecipe("ingot", "ore", 5));   // 需要 ore x5 —— 不满足
            FakeItemSource source = new FakeItemSource().With("log", 3).With("ore", 1);

            List<Recipe> craftable = book.CraftableWith(source);

            Assert.AreEqual(1, craftable.Count);
            Assert.AreEqual("plank", craftable[0].Id);
        }

        [Test]
        public void CraftableWith_FiltersByStation()
        {
            RecipeBook book = new RecipeBook();
            book.Add(MakeRecipe("plank", "log", 1));                  // 无工作台要求 —— 任意场合可制
            book.Add(MakeRecipe("sword", "ingot", 1, "forge"));      // 需要 forge
            book.Add(MakeRecipe("potion", "herb", 1, "alchemy"));    // 需要 alchemy
            FakeItemSource source = new FakeItemSource()
                .With("log", 5)
                .With("ingot", 5)
                .With("herb", 5);

            List<Recipe> atForge = book.CraftableWith(source, "forge");

            Assert.AreEqual(2, atForge.Count);
            CollectionAssert.AreEquivalent(
                new[] { "plank", "sword" },
                atForge.ConvertAll(r => r.Id));
        }

        [Test]
        public void CraftableWith_NullSource_Throws()
        {
            RecipeBook book = new RecipeBook();

            Assert.Throws<ArgumentNullException>(() => book.CraftableWith(null));
        }
    }
}
