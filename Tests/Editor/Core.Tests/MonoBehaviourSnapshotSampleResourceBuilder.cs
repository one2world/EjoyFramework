//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EjoyFramework.Core.Serialization;
using EjoyFramework.Core.Unity;
using EjoyFramework.Tests.CodeGenSamples.MonoBehaviourSnapshots;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Tests
{
    public static class MonoBehaviourSnapshotSampleResourceBuilder
    {
        public const string ResourceDir = "Packages/com.ejoy.framework/Tests/Runtime/Core.Tests.PlayMode/CodeGenSamples/MonoBehaviourSnapshots/Resources";
        public const string PrefabName = "SnapshotSampleArena";
        public const string PrefabPath = ResourceDir + "/" + PrefabName + ".prefab";
        public const string GoldenPath = ResourceDir + "/" + PrefabName + ".golden.json";

        [MenuItem("EjoyFramework.Tests/CodeGen/Rebuild MonoBehaviour Snapshot Sample Resources")]
        public static void RebuildAssets()
        {
            EjoyFramework.Core.Unity.Editor.CodeGen.CodeGenPath.RequireWritable(ResourceDir);
            Directory.CreateDirectory(EjoyFramework.Core.Unity.Editor.CodeGen.CodeGenPath.Resolve(ResourceDir));

            GameObject root = CreateArenaInstance();
            try
            {
                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Failed to save prefab: " + PrefabPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException("Failed to reload prefab: " + PrefabPath);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                byte[] snapshot = CaptureArenaSnapshot(instance);
                File.WriteAllText(EjoyFramework.Core.Unity.Editor.CodeGen.CodeGenPath.Resolve(GoldenPath), BuildReportJson(instance, snapshot), Encoding.UTF8);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            AssetDatabase.ImportAsset(GoldenPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static GameObject CreateArenaInstance()
        {
            var root = new GameObject(PrefabName);
            var hub = root.AddComponent<SnapshotReferenceHubBehaviour>();

            GameObject heroGo = CreateChild(root.transform, "Hero");
            GameObject bossGo = CreateChild(root.transform, "Boss");
            GameObject weaponGo = CreateChild(root.transform, "Weapon");
            GameObject markerAGo = CreateChild(root.transform, "MarkerA");
            GameObject markerBGo = CreateChild(root.transform, "MarkerB");
            GameObject anchorAGo = CreateChild(root.transform, "AnchorA");
            GameObject anchorBGo = CreateChild(root.transform, "AnchorB");
            GameObject cameraGo = CreateChild(root.transform, "CameraRig");

            var hero = heroGo.AddComponent<SnapshotHeroBehaviour>();
            var boss = bossGo.AddComponent<SnapshotBossBehaviour>();
            var camera = cameraGo.AddComponent<Camera>();

            FillHero(hero, weaponGo, markerAGo, markerBGo, anchorAGo.transform, boss);
            FillBoss(boss, weaponGo, markerBGo, anchorBGo.transform, hero);
            FillHub(hub, hero, boss, markerAGo, markerBGo, anchorAGo.transform, anchorBGo.transform, camera);

            return root;
        }

        public static SnapshotSampleObjectMap CreateObjectMap(GameObject root)
        {
            return SnapshotSampleObjectMap.FromArena(root);
        }

        public static byte[] CaptureArenaSnapshot(GameObject root)
        {
            SnapshotSampleObjectMap map = CreateObjectMap(root);
            ByteBuffer buffer = ByteBuffer.Acquire();
            try
            {
                var context = new MonoBehaviourSnapshotContext(map);
                ((IMonoBehaviourSnapshot)map.Hero).CaptureSnapshot(buffer, context);
                ((IMonoBehaviourSnapshot)map.Boss).CaptureSnapshot(buffer, context);
                ((IMonoBehaviourSnapshot)map.Hub).CaptureSnapshot(buffer, context);
                return buffer.ToArray();
            }
            finally
            {
                buffer.Release();
            }
        }

        public static void RestoreArenaSnapshot(GameObject root, byte[] snapshot)
        {
            SnapshotSampleObjectMap map = CreateObjectMap(root);
            ByteBuffer buffer = ByteBuffer.Acquire(snapshot);
            try
            {
                var context = new MonoBehaviourSnapshotContext(map);
                ((IMonoBehaviourSnapshot)map.Hero).RestoreSnapshot(buffer, context);
                ((IMonoBehaviourSnapshot)map.Boss).RestoreSnapshot(buffer, context);
                ((IMonoBehaviourSnapshot)map.Hub).RestoreSnapshot(buffer, context);
            }
            finally
            {
                buffer.Release();
            }
        }

        public static void MutateArena(GameObject root)
        {
            SnapshotSampleObjectMap map = CreateObjectMap(root);
            MutateHero(map.Hero);
            MutateBoss(map.Boss);
            MutateHub(map.Hub);
        }

        public static string BuildReportJson(GameObject root, byte[] snapshot)
        {
            SnapshotSampleObjectMap map = CreateObjectMap(root);
            var report = new ArenaReport
            {
                prefab = PrefabName,
                snapshot = new SnapshotReport
                {
                    length = snapshot == null ? 0 : snapshot.Length,
                    sha256 = Sha256(snapshot),
                },
                hero = BuildHeroReport(map.Hero, map),
                boss = BuildBossReport(map.Boss, map),
                hub = BuildHubReport(map.Hub, map),
            };

            return JsonUtility.ToJson(report, true).Replace("\r\n", "\n") + "\n";
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void FillHero(
            SnapshotHeroBehaviour hero,
            GameObject weapon,
            GameObject markerA,
            GameObject markerB,
            Transform anchor,
            SnapshotActorBase assistTarget)
        {
            hero.ActorId = 301;
            hero.ActorName = "AssetNezha";
            hero.RuntimeCacheVersion = 9101;
            hero.NonSerializedCounter = 9201;
            hero.SetBaseSnapshotState(
                new Vector3(1.5f, 2.5f, 3.5f),
                Stats(30, 301.5f, "asset-base-hero", SnapshotFaction.Guard),
                new List<int> { 101, 103, 107 });
            hero.Faction = SnapshotFaction.Guard;
            hero.AbilityFlags = SnapshotAbilityFlags.Melee | SnapshotAbilityFlags.Control | SnapshotAbilityFlags.Ultimate;
            hero.IsElite = true;
            hero.Grade = 'H';
            hero.Slot = 7;
            hero.Alignment = -8;
            hero.Armor = 222;
            hero.ArmorPierce = 45;
            hero.ScoreSeed = 345678u;
            hero.SpawnToken = 3456789012ul;
            hero.MoveInput = new Vector2(0.125f, 0.875f);
            hero.Tuning = new Vector4(2f, 4f, 6f, 8f);
            hero.AimRotation = new Quaternion(0.11f, 0.22f, 0.33f, 0.88f);
            hero.AuraColor = new Color(0.7f, 0.25f, 0.15f, 0.95f);
            hero.TeamColor = new Color32(11, 22, 33, 44);
            hero.PatrolRect = new Rect(15f, 16f, 17f, 18f);
            hero.GridCell = new Vector2Int(19, 20);
            hero.GridVolume = new Vector3Int(21, 22, 23);
            hero.DamageHistory = new[] { 34, 55, 89 };
            hero.PatrolPath = new[] { new Vector3(24f, 25f, 26f), new Vector3(27f, 28f, 29f) };
            hero.Tags = new List<string> { "asset-front", null, "asset-fire" };
            hero.AttackRange = Range(4, 14);
            hero.SkillRanges = new List<SnapshotRange> { Range(5, 9), Range(10, 15) };
            hero.WeaponPrefab = weapon;
            hero.AimTarget = anchor;
            hero.AssistTarget = assistTarget;
            hero.SetCombatSnapshotState(
                Stats(31, 311.5f, "asset-combat-hero", SnapshotFaction.Guard),
                new List<SnapshotStats>
                {
                    Stats(32, 321.5f, "asset-loadout-a", SnapshotFaction.Guard),
                    null,
                    Stats(33, 331.5f, "asset-loadout-b", SnapshotFaction.Guard),
                },
                new List<Vector2Int> { new Vector2Int(40, 41), new Vector2Int(42, 43) });
            hero.HeroKey = "asset-hero-nezhaguard";
            hero.CooldownMultiplier = 2.25d;
            hero.EquipmentSlots = new List<GameObject> { markerA, null, markerB };
            hero.UpgradeStats = new[]
            {
                Stats(34, 341.5f, "asset-upgrade-a", SnapshotFaction.Guard),
                null,
                Stats(35, 351.5f, "asset-upgrade-b", SnapshotFaction.Guard),
            };
            hero.SetHeroPrivateSnapshotState(
                Stats(36, 361.5f, "asset-private-growth", SnapshotFaction.Guard),
                new List<int> { 144, 233, 377 });
        }

        private static void FillBoss(
            SnapshotBossBehaviour boss,
            GameObject weapon,
            GameObject markerB,
            Transform anchor,
            SnapshotHeroBehaviour hero)
        {
            boss.ActorId = 401;
            boss.ActorName = "AssetDragonKing";
            boss.RuntimeCacheVersion = 9102;
            boss.NonSerializedCounter = 9202;
            boss.SetBaseSnapshotState(
                new Vector3(41f, 42f, 43f),
                Stats(40, 401.5f, "asset-base-boss", SnapshotFaction.Demon),
                new List<int> { 159, 265, 358 });
            boss.Faction = SnapshotFaction.Demon;
            boss.AbilityFlags = SnapshotAbilityFlags.Ranged | SnapshotAbilityFlags.Control;
            boss.IsElite = false;
            boss.Grade = 'B';
            boss.Slot = 11;
            boss.Alignment = 9;
            boss.Armor = 444;
            boss.ArmorPierce = 88;
            boss.ScoreSeed = 876543u;
            boss.SpawnToken = 2109876543ul;
            boss.MoveInput = new Vector2(0.625f, 0.375f);
            boss.Tuning = new Vector4(12f, 14f, 16f, 18f);
            boss.AimRotation = new Quaternion(0.44f, 0.33f, 0.22f, 0.77f);
            boss.AuraColor = new Color(0.15f, 0.35f, 0.85f, 0.9f);
            boss.TeamColor = new Color32(55, 66, 77, 88);
            boss.PatrolRect = new Rect(51f, 52f, 53f, 54f);
            boss.GridCell = new Vector2Int(55, 56);
            boss.GridVolume = new Vector3Int(57, 58, 59);
            boss.DamageHistory = new[] { 610, 987 };
            boss.PatrolPath = new[] { new Vector3(60f, 61f, 62f), new Vector3(63f, 64f, 65f) };
            boss.Tags = new List<string> { "asset-boss", "asset-water" };
            boss.AttackRange = Range(16, 29);
            boss.SkillRanges = new List<SnapshotRange> { Range(17, 23), Range(24, 30) };
            boss.WeaponPrefab = weapon;
            boss.AimTarget = anchor;
            boss.AssistTarget = hero;
            boss.SetCombatSnapshotState(
                Stats(41, 411.5f, "asset-combat-boss", SnapshotFaction.Demon),
                new List<SnapshotStats> { Stats(42, 421.5f, "asset-boss-loadout", SnapshotFaction.Demon) },
                new List<Vector2Int> { new Vector2Int(70, 71), new Vector2Int(72, 73) });
            boss.Phase = 6;
            boss.EnrageSeconds = 123.5d;
            boss.Summons = new[] { hero, null };
            boss.ReserveSummons = new List<SnapshotHeroBehaviour> { null, hero };
            boss.ThreatTable = new SnapshotActorBase[] { boss, hero };
            boss.ThreatList = new List<SnapshotActorBase> { hero, null, boss };
            boss.PhaseColors = new[] { new Color(0.25f, 0.45f, 0.65f, 0.85f), Color.magenta };
            boss.ArenaCells = new List<Vector2Int> { new Vector2Int(74, 75), new Vector2Int(76, 77) };
            boss.PhaseStats = new[]
            {
                Stats(43, 431.5f, "asset-phase-a", SnapshotFaction.Demon),
                Stats(44, 441.5f, "asset-phase-b", SnapshotFaction.Demon),
            };
            boss.SetBossPrivateSnapshotState(
                Stats(45, 451.5f, "asset-private-boss", SnapshotFaction.Demon),
                new List<SnapshotRange> { Range(31, 37), Range(41, 47) },
                markerB);
        }

        private static void FillHub(
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

        private static void MutateHero(SnapshotHeroBehaviour hero)
        {
            hero.ActorId = -301;
            hero.ActorName = "mutated-resource-hero";
            hero.RuntimeCacheVersion = -9101;
            hero.NonSerializedCounter = -9201;
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

        private static void MutateBoss(SnapshotBossBehaviour boss)
        {
            boss.ActorId = -401;
            boss.ActorName = "mutated-resource-boss";
            boss.RuntimeCacheVersion = -9102;
            boss.NonSerializedCounter = -9202;
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

        private static void MutateHub(SnapshotReferenceHubBehaviour hub)
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

        private static HeroReport BuildHeroReport(SnapshotHeroBehaviour hero, SnapshotSampleObjectMap map)
        {
            return new HeroReport
            {
                actorId = hero.ActorId,
                actorName = hero.ActorName,
                spawnPoint = Vec(hero.SpawnPoint),
                baseStats = StatsReport.From(hero.BaseStats),
                baseBuffIds = ToArray(hero.BaseBuffIds),
                faction = hero.Faction.ToString(),
                abilityFlags = hero.AbilityFlags.ToString(),
                isElite = hero.IsElite,
                grade = hero.Grade.ToString(),
                slot = hero.Slot,
                alignment = hero.Alignment,
                armor = hero.Armor,
                armorPierce = hero.ArmorPierce,
                scoreSeed = hero.ScoreSeed.ToString(CultureInfo.InvariantCulture),
                spawnToken = hero.SpawnToken.ToString(CultureInfo.InvariantCulture),
                moveInput = Vec(hero.MoveInput),
                tuning = Vec(hero.Tuning),
                aimRotation = Quat(hero.AimRotation),
                auraColor = ColorValue(hero.AuraColor),
                teamColor = ColorValue(hero.TeamColor),
                patrolRect = RectValue(hero.PatrolRect),
                gridCell = Vec(hero.GridCell),
                gridVolume = Vec(hero.GridVolume),
                damageHistory = hero.DamageHistory,
                patrolPath = VecArray(hero.PatrolPath),
                tags = ToArray(hero.Tags),
                attackRange = RangeReport.From(hero.AttackRange),
                skillRanges = RangeArray(hero.SkillRanges),
                weaponPrefab = map.ToReference(hero.WeaponPrefab),
                aimTarget = map.ToReference(hero.AimTarget),
                assistTarget = map.ToReference(hero.AssistTarget),
                combatStats = StatsReport.From(hero.CombatStats),
                loadoutStats = StatsArray(hero.LoadoutStats),
                controlCells = VecArray(hero.ControlCells),
                heroKey = hero.HeroKey,
                cooldownMultiplier = D(hero.CooldownMultiplier),
                equipmentSlots = RefArray(hero.EquipmentSlots, map),
                upgradeStats = StatsArray(hero.UpgradeStats),
                privateGrowthStats = StatsReport.From(hero.PrivateGrowthStats),
                privateTalentIds = ToArray(hero.PrivateTalentIds),
            };
        }

        private static BossReport BuildBossReport(SnapshotBossBehaviour boss, SnapshotSampleObjectMap map)
        {
            return new BossReport
            {
                actorId = boss.ActorId,
                actorName = boss.ActorName,
                spawnPoint = Vec(boss.SpawnPoint),
                baseStats = StatsReport.From(boss.BaseStats),
                baseBuffIds = ToArray(boss.BaseBuffIds),
                faction = boss.Faction.ToString(),
                abilityFlags = boss.AbilityFlags.ToString(),
                isElite = boss.IsElite,
                grade = boss.Grade.ToString(),
                slot = boss.Slot,
                alignment = boss.Alignment,
                armor = boss.Armor,
                armorPierce = boss.ArmorPierce,
                scoreSeed = boss.ScoreSeed.ToString(CultureInfo.InvariantCulture),
                spawnToken = boss.SpawnToken.ToString(CultureInfo.InvariantCulture),
                moveInput = Vec(boss.MoveInput),
                tuning = Vec(boss.Tuning),
                aimRotation = Quat(boss.AimRotation),
                auraColor = ColorValue(boss.AuraColor),
                teamColor = ColorValue(boss.TeamColor),
                patrolRect = RectValue(boss.PatrolRect),
                gridCell = Vec(boss.GridCell),
                gridVolume = Vec(boss.GridVolume),
                damageHistory = boss.DamageHistory,
                patrolPath = VecArray(boss.PatrolPath),
                tags = ToArray(boss.Tags),
                attackRange = RangeReport.From(boss.AttackRange),
                skillRanges = RangeArray(boss.SkillRanges),
                weaponPrefab = map.ToReference(boss.WeaponPrefab),
                aimTarget = map.ToReference(boss.AimTarget),
                assistTarget = map.ToReference(boss.AssistTarget),
                combatStats = StatsReport.From(boss.CombatStats),
                loadoutStats = StatsArray(boss.LoadoutStats),
                controlCells = VecArray(boss.ControlCells),
                phase = boss.Phase,
                enrageSeconds = D(boss.EnrageSeconds),
                summons = RefArray(boss.Summons, map),
                reserveSummons = RefArray(boss.ReserveSummons, map),
                threatTable = RefArray(boss.ThreatTable, map),
                threatList = RefArray(boss.ThreatList, map),
                phaseColors = ColorArray(boss.PhaseColors),
                arenaCells = VecArray(boss.ArenaCells),
                phaseStats = StatsArray(boss.PhaseStats),
                bossStats = StatsReport.From(boss.BossStats),
                enrageRanges = RangeArray(boss.EnrageRanges),
                privateArenaMarker = map.ToReference(boss.PrivateArenaMarker),
            };
        }

        private static HubReport BuildHubReport(SnapshotReferenceHubBehaviour hub, SnapshotSampleObjectMap map)
        {
            return new HubReport
            {
                primaryActor = map.ToReference(hub.PrimaryActor),
                actors = RefArray(hub.Actors, map),
                heroes = RefArray(hub.Heroes, map),
                boss = map.ToReference(hub.Boss),
                markers = RefArray(hub.Markers, map),
                markerList = RefArray(hub.MarkerList, map),
                anchors = RefArray(hub.Anchors, map),
                cameraRef = map.ToReference(hub.CameraRef),
                privateActor = map.ToReference(hub.PrivateActor),
                privateBosses = RefArray(hub.PrivateBosses, map),
            };
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

        private static string Sha256(byte[] value)
        {
            if (value == null) return null;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(value);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        private static int[] ToArray(List<int> value)
        {
            return value == null ? null : value.ToArray();
        }

        private static string[] ToArray(List<string> value)
        {
            return value == null ? null : value.ToArray();
        }

        private static string[] RefArray<T>(IList<T> values, SnapshotSampleObjectMap map) where T : UnityEngine.Object
        {
            if (values == null) return null;
            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = map.ToReference(values[i]);
            }
            return result;
        }

        private static string[] RefArray<T>(T[] values, SnapshotSampleObjectMap map) where T : UnityEngine.Object
        {
            if (values == null) return null;
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = map.ToReference(values[i]);
            }
            return result;
        }

        private static string[] VecArray(Vector3[] values)
        {
            if (values == null) return null;
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = Vec(values[i]);
            return result;
        }

        private static string[] VecArray(List<Vector2Int> values)
        {
            if (values == null) return null;
            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = Vec(values[i]);
            return result;
        }

        private static StatsReport[] StatsArray(IList<SnapshotStats> values)
        {
            if (values == null) return null;
            var result = new StatsReport[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = StatsReport.From(values[i]);
            return result;
        }

        private static StatsReport[] StatsArray(SnapshotStats[] values)
        {
            if (values == null) return null;
            var result = new StatsReport[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = StatsReport.From(values[i]);
            return result;
        }

        private static RangeReport[] RangeArray(IList<SnapshotRange> values)
        {
            if (values == null) return null;
            var result = new RangeReport[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = RangeReport.From(values[i]);
            return result;
        }

        private static string[] ColorArray(Color[] values)
        {
            if (values == null) return null;
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = ColorValue(values[i]);
            return result;
        }

        private static string Vec(Vector2 value)
        {
            return F(value.x) + "," + F(value.y);
        }

        private static string Vec(Vector3 value)
        {
            return F(value.x) + "," + F(value.y) + "," + F(value.z);
        }

        private static string Vec(Vector4 value)
        {
            return F(value.x) + "," + F(value.y) + "," + F(value.z) + "," + F(value.w);
        }

        private static string Vec(Vector2Int value)
        {
            return value.x.ToString(CultureInfo.InvariantCulture) + "," + value.y.ToString(CultureInfo.InvariantCulture);
        }

        private static string Vec(Vector3Int value)
        {
            return value.x.ToString(CultureInfo.InvariantCulture)
                + "," + value.y.ToString(CultureInfo.InvariantCulture)
                + "," + value.z.ToString(CultureInfo.InvariantCulture);
        }

        private static string Quat(Quaternion value)
        {
            return F(value.x) + "," + F(value.y) + "," + F(value.z) + "," + F(value.w);
        }

        private static string ColorValue(Color value)
        {
            return F(value.r) + "," + F(value.g) + "," + F(value.b) + "," + F(value.a);
        }

        private static string ColorValue(Color32 value)
        {
            return value.r.ToString(CultureInfo.InvariantCulture)
                + "," + value.g.ToString(CultureInfo.InvariantCulture)
                + "," + value.b.ToString(CultureInfo.InvariantCulture)
                + "," + value.a.ToString(CultureInfo.InvariantCulture);
        }

        private static string RectValue(Rect value)
        {
            return F(value.x) + "," + F(value.y) + "," + F(value.width) + "," + F(value.height);
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string D(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public sealed class SnapshotSampleObjectMap : IUnityObjectReferenceResolver
        {
            private readonly Dictionary<string, UnityEngine.Object> m_ObjectByReference
                = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
            private readonly Dictionary<int, string> m_ReferenceByObject = new Dictionary<int, string>();

            private SnapshotSampleObjectMap()
            {
            }

            public SnapshotHeroBehaviour Hero { get; private set; }
            public SnapshotBossBehaviour Boss { get; private set; }
            public SnapshotReferenceHubBehaviour Hub { get; private set; }

            public static SnapshotSampleObjectMap FromArena(GameObject root)
            {
                if (root == null) throw new ArgumentNullException(nameof(root));

                var map = new SnapshotSampleObjectMap();
                map.Hub = root.GetComponent<SnapshotReferenceHubBehaviour>();
                map.Hero = RequireChild(root, "Hero").GetComponent<SnapshotHeroBehaviour>();
                map.Boss = RequireChild(root, "Boss").GetComponent<SnapshotBossBehaviour>();

                map.Register("component:hero", map.Hero);
                map.Register("component:boss", map.Boss);
                map.Register("gameobject:weapon", RequireChild(root, "Weapon"));
                map.Register("gameobject:marker-a", RequireChild(root, "MarkerA"));
                map.Register("gameobject:marker-b", RequireChild(root, "MarkerB"));
                map.Register("transform:anchor-a", RequireChild(root, "AnchorA").transform);
                map.Register("transform:anchor-b", RequireChild(root, "AnchorB").transform);
                map.Register("component:camera", RequireChild(root, "CameraRig").GetComponent<Camera>());

                return map;
            }

            public string ToReference(UnityEngine.Object value)
            {
                if (value == null) return null;
                int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
                if (m_ReferenceByObject.TryGetValue(key, out string reference)) return reference;
                throw new InvalidOperationException("Object is not registered in snapshot sample resource map: " + value.name);
            }

            public UnityEngine.Object FromReference(string reference, Type expectedType)
            {
                if (reference == null) return null;
                if (!m_ObjectByReference.TryGetValue(reference, out UnityEngine.Object value))
                {
                    throw new InvalidOperationException("Reference is not registered in snapshot sample resource map: " + reference);
                }
                if (expectedType != null && value != null && !expectedType.IsInstanceOfType(value))
                {
                    throw new InvalidOperationException(
                        "Reference '" + reference + "' resolved to " + value.GetType().FullName
                        + " but expected " + expectedType.FullName + ".");
                }
                return value;
            }

            private static GameObject RequireChild(GameObject root, string childName)
            {
                Transform child = root.transform.Find(childName);
                if (child == null)
                {
                    throw new InvalidOperationException("Missing child '" + childName + "' under " + root.name + ".");
                }
                return child.gameObject;
            }

            private void Register(string reference, UnityEngine.Object value)
            {
                if (value == null) throw new ArgumentNullException(nameof(value), reference);
                m_ObjectByReference[reference] = value;
                m_ReferenceByObject[System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value)] = reference;
            }
        }

        [Serializable]
        private sealed class ArenaReport
        {
            public string prefab;
            public SnapshotReport snapshot;
            public HeroReport hero;
            public BossReport boss;
            public HubReport hub;
        }

        [Serializable]
        private sealed class SnapshotReport
        {
            public int length;
            public string sha256;
        }

        [Serializable]
        private sealed class HeroReport
        {
            public int actorId;
            public string actorName;
            public string spawnPoint;
            public StatsReport baseStats;
            public int[] baseBuffIds;
            public string faction;
            public string abilityFlags;
            public bool isElite;
            public string grade;
            public int slot;
            public int alignment;
            public int armor;
            public int armorPierce;
            public string scoreSeed;
            public string spawnToken;
            public string moveInput;
            public string tuning;
            public string aimRotation;
            public string auraColor;
            public string teamColor;
            public string patrolRect;
            public string gridCell;
            public string gridVolume;
            public int[] damageHistory;
            public string[] patrolPath;
            public string[] tags;
            public RangeReport attackRange;
            public RangeReport[] skillRanges;
            public string weaponPrefab;
            public string aimTarget;
            public string assistTarget;
            public StatsReport combatStats;
            public StatsReport[] loadoutStats;
            public string[] controlCells;
            public string heroKey;
            public string cooldownMultiplier;
            public string[] equipmentSlots;
            public StatsReport[] upgradeStats;
            public StatsReport privateGrowthStats;
            public int[] privateTalentIds;
        }

        [Serializable]
        private sealed class BossReport
        {
            public int actorId;
            public string actorName;
            public string spawnPoint;
            public StatsReport baseStats;
            public int[] baseBuffIds;
            public string faction;
            public string abilityFlags;
            public bool isElite;
            public string grade;
            public int slot;
            public int alignment;
            public int armor;
            public int armorPierce;
            public string scoreSeed;
            public string spawnToken;
            public string moveInput;
            public string tuning;
            public string aimRotation;
            public string auraColor;
            public string teamColor;
            public string patrolRect;
            public string gridCell;
            public string gridVolume;
            public int[] damageHistory;
            public string[] patrolPath;
            public string[] tags;
            public RangeReport attackRange;
            public RangeReport[] skillRanges;
            public string weaponPrefab;
            public string aimTarget;
            public string assistTarget;
            public StatsReport combatStats;
            public StatsReport[] loadoutStats;
            public string[] controlCells;
            public int phase;
            public string enrageSeconds;
            public string[] summons;
            public string[] reserveSummons;
            public string[] threatTable;
            public string[] threatList;
            public string[] phaseColors;
            public string[] arenaCells;
            public StatsReport[] phaseStats;
            public StatsReport bossStats;
            public RangeReport[] enrageRanges;
            public string privateArenaMarker;
        }

        [Serializable]
        private sealed class HubReport
        {
            public string primaryActor;
            public string[] actors;
            public string[] heroes;
            public string boss;
            public string[] markers;
            public string[] markerList;
            public string[] anchors;
            public string cameraRef;
            public string privateActor;
            public string[] privateBosses;
        }

        [Serializable]
        private sealed class StatsReport
        {
            public int level;
            public string health;
            public string title;
            public string faction;

            public static StatsReport From(SnapshotStats value)
            {
                if (value == null) return null;
                return new StatsReport
                {
                    level = value.Level,
                    health = F(value.Health),
                    title = value.Title,
                    faction = value.Faction.ToString(),
                };
            }
        }

        [Serializable]
        private sealed class RangeReport
        {
            public int min;
            public int max;

            public static RangeReport From(SnapshotRange value)
            {
                return new RangeReport { min = value.Min, max = value.Max };
            }
        }
    }
}
