//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Factions;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Factions
{
    /// <summary>
    /// 针对 <see cref="FactionRelations"/> 关系解析、对称性、显式覆盖与默认值的单元测试。
    /// </summary>
    [TestFixture]
    public class FactionRelationsTests
    {
        [Test]
        public void SameFaction_DefaultsToAlly()
        {
            FactionRelations relations = new FactionRelations();

            Assert.AreEqual(FactionRelation.Ally, relations.GetRelation(1, 1));
            Assert.IsTrue(relations.AreAllies(1, 1));
            Assert.IsFalse(relations.AreEnemies(1, 1));
        }

        [Test]
        public void DifferentFaction_DefaultsToEnemy()
        {
            FactionRelations relations = new FactionRelations();

            Assert.AreEqual(FactionRelation.Enemy, relations.GetRelation(1, 2));
            Assert.IsTrue(relations.AreEnemies(1, 2));
            Assert.IsFalse(relations.AreAllies(1, 2));
        }

        [Test]
        public void DefaultBetweenDifferent_Neutral_ChangesUnsetDifferentPairs()
        {
            FactionRelations relations = new FactionRelations();
            relations.SetDefaultBetweenDifferent(FactionRelation.Neutral);

            Assert.AreEqual(FactionRelation.Neutral, relations.GetRelation(1, 2));
            Assert.IsTrue(relations.AreNeutral(1, 2));

            // 同 Id 仍默认友方，不受不同阵营默认值影响。
            Assert.AreEqual(FactionRelation.Ally, relations.GetRelation(3, 3));
        }

        [Test]
        public void DefaultBetweenDifferent_CanBeChangedAfterConstruction()
        {
            FactionRelations relations = new FactionRelations();
            Assert.AreEqual(FactionRelation.Enemy, relations.GetRelation(1, 2));

            relations.DefaultBetweenDifferent = FactionRelation.Neutral;

            Assert.AreEqual(FactionRelation.Neutral, relations.GetRelation(1, 2));
        }

        [Test]
        public void SetRelation_IsSymmetric()
        {
            FactionRelations relations = new FactionRelations();

            relations.SetRelation(1, 2, FactionRelation.Ally);

            Assert.AreEqual(FactionRelation.Ally, relations.GetRelation(1, 2));
            Assert.AreEqual(FactionRelation.Ally, relations.GetRelation(2, 1));
            Assert.IsTrue(relations.AreAllies(2, 1));
        }

        [Test]
        public void SetRelation_OverridesDefaultBetweenDifferent()
        {
            FactionRelations relations = new FactionRelations();

            relations.SetRelation(5, 6, FactionRelation.Neutral);

            Assert.AreEqual(FactionRelation.Neutral, relations.GetRelation(5, 6));
            Assert.IsTrue(relations.AreNeutral(5, 6));
        }

        [Test]
        public void SetRelation_SameId_ToEnemy_EnablesFreeForAll()
        {
            FactionRelations relations = new FactionRelations();

            // 显式覆盖对同 Id 同样生效：大混战（FFA）阵营成员互相敌对。
            relations.SetRelation(7, 7, FactionRelation.Enemy);

            Assert.AreEqual(FactionRelation.Enemy, relations.GetRelation(7, 7));
            Assert.IsTrue(relations.AreEnemies(7, 7));
            Assert.IsFalse(relations.AreAllies(7, 7));
        }

        [Test]
        public void AreEnemies_AreAllies_AreNeutral_ReflectExplicitRelation()
        {
            FactionRelations relations = new FactionRelations();

            relations.SetRelation(1, 2, FactionRelation.Enemy);
            relations.SetRelation(1, 3, FactionRelation.Ally);
            relations.SetRelation(1, 4, FactionRelation.Neutral);

            Assert.IsTrue(relations.AreEnemies(1, 2));
            Assert.IsFalse(relations.AreAllies(1, 2));
            Assert.IsFalse(relations.AreNeutral(1, 2));

            Assert.IsTrue(relations.AreAllies(1, 3));
            Assert.IsFalse(relations.AreEnemies(1, 3));

            Assert.IsTrue(relations.AreNeutral(1, 4));
            Assert.IsFalse(relations.AreEnemies(1, 4));
        }

        [Test]
        public void ClearRelation_RevertsToDefault_AndReportsExistence()
        {
            FactionRelations relations = new FactionRelations();
            relations.SetRelation(1, 2, FactionRelation.Ally);
            Assert.AreEqual(FactionRelation.Ally, relations.GetRelation(1, 2));

            // 第一次清除：存在覆盖 → true，且对称（用 (2,1) 清除同一条记录）。
            bool removed = relations.ClearRelation(2, 1);
            Assert.IsTrue(removed);
            Assert.AreEqual(FactionRelation.Enemy, relations.GetRelation(1, 2));

            // 再次清除：已无覆盖 → false。
            bool removedAgain = relations.ClearRelation(1, 2);
            Assert.IsFalse(removedAgain);
        }

        [Test]
        public void ClearRelation_OnUnsetPair_ReturnsFalse()
        {
            FactionRelations relations = new FactionRelations();

            Assert.IsFalse(relations.ClearRelation(10, 11));
        }

        [Test]
        public void Clear_RemovesAllOverrides_KeepsDefaults()
        {
            FactionRelations relations = new FactionRelations();
            relations.SetRelation(1, 2, FactionRelation.Ally);
            relations.SetRelation(3, 3, FactionRelation.Enemy);

            relations.Clear();

            // 回落到默认：不同阵营 Enemy，同阵营 Ally。
            Assert.AreEqual(FactionRelation.Enemy, relations.GetRelation(1, 2));
            Assert.AreEqual(FactionRelation.Ally, relations.GetRelation(3, 3));
        }
    }
}
