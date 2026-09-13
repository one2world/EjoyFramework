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
    /// 针对 <see cref="Recipe"/> 流式构造器与不可变性的单元测试。
    /// </summary>
    [TestFixture]
    public class RecipeTests
    {
        [Test]
        public void Build_NoOutput_Throws()
        {
            Recipe.Builder builder = Recipe.Create("nothing").AddInput("log", 1);

            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Test]
        public void Build_WithOutput_Succeeds()
        {
            Recipe recipe = Recipe.Create("plank")
                .AddInput("log", 1)
                .AddOutput("plank", 4)
                .Build();

            Assert.AreEqual("plank", recipe.Id);
            Assert.AreEqual(1, recipe.Inputs.Count);
            Assert.AreEqual(1, recipe.Outputs.Count);
            Assert.AreEqual("plank", recipe.Outputs[0].ItemId);
            Assert.AreEqual(4, recipe.Outputs[0].Count);
        }

        [Test]
        public void Create_EmptyId_Throws()
        {
            Assert.Throws<ArgumentException>(() => Recipe.Create(string.Empty));
        }

        [Test]
        public void Build_CapturesTimeStationAndPayload()
        {
            object payload = new object();
            Recipe recipe = Recipe.Create("sword")
                .AddInput("ingot", 3)
                .AddOutput("sword", 1)
                .WithTime(5f)
                .AtStation("forge")
                .WithPayload(payload)
                .Build();

            Assert.AreEqual(5f, recipe.CraftTimeSec, 1e-4f);
            Assert.AreEqual("forge", recipe.Station);
            Assert.AreSame(payload, recipe.Payload);
        }

        [Test]
        public void Build_DefaultsTimeZeroAndNoStation()
        {
            Recipe recipe = Recipe.Create("instant")
                .AddOutput("gem", 1)
                .Build();

            Assert.AreEqual(0f, recipe.CraftTimeSec, 1e-4f);
            Assert.IsNull(recipe.Station);
            Assert.IsNull(recipe.Payload);
        }

        [Test]
        public void Build_ProducesIndependentSnapshot()
        {
            // 同一构造器在 Build 后继续追加，不应影响已生成的配方。
            Recipe.Builder builder = Recipe.Create("snap")
                .AddInput("a", 1)
                .AddOutput("b", 1);
            Recipe first = builder.Build();

            builder.AddInput("c", 9).AddOutput("d", 9);
            Recipe second = builder.Build();

            Assert.AreEqual(1, first.Inputs.Count);
            Assert.AreEqual(1, first.Outputs.Count);
            Assert.AreEqual(2, second.Inputs.Count);
            Assert.AreEqual(2, second.Outputs.Count);
        }

        [Test]
        public void Build_AggregatesDuplicateInputsById()
        {
            // 同一物品的多条输入应在 Build 时按 ItemId 合并为一条（保留首次出现顺序），
            // 否则 HasAllInputs 会对每条独立校验、ConsumeInputs 逐条扣除 → 少付材料/凭空多产。
            Recipe recipe = Recipe.Create("wall")
                .AddInput("wood", 5)
                .AddInput("nail", 2)
                .AddInput("wood", 5)
                .AddOutput("wall", 1)
                .Build();

            Assert.AreEqual(2, recipe.Inputs.Count);
            Assert.AreEqual("wood", recipe.Inputs[0].ItemId);
            Assert.AreEqual(10, recipe.Inputs[0].Count);
            Assert.AreEqual("nail", recipe.Inputs[1].ItemId);
            Assert.AreEqual(2, recipe.Inputs[1].Count);
        }

        [Test]
        public void WithTime_Negative_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Recipe.Create("x").WithTime(-1f));
        }
    }
}
