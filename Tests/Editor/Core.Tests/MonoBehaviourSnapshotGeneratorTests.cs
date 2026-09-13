//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using EjoyFramework.Core.Unity.Editor.CodeGen;
using EjoyFramework.Core.Serialization;
using EjoyFramework.Core.Unity;
using EjoyFramework.Tests.CodeGenSamples;
using EjoyFramework.Tests.CodeGenSamples.MonoBehaviourSnapshots;
using NUnit.Framework;
using UnityEngine;

namespace EjoyFramework.Tests
{
    [TestFixture]
    public sealed class MonoBehaviourSnapshotGeneratorTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void Run_WritesSnapshotImplementationForMonoBehaviourFieldStyles()
        {
            string outputDir = Path.Combine("Temp", "CodeGenTests", Guid.NewGuid().ToString("N"));
            try
            {
                var generator = new MonoBehaviourSnapshotGenerator(
                    outputDir,
                    new[] { typeof(MonoBehaviourSnapshotSampleComponent) });

                CodeGenResult result = generator.Run();
                string outputPath = Path.Combine(
                    outputDir,
                    CodeGenTypeUtil.GeneratedFileName(typeof(MonoBehaviourSnapshotSampleComponent)));

                Assert.That(result.Written, Is.EqualTo(1));
                Assert.That(result.Skipped, Is.EqualTo(0));
                Assert.That(File.Exists(outputPath), Is.True);

                string source = File.ReadAllText(outputPath);
                StringAssert.Contains("partial class MonoBehaviourSnapshotSampleComponent : IMonoBehaviourSnapshot", source);
                StringAssert.Contains("buffer.WriteDouble(this.DoubleValue);", source);
                StringAssert.Contains("buffer.WriteByte((System.Byte)this.Mode);", source);
                StringAssert.Contains("buffer.WriteFloat(this.Vector3Value.x);", source);
                StringAssert.Contains("new UnityEngine.Vector3(buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat())", source);
                StringAssert.Contains("this.Payload = new EjoyFramework.Tests.CodeGenSamples.SnapshotNestedPayload();", source);
                StringAssert.Contains("RequireObjectReferenceResolver(context, \"Target\")", source);
                StringAssert.Contains("this.Target = (UnityEngine.GameObject)", source);
                StringAssert.Contains("this.m_PrivateScores", source);
            }
            finally
            {
                if (Directory.Exists(outputDir))
                {
                    Directory.Delete(outputDir, true);
                }
            }
        }

        [Test]
        public void CaptureAndRestore_RoundTripsActualMonoBehaviourState()
        {
            var host = new GameObject("snapshot-host");
            var targetA = new GameObject("snapshot-target-a");
            var targetB = new GameObject("snapshot-target-b");
            string phase = "setup";

            try
            {
                var component = host.AddComponent<MonoBehaviourSnapshotSampleComponent>();
                phase = "fill";
                FillOriginalState(component, targetA, targetB);

                phase = "resolver";
                var resolver = new TestUnityObjectReferenceResolver();
                resolver.Register("target-a", targetA);
                resolver.Register("target-b", targetB);
                var context = new MonoBehaviourSnapshotContext(resolver);

                ByteBuffer buffer = ByteBuffer.Acquire();
                try
                {
                    phase = "capture";
                    ((IMonoBehaviourSnapshot)component).CaptureSnapshot(buffer, context);
                    phase = "mutate";
                    MutateState(component);
                    phase = "rewind";
                    buffer.RewindRead();
                    phase = "restore";
                    ((IMonoBehaviourSnapshot)component).RestoreSnapshot(buffer, context);
                }
                finally
                {
                    buffer.Release();
                }

                phase = "assert";
                AssertOriginalState(component, targetA, targetB);
            }
            catch (Exception ex)
            {
                Assert.Fail("MonoBehaviour snapshot test failed during phase '" + phase + "': " + ex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(targetA);
                UnityEngine.Object.DestroyImmediate(targetB);
            }
        }

        [Test]
        public void CaptureAndRestore_RoundTripsFormalGameMonoBehaviourSamples()
        {
            var heroGo = new GameObject("formal-snapshot-hero");
            var bossGo = new GameObject("formal-snapshot-boss");
            var hubGo = new GameObject("formal-snapshot-hub");
            var weaponGo = new GameObject("formal-snapshot-weapon");
            var markerA = new GameObject("formal-snapshot-marker-a");
            var markerB = new GameObject("formal-snapshot-marker-b");
            var anchorA = new GameObject("formal-snapshot-anchor-a");
            var anchorB = new GameObject("formal-snapshot-anchor-b");
            var cameraGo = new GameObject("formal-snapshot-camera");
            string phase = "setup";

            try
            {
                var hero = heroGo.AddComponent<SnapshotHeroBehaviour>();
                var boss = bossGo.AddComponent<SnapshotBossBehaviour>();
                var hub = hubGo.AddComponent<SnapshotReferenceHubBehaviour>();
                var camera = cameraGo.AddComponent<Camera>();

                phase = "fill";
                FillFormalHero(hero, weaponGo, markerA, markerB, anchorA.transform, boss);
                FillFormalBoss(boss, weaponGo, markerB, anchorB.transform, hero);
                FillFormalHub(hub, hero, boss, markerA, markerB, anchorA.transform, anchorB.transform, camera);

                phase = "resolver";
                var resolver = new TestUnityObjectReferenceResolver();
                resolver.Register("hero", hero);
                resolver.Register("boss", boss);
                resolver.Register("weapon", weaponGo);
                resolver.Register("marker-a", markerA);
                resolver.Register("marker-b", markerB);
                resolver.Register("anchor-a", anchorA.transform);
                resolver.Register("anchor-b", anchorB.transform);
                resolver.Register("camera", camera);
                var context = new MonoBehaviourSnapshotContext(resolver);

                ByteBuffer buffer = ByteBuffer.Acquire();
                try
                {
                    phase = "capture";
                    ((IMonoBehaviourSnapshot)hero).CaptureSnapshot(buffer, context);
                    ((IMonoBehaviourSnapshot)boss).CaptureSnapshot(buffer, context);
                    ((IMonoBehaviourSnapshot)hub).CaptureSnapshot(buffer, context);

                    phase = "mutate";
                    MutateFormalHero(hero);
                    MutateFormalBoss(boss);
                    MutateFormalHub(hub);

                    phase = "restore";
                    buffer.RewindRead();
                    ((IMonoBehaviourSnapshot)hero).RestoreSnapshot(buffer, context);
                    ((IMonoBehaviourSnapshot)boss).RestoreSnapshot(buffer, context);
                    ((IMonoBehaviourSnapshot)hub).RestoreSnapshot(buffer, context);
                }
                finally
                {
                    buffer.Release();
                }

                phase = "assert";
                AssertFormalHero(hero, weaponGo, markerA, markerB, anchorA.transform, boss);
                AssertFormalBoss(boss, weaponGo, markerB, anchorB.transform, hero);
                AssertFormalHub(hub, hero, boss, markerA, markerB, anchorA.transform, anchorB.transform, camera);
            }
            catch (Exception ex)
            {
                Assert.Fail("Formal MonoBehaviour snapshot test failed during phase '" + phase + "': " + ex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(heroGo);
                UnityEngine.Object.DestroyImmediate(bossGo);
                UnityEngine.Object.DestroyImmediate(hubGo);
                UnityEngine.Object.DestroyImmediate(weaponGo);
                UnityEngine.Object.DestroyImmediate(markerA);
                UnityEngine.Object.DestroyImmediate(markerB);
                UnityEngine.Object.DestroyImmediate(anchorA);
                UnityEngine.Object.DestroyImmediate(anchorB);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        private static void FillFormalHero(
            SnapshotHeroBehaviour hero,
            GameObject weapon,
            GameObject markerA,
            GameObject markerB,
            Transform anchor,
            SnapshotActorBase assistTarget)
        {
            hero.ActorId = 101;
            hero.ActorName = "Nezha";
            hero.RuntimeCacheVersion = 7001;
            hero.NonSerializedCounter = 8001;
            hero.SetBaseSnapshotState(
                new Vector3(1f, 2f, 3f),
                Stats(10, 110.5f, "base-hero", SnapshotFaction.Guard),
                new List<int> { 3, 5, 8 });
            hero.Faction = SnapshotFaction.Guard;
            hero.AbilityFlags = SnapshotAbilityFlags.Melee | SnapshotAbilityFlags.Ultimate;
            hero.IsElite = true;
            hero.Grade = 'S';
            hero.Slot = 2;
            hero.Alignment = -4;
            hero.Armor = 120;
            hero.ArmorPierce = 31;
            hero.ScoreSeed = 123456u;
            hero.SpawnToken = 9876543210ul;
            hero.MoveInput = new Vector2(0.25f, 0.75f);
            hero.Tuning = new Vector4(1f, 2f, 3f, 4f);
            hero.AimRotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
            hero.AuraColor = new Color(0.8f, 0.2f, 0.1f, 1f);
            hero.TeamColor = new Color32(10, 20, 30, 40);
            hero.PatrolRect = new Rect(5f, 6f, 7f, 8f);
            hero.GridCell = new Vector2Int(9, 10);
            hero.GridVolume = new Vector3Int(11, 12, 13);
            hero.DamageHistory = new[] { 5, 8, 13 };
            hero.PatrolPath = new[] { new Vector3(14f, 15f, 16f), new Vector3(17f, 18f, 19f) };
            hero.Tags = new List<string> { "front", null, "fire" };
            hero.AttackRange = Range(2, 9);
            hero.SkillRanges = new List<SnapshotRange> { Range(3, 6), Range(7, 11) };
            hero.WeaponPrefab = weapon;
            hero.AimTarget = anchor;
            hero.AssistTarget = assistTarget;
            hero.SetCombatSnapshotState(
                Stats(11, 210.5f, "combat-hero", SnapshotFaction.Guard),
                new List<SnapshotStats>
                {
                    Stats(12, 220.5f, "loadout-a", SnapshotFaction.Guard),
                    null,
                    Stats(13, 230.5f, "loadout-b", SnapshotFaction.Guard),
                },
                new List<Vector2Int> { new Vector2Int(20, 21), new Vector2Int(22, 23) });
            hero.HeroKey = "hero-nezhaguard";
            hero.CooldownMultiplier = 1.75d;
            hero.EquipmentSlots = new List<GameObject> { markerA, null, markerB };
            hero.UpgradeStats = new[]
            {
                Stats(14, 240.5f, "upgrade-a", SnapshotFaction.Guard),
                null,
                Stats(15, 250.5f, "upgrade-b", SnapshotFaction.Guard),
            };
            hero.SetHeroPrivateSnapshotState(
                Stats(16, 260.5f, "private-growth", SnapshotFaction.Guard),
                new List<int> { 34, 55, 89 });
        }

        private static void FillFormalBoss(
            SnapshotBossBehaviour boss,
            GameObject weapon,
            GameObject markerB,
            Transform anchor,
            SnapshotHeroBehaviour hero)
        {
            boss.ActorId = 201;
            boss.ActorName = "DragonKing";
            boss.SetBaseSnapshotState(
                new Vector3(31f, 32f, 33f),
                Stats(20, 310.5f, "base-boss", SnapshotFaction.Demon),
                new List<int> { 21, 34, 55 });
            boss.Faction = SnapshotFaction.Demon;
            boss.AbilityFlags = SnapshotAbilityFlags.Control | SnapshotAbilityFlags.Ranged;
            boss.IsElite = false;
            boss.Grade = 'A';
            boss.Slot = 9;
            boss.Alignment = 5;
            boss.Armor = 320;
            boss.ArmorPierce = 61;
            boss.ScoreSeed = 654321u;
            boss.SpawnToken = 1234567890ul;
            boss.MoveInput = new Vector2(0.5f, 0.125f);
            boss.Tuning = new Vector4(41f, 42f, 43f, 44f);
            boss.AimRotation = new Quaternion(0.4f, 0.3f, 0.2f, 0.1f);
            boss.AuraColor = new Color(0.1f, 0.3f, 0.9f, 1f);
            boss.TeamColor = new Color32(50, 60, 70, 80);
            boss.PatrolRect = new Rect(45f, 46f, 47f, 48f);
            boss.GridCell = new Vector2Int(49, 50);
            boss.GridVolume = new Vector3Int(51, 52, 53);
            boss.DamageHistory = new[] { 144, 233 };
            boss.PatrolPath = new[] { new Vector3(54f, 55f, 56f) };
            boss.Tags = new List<string> { "boss", "water" };
            boss.AttackRange = Range(12, 19);
            boss.SkillRanges = new List<SnapshotRange> { Range(13, 16), Range(17, 21) };
            boss.WeaponPrefab = weapon;
            boss.AimTarget = anchor;
            boss.AssistTarget = hero;
            boss.SetCombatSnapshotState(
                Stats(22, 410.5f, "combat-boss", SnapshotFaction.Demon),
                new List<SnapshotStats> { Stats(23, 420.5f, "boss-loadout", SnapshotFaction.Demon) },
                new List<Vector2Int> { new Vector2Int(57, 58) });
            boss.Phase = 4;
            boss.EnrageSeconds = 99.25d;
            boss.Summons = new[] { hero, null };
            boss.ReserveSummons = new List<SnapshotHeroBehaviour> { null, hero };
            boss.ThreatTable = new SnapshotActorBase[] { boss, hero };
            boss.ThreatList = new List<SnapshotActorBase> { hero, null, boss };
            boss.PhaseColors = new[] { new Color(0.2f, 0.4f, 0.6f, 0.8f), Color.red };
            boss.ArenaCells = new List<Vector2Int> { new Vector2Int(59, 60), new Vector2Int(61, 62) };
            boss.PhaseStats = new[]
            {
                Stats(24, 430.5f, "phase-a", SnapshotFaction.Demon),
                Stats(25, 440.5f, "phase-b", SnapshotFaction.Demon),
            };
            boss.SetBossPrivateSnapshotState(
                Stats(26, 450.5f, "private-boss", SnapshotFaction.Demon),
                new List<SnapshotRange> { Range(23, 29), Range(31, 37) },
                markerB);
            boss.RuntimeCacheVersion = 7002;
            boss.NonSerializedCounter = 8002;
        }

        private static void FillFormalHub(
            SnapshotReferenceHubBehaviour hub,
            SnapshotHeroBehaviour hero,
            SnapshotBossBehaviour boss,
            GameObject markerA,
            GameObject markerB,
            Transform anchorA,
            Transform anchorB,
            Camera camera)
        {
            hub.PrimaryActor = boss;
            hub.Actors = new List<SnapshotActorBase> { hero, null, boss };
            hub.Heroes = new[] { hero, null };
            hub.Boss = boss;
            hub.Markers = new[] { markerA, null, markerB };
            hub.MarkerList = new List<GameObject> { markerB, markerA };
            hub.Anchors = new[] { anchorA, anchorB };
            hub.CameraRef = camera;
            hub.SetPrivateReferenceState(hero, new List<SnapshotBossBehaviour> { boss, null });
        }

        private static void MutateFormalHero(SnapshotHeroBehaviour hero)
        {
            hero.ActorId = -1;
            hero.ActorName = "mutated";
            hero.RuntimeCacheVersion = -7001;
            hero.NonSerializedCounter = -8001;
            hero.SetBaseSnapshotState(Vector3.zero, null, null);
            hero.Faction = SnapshotFaction.Neutral;
            hero.AbilityFlags = SnapshotAbilityFlags.None;
            hero.IsElite = false;
            hero.Grade = 'x';
            hero.Slot = 0;
            hero.Alignment = 0;
            hero.Armor = 0;
            hero.ArmorPierce = 0;
            hero.ScoreSeed = 0u;
            hero.SpawnToken = 0ul;
            hero.MoveInput = Vector2.zero;
            hero.Tuning = Vector4.zero;
            hero.AimRotation = Quaternion.identity;
            hero.AuraColor = Color.clear;
            hero.TeamColor = new Color32(0, 0, 0, 0);
            hero.PatrolRect = Rect.zero;
            hero.GridCell = Vector2Int.zero;
            hero.GridVolume = Vector3Int.zero;
            hero.DamageHistory = null;
            hero.PatrolPath = null;
            hero.Tags = null;
            hero.AttackRange = default;
            hero.SkillRanges = null;
            hero.WeaponPrefab = null;
            hero.AimTarget = null;
            hero.AssistTarget = null;
            hero.SetCombatSnapshotState(null, null, null);
            hero.HeroKey = null;
            hero.CooldownMultiplier = 0d;
            hero.EquipmentSlots = null;
            hero.UpgradeStats = null;
            hero.SetHeroPrivateSnapshotState(null, null);
        }

        private static void MutateFormalBoss(SnapshotBossBehaviour boss)
        {
            boss.ActorId = -2;
            boss.ActorName = "mutated";
            boss.RuntimeCacheVersion = -7002;
            boss.NonSerializedCounter = -8002;
            boss.SetBaseSnapshotState(Vector3.zero, null, null);
            boss.Faction = SnapshotFaction.Neutral;
            boss.AbilityFlags = SnapshotAbilityFlags.None;
            boss.IsElite = true;
            boss.Grade = 'z';
            boss.Slot = 0;
            boss.Alignment = 0;
            boss.Armor = 0;
            boss.ArmorPierce = 0;
            boss.ScoreSeed = 0u;
            boss.SpawnToken = 0ul;
            boss.MoveInput = Vector2.zero;
            boss.Tuning = Vector4.zero;
            boss.AimRotation = Quaternion.identity;
            boss.AuraColor = Color.clear;
            boss.TeamColor = new Color32(0, 0, 0, 0);
            boss.PatrolRect = Rect.zero;
            boss.GridCell = Vector2Int.zero;
            boss.GridVolume = Vector3Int.zero;
            boss.DamageHistory = null;
            boss.PatrolPath = null;
            boss.Tags = null;
            boss.AttackRange = default;
            boss.SkillRanges = null;
            boss.WeaponPrefab = null;
            boss.AimTarget = null;
            boss.AssistTarget = null;
            boss.SetCombatSnapshotState(null, null, null);
            boss.Phase = 0;
            boss.EnrageSeconds = 0d;
            boss.Summons = null;
            boss.ReserveSummons = null;
            boss.ThreatTable = null;
            boss.ThreatList = null;
            boss.PhaseColors = null;
            boss.ArenaCells = null;
            boss.PhaseStats = null;
            boss.SetBossPrivateSnapshotState(null, null, null);
        }

        private static void MutateFormalHub(SnapshotReferenceHubBehaviour hub)
        {
            hub.PrimaryActor = null;
            hub.Actors = null;
            hub.Heroes = null;
            hub.Boss = null;
            hub.Markers = null;
            hub.MarkerList = null;
            hub.Anchors = null;
            hub.CameraRef = null;
            hub.SetPrivateReferenceState(null, null);
        }

        private static void AssertFormalHero(
            SnapshotHeroBehaviour hero,
            GameObject weapon,
            GameObject markerA,
            GameObject markerB,
            Transform anchor,
            SnapshotActorBase assistTarget)
        {
            Assert.That(hero.ActorId, Is.EqualTo(101));
            Assert.That(hero.ActorName, Is.EqualTo("Nezha"));
            Assert.That(hero.RuntimeCacheVersion, Is.EqualTo(-7001));
            Assert.That(hero.NonSerializedCounter, Is.EqualTo(-8001));
            AssertVector3(hero.SpawnPoint, new Vector3(1f, 2f, 3f));
            AssertStats(hero.BaseStats, 10, 110.5f, "base-hero", SnapshotFaction.Guard);
            CollectionAssert.AreEqual(new[] { 3, 5, 8 }, hero.BaseBuffIds);
            Assert.That(hero.Faction, Is.EqualTo(SnapshotFaction.Guard));
            Assert.That(hero.AbilityFlags, Is.EqualTo(SnapshotAbilityFlags.Melee | SnapshotAbilityFlags.Ultimate));
            Assert.That(hero.IsElite, Is.True);
            Assert.That(hero.Grade, Is.EqualTo('S'));
            Assert.That(hero.Slot, Is.EqualTo(2));
            Assert.That(hero.Alignment, Is.EqualTo(-4));
            Assert.That(hero.Armor, Is.EqualTo(120));
            Assert.That(hero.ArmorPierce, Is.EqualTo(31));
            Assert.That(hero.ScoreSeed, Is.EqualTo(123456u));
            Assert.That(hero.SpawnToken, Is.EqualTo(9876543210ul));
            AssertVector2(hero.MoveInput, new Vector2(0.25f, 0.75f));
            AssertVector4(hero.Tuning, new Vector4(1f, 2f, 3f, 4f));
            AssertQuaternion(hero.AimRotation, new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
            AssertColor(hero.AuraColor, new Color(0.8f, 0.2f, 0.1f, 1f));
            Assert.That(hero.TeamColor, Is.EqualTo(new Color32(10, 20, 30, 40)));
            AssertRect(hero.PatrolRect, new Rect(5f, 6f, 7f, 8f));
            Assert.That(hero.GridCell, Is.EqualTo(new Vector2Int(9, 10)));
            Assert.That(hero.GridVolume, Is.EqualTo(new Vector3Int(11, 12, 13)));
            CollectionAssert.AreEqual(new[] { 5, 8, 13 }, hero.DamageHistory);
            AssertVector3(hero.PatrolPath[0], new Vector3(14f, 15f, 16f));
            AssertVector3(hero.PatrolPath[1], new Vector3(17f, 18f, 19f));
            CollectionAssert.AreEqual(new List<string> { "front", null, "fire" }, hero.Tags);
            AssertRange(hero.AttackRange, 2, 9);
            AssertRange(hero.SkillRanges[0], 3, 6);
            AssertRange(hero.SkillRanges[1], 7, 11);
            Assert.That(hero.WeaponPrefab, Is.SameAs(weapon));
            Assert.That(hero.AimTarget, Is.SameAs(anchor));
            Assert.That(hero.AssistTarget, Is.SameAs(assistTarget));
            AssertStats(hero.CombatStats, 11, 210.5f, "combat-hero", SnapshotFaction.Guard);
            AssertStats(hero.LoadoutStats[0], 12, 220.5f, "loadout-a", SnapshotFaction.Guard);
            Assert.That(hero.LoadoutStats[1], Is.Null);
            AssertStats(hero.LoadoutStats[2], 13, 230.5f, "loadout-b", SnapshotFaction.Guard);
            Assert.That(hero.ControlCells[0], Is.EqualTo(new Vector2Int(20, 21)));
            Assert.That(hero.ControlCells[1], Is.EqualTo(new Vector2Int(22, 23)));
            Assert.That(hero.HeroKey, Is.EqualTo("hero-nezhaguard"));
            Assert.That(hero.CooldownMultiplier, Is.EqualTo(1.75d).Within(Epsilon));
            Assert.That(hero.EquipmentSlots[0], Is.SameAs(markerA));
            Assert.That(hero.EquipmentSlots[1], Is.Null);
            Assert.That(hero.EquipmentSlots[2], Is.SameAs(markerB));
            AssertStats(hero.UpgradeStats[0], 14, 240.5f, "upgrade-a", SnapshotFaction.Guard);
            Assert.That(hero.UpgradeStats[1], Is.Null);
            AssertStats(hero.UpgradeStats[2], 15, 250.5f, "upgrade-b", SnapshotFaction.Guard);
            AssertStats(hero.PrivateGrowthStats, 16, 260.5f, "private-growth", SnapshotFaction.Guard);
            CollectionAssert.AreEqual(new[] { 34, 55, 89 }, hero.PrivateTalentIds);
        }

        private static void AssertFormalBoss(
            SnapshotBossBehaviour boss,
            GameObject weapon,
            GameObject markerB,
            Transform anchor,
            SnapshotHeroBehaviour hero)
        {
            Assert.That(boss.ActorId, Is.EqualTo(201));
            Assert.That(boss.ActorName, Is.EqualTo("DragonKing"));
            Assert.That(boss.RuntimeCacheVersion, Is.EqualTo(-7002));
            Assert.That(boss.NonSerializedCounter, Is.EqualTo(-8002));
            AssertVector3(boss.SpawnPoint, new Vector3(31f, 32f, 33f));
            AssertStats(boss.BaseStats, 20, 310.5f, "base-boss", SnapshotFaction.Demon);
            CollectionAssert.AreEqual(new[] { 21, 34, 55 }, boss.BaseBuffIds);
            Assert.That(boss.Faction, Is.EqualTo(SnapshotFaction.Demon));
            Assert.That(boss.AbilityFlags, Is.EqualTo(SnapshotAbilityFlags.Control | SnapshotAbilityFlags.Ranged));
            Assert.That(boss.IsElite, Is.False);
            Assert.That(boss.Grade, Is.EqualTo('A'));
            Assert.That(boss.Slot, Is.EqualTo(9));
            Assert.That(boss.Alignment, Is.EqualTo(5));
            Assert.That(boss.Armor, Is.EqualTo(320));
            Assert.That(boss.ArmorPierce, Is.EqualTo(61));
            Assert.That(boss.ScoreSeed, Is.EqualTo(654321u));
            Assert.That(boss.SpawnToken, Is.EqualTo(1234567890ul));
            AssertVector2(boss.MoveInput, new Vector2(0.5f, 0.125f));
            AssertVector4(boss.Tuning, new Vector4(41f, 42f, 43f, 44f));
            AssertQuaternion(boss.AimRotation, new Quaternion(0.4f, 0.3f, 0.2f, 0.1f));
            AssertColor(boss.AuraColor, new Color(0.1f, 0.3f, 0.9f, 1f));
            Assert.That(boss.TeamColor, Is.EqualTo(new Color32(50, 60, 70, 80)));
            AssertRect(boss.PatrolRect, new Rect(45f, 46f, 47f, 48f));
            Assert.That(boss.GridCell, Is.EqualTo(new Vector2Int(49, 50)));
            Assert.That(boss.GridVolume, Is.EqualTo(new Vector3Int(51, 52, 53)));
            CollectionAssert.AreEqual(new[] { 144, 233 }, boss.DamageHistory);
            AssertVector3(boss.PatrolPath[0], new Vector3(54f, 55f, 56f));
            CollectionAssert.AreEqual(new List<string> { "boss", "water" }, boss.Tags);
            AssertRange(boss.AttackRange, 12, 19);
            AssertRange(boss.SkillRanges[0], 13, 16);
            AssertRange(boss.SkillRanges[1], 17, 21);
            Assert.That(boss.WeaponPrefab, Is.SameAs(weapon));
            Assert.That(boss.AimTarget, Is.SameAs(anchor));
            Assert.That(boss.AssistTarget, Is.SameAs(hero));
            AssertStats(boss.CombatStats, 22, 410.5f, "combat-boss", SnapshotFaction.Demon);
            AssertStats(boss.LoadoutStats[0], 23, 420.5f, "boss-loadout", SnapshotFaction.Demon);
            Assert.That(boss.ControlCells[0], Is.EqualTo(new Vector2Int(57, 58)));
            Assert.That(boss.Phase, Is.EqualTo(4));
            Assert.That(boss.EnrageSeconds, Is.EqualTo(99.25d).Within(Epsilon));
            Assert.That(boss.Summons[0], Is.SameAs(hero));
            Assert.That(boss.Summons[1], Is.Null);
            Assert.That(boss.ReserveSummons[0], Is.Null);
            Assert.That(boss.ReserveSummons[1], Is.SameAs(hero));
            Assert.That(boss.ThreatTable[0], Is.SameAs(boss));
            Assert.That(boss.ThreatTable[1], Is.SameAs(hero));
            Assert.That(boss.ThreatList[0], Is.SameAs(hero));
            Assert.That(boss.ThreatList[1], Is.Null);
            Assert.That(boss.ThreatList[2], Is.SameAs(boss));
            AssertColor(boss.PhaseColors[0], new Color(0.2f, 0.4f, 0.6f, 0.8f));
            AssertColor(boss.PhaseColors[1], Color.red);
            Assert.That(boss.ArenaCells[0], Is.EqualTo(new Vector2Int(59, 60)));
            Assert.That(boss.ArenaCells[1], Is.EqualTo(new Vector2Int(61, 62)));
            AssertStats(boss.PhaseStats[0], 24, 430.5f, "phase-a", SnapshotFaction.Demon);
            AssertStats(boss.PhaseStats[1], 25, 440.5f, "phase-b", SnapshotFaction.Demon);
            AssertStats(boss.BossStats, 26, 450.5f, "private-boss", SnapshotFaction.Demon);
            AssertRange(boss.EnrageRanges[0], 23, 29);
            AssertRange(boss.EnrageRanges[1], 31, 37);
            Assert.That(boss.PrivateArenaMarker, Is.SameAs(markerB));
        }

        private static void AssertFormalHub(
            SnapshotReferenceHubBehaviour hub,
            SnapshotHeroBehaviour hero,
            SnapshotBossBehaviour boss,
            GameObject markerA,
            GameObject markerB,
            Transform anchorA,
            Transform anchorB,
            Camera camera)
        {
            Assert.That(hub.PrimaryActor, Is.SameAs(boss));
            Assert.That(hub.Actors[0], Is.SameAs(hero));
            Assert.That(hub.Actors[1], Is.Null);
            Assert.That(hub.Actors[2], Is.SameAs(boss));
            Assert.That(hub.Heroes[0], Is.SameAs(hero));
            Assert.That(hub.Heroes[1], Is.Null);
            Assert.That(hub.Boss, Is.SameAs(boss));
            Assert.That(hub.Markers[0], Is.SameAs(markerA));
            Assert.That(hub.Markers[1], Is.Null);
            Assert.That(hub.Markers[2], Is.SameAs(markerB));
            Assert.That(hub.MarkerList[0], Is.SameAs(markerB));
            Assert.That(hub.MarkerList[1], Is.SameAs(markerA));
            Assert.That(hub.Anchors[0], Is.SameAs(anchorA));
            Assert.That(hub.Anchors[1], Is.SameAs(anchorB));
            Assert.That(hub.CameraRef, Is.SameAs(camera));
            Assert.That(hub.PrivateActor, Is.SameAs(hero));
            Assert.That(hub.PrivateBosses[0], Is.SameAs(boss));
            Assert.That(hub.PrivateBosses[1], Is.Null);
        }

        private static SnapshotStats Stats(int level, float health, string title, SnapshotFaction faction)
        {
            return new SnapshotStats
            {
                Level = level,
                Health = health,
                Title = title,
                Faction = faction,
            };
        }

        private static SnapshotRange Range(short min, short max)
        {
            return new SnapshotRange { Min = min, Max = max };
        }

        private static void AssertStats(
            SnapshotStats stats,
            int level,
            float health,
            string title,
            SnapshotFaction faction)
        {
            Assert.That(stats, Is.Not.Null);
            Assert.That(stats.Level, Is.EqualTo(level));
            Assert.That(stats.Health, Is.EqualTo(health).Within(Epsilon));
            Assert.That(stats.Title, Is.EqualTo(title));
            Assert.That(stats.Faction, Is.EqualTo(faction));
        }

        private static void AssertRange(SnapshotRange range, short min, short max)
        {
            Assert.That(range.Min, Is.EqualTo(min));
            Assert.That(range.Max, Is.EqualTo(max));
        }

        private static void FillOriginalState(
            MonoBehaviourSnapshotSampleComponent component,
            GameObject targetA,
            GameObject targetB)
        {
            component.IntValue = 42;
            component.LongValue = 9000000000L;
            component.FloatValue = 1.25f;
            component.DoubleValue = 9.5d;
            component.BoolValue = true;
            component.CharValue = 'Z';
            component.StringValue = "chentang";
            component.Mode = SnapshotSampleMode.Active;
            component.Vector2Value = new Vector2(1f, 2f);
            component.Vector3Value = new Vector3(3f, 4f, 5f);
            component.Vector4Value = new Vector4(6f, 7f, 8f, 9f);
            component.Vector2IntValue = new Vector2Int(10, 11);
            component.Vector3IntValue = new Vector3Int(12, 13, 14);
            component.RotationValue = new Quaternion(0.1f, 0.2f, 0.3f, 0.4f);
            component.ColorValue = new Color(0.2f, 0.3f, 0.4f, 0.5f);
            component.Color32Value = new Color32(1, 2, 3, 4);
            component.RectValue = new Rect(15f, 16f, 17f, 18f);
            component.IntArray = new[] { 21, 22, 23 };
            component.Vector3Array = new[] { new Vector3(24f, 25f, 26f), new Vector3(27f, 28f, 29f) };
            component.StringList = new List<string> { "li", "na", null };
            component.Payload = new SnapshotNestedPayload { Id = 31, Label = "main", Weight = 32.5f };
            component.PayloadList = new List<SnapshotNestedPayload>
            {
                new SnapshotNestedPayload { Id = 33, Label = "first", Weight = 34.5f },
                null,
                new SnapshotNestedPayload { Id = 35, Label = "second", Weight = 36.5f },
            };
            component.Target = targetA;
            component.Targets = new List<GameObject> { targetA, null, targetB };
            component.SetPrivateState(37, new Vector3(38f, 39f, 40f), new List<int> { 41, 42 });
        }

        private static void MutateState(MonoBehaviourSnapshotSampleComponent component)
        {
            component.IntValue = -1;
            component.LongValue = -2L;
            component.FloatValue = -3f;
            component.DoubleValue = -4d;
            component.BoolValue = false;
            component.CharValue = 'x';
            component.StringValue = "mutated";
            component.Mode = SnapshotSampleMode.Paused;
            component.Vector2Value = Vector2.zero;
            component.Vector3Value = Vector3.zero;
            component.Vector4Value = Vector4.zero;
            component.Vector2IntValue = Vector2Int.zero;
            component.Vector3IntValue = Vector3Int.zero;
            component.RotationValue = Quaternion.identity;
            component.ColorValue = Color.clear;
            component.Color32Value = new Color32(0, 0, 0, 0);
            component.RectValue = Rect.zero;
            component.IntArray = null;
            component.Vector3Array = null;
            component.StringList = null;
            component.Payload = null;
            component.PayloadList = null;
            component.Target = null;
            component.Targets = null;
            component.SetPrivateState(0, Vector3.zero, null);
        }

        private static void AssertOriginalState(
            MonoBehaviourSnapshotSampleComponent component,
            GameObject targetA,
            GameObject targetB)
        {
            Assert.That(component.IntValue, Is.EqualTo(42));
            Assert.That(component.LongValue, Is.EqualTo(9000000000L));
            Assert.That(component.FloatValue, Is.EqualTo(1.25f).Within(Epsilon));
            Assert.That(component.DoubleValue, Is.EqualTo(9.5d).Within(Epsilon));
            Assert.That(component.BoolValue, Is.True);
            Assert.That(component.CharValue, Is.EqualTo('Z'));
            Assert.That(component.StringValue, Is.EqualTo("chentang"));
            Assert.That(component.Mode, Is.EqualTo(SnapshotSampleMode.Active));
            AssertVector2(component.Vector2Value, new Vector2(1f, 2f));
            AssertVector3(component.Vector3Value, new Vector3(3f, 4f, 5f));
            AssertVector4(component.Vector4Value, new Vector4(6f, 7f, 8f, 9f));
            Assert.That(component.Vector2IntValue, Is.EqualTo(new Vector2Int(10, 11)));
            Assert.That(component.Vector3IntValue, Is.EqualTo(new Vector3Int(12, 13, 14)));
            AssertQuaternion(component.RotationValue, new Quaternion(0.1f, 0.2f, 0.3f, 0.4f));
            AssertColor(component.ColorValue, new Color(0.2f, 0.3f, 0.4f, 0.5f));
            Assert.That(component.Color32Value, Is.EqualTo(new Color32(1, 2, 3, 4)));
            AssertRect(component.RectValue, new Rect(15f, 16f, 17f, 18f));
            CollectionAssert.AreEqual(new[] { 21, 22, 23 }, component.IntArray);
            AssertVector3(component.Vector3Array[0], new Vector3(24f, 25f, 26f));
            AssertVector3(component.Vector3Array[1], new Vector3(27f, 28f, 29f));
            CollectionAssert.AreEqual(new List<string> { "li", "na", null }, component.StringList);
            AssertPayload(component.Payload, 31, "main", 32.5f);
            AssertPayload(component.PayloadList[0], 33, "first", 34.5f);
            Assert.That(component.PayloadList[1], Is.Null);
            AssertPayload(component.PayloadList[2], 35, "second", 36.5f);
            Assert.That(component.Target, Is.SameAs(targetA));
            Assert.That(component.Targets[0], Is.SameAs(targetA));
            Assert.That(component.Targets[1], Is.Null);
            Assert.That(component.Targets[2], Is.SameAs(targetB));
            Assert.That(component.PrivateInt, Is.EqualTo(37));
            AssertVector3(component.PrivatePosition, new Vector3(38f, 39f, 40f));
            CollectionAssert.AreEqual(new[] { 41, 42 }, component.PrivateScores);
        }

        private static void AssertPayload(SnapshotNestedPayload payload, int id, string label, float weight)
        {
            Assert.That(payload, Is.Not.Null);
            Assert.That(payload.Id, Is.EqualTo(id));
            Assert.That(payload.Label, Is.EqualTo(label));
            Assert.That(payload.Weight, Is.EqualTo(weight).Within(Epsilon));
        }

        private static void AssertVector2(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
        }

        private static void AssertVector4(Vector4 actual, Vector4 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(Epsilon));
        }

        private static void AssertQuaternion(Quaternion actual, Quaternion expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(Epsilon));
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Epsilon));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Epsilon));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Epsilon));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(Epsilon));
        }

        private static void AssertRect(Rect actual, Rect expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(Epsilon));
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(Epsilon));
        }

        private sealed class TestUnityObjectReferenceResolver : IUnityObjectReferenceResolver
        {
            private readonly Dictionary<string, UnityEngine.Object> m_ObjectByReference
                = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
            private readonly Dictionary<int, string> m_ReferenceByObject = new Dictionary<int, string>();

            public void Register(string reference, UnityEngine.Object value)
            {
                m_ObjectByReference[reference] = value;
                m_ReferenceByObject[System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value)] = reference;
            }

            public string ToReference(UnityEngine.Object value)
            {
                if (value == null) return null;
                int objectKey = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
                if (m_ReferenceByObject.TryGetValue(objectKey, out string reference)) return reference;
                throw new InvalidOperationException("Object is not registered: " + value.name);
            }

            public UnityEngine.Object FromReference(string reference, Type expectedType)
            {
                if (reference == null) return null;
                if (m_ObjectByReference.TryGetValue(reference, out UnityEngine.Object value)) return value;
                throw new InvalidOperationException("Reference is not registered: " + reference);
            }
        }
    }
}
