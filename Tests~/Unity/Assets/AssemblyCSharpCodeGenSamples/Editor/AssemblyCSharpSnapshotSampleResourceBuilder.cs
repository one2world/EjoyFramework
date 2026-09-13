//------------------------------------------------------------
// EjoyGame
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
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Tests.AssemblyCSharpCodeGenSamples.Editor
{
    public static class AssemblyCSharpSnapshotSampleResourceBuilder
    {
        public const string ResourceDir = "Assets/AssemblyCSharpCodeGenSamples/TestAssets";
        public const string PrefabName = "AssemblyCSharpSnapshotArena";
        public const string PrefabPath = ResourceDir + "/" + PrefabName + ".prefab";
        public const string GoldenPath = ResourceDir + "/" + PrefabName + ".golden.json";

        [MenuItem("EjoyFramework.Tests/CodeGen/Rebuild Assembly-CSharp Snapshot Sample Resources")]
        public static void RebuildAssets()
        {
            Directory.CreateDirectory(ResourceDir);

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
                RefreshRuntimeData(instance);
                byte[] snapshot = CaptureArenaSnapshot(instance);
                File.WriteAllText(GoldenPath, BuildReportJson(instance, snapshot), Encoding.UTF8);
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
            root.AddComponent<AssemblyCSharpSnapshotHub>();

            CreateChild(root.transform, "Hero").AddComponent<AssemblyCSharpSnapshotHero>();
            CreateChild(root.transform, "Boss").AddComponent<AssemblyCSharpSnapshotBoss>();
            CreateChild(root.transform, "Weapon");
            CreateChild(root.transform, "MarkerA");
            CreateChild(root.transform, "MarkerB");
            CreateChild(root.transform, "AnchorA");
            CreateChild(root.transform, "AnchorB");
            CreateChild(root.transform, "CameraRig").AddComponent<Camera>();

            RefreshRuntimeData(root);
            return root;
        }

        public static void RefreshRuntimeData(GameObject root)
        {
            SnapshotObjectMap map = SnapshotObjectMap.FromArena(root);
            FillHero(map.Hero, map.Weapon, map.MarkerA, map.MarkerB, map.AnchorA, map.Boss);
            FillBoss(map.Boss, map.Weapon, map.MarkerB, map.AnchorB, map.Hero);
            FillHub(map.Hub, map.Hero, map.Boss, map.MarkerA, map.MarkerB, map.AnchorA, map.AnchorB, map.Camera);
        }

        public static byte[] CaptureArenaSnapshot(GameObject root)
        {
            SnapshotObjectMap map = SnapshotObjectMap.FromArena(root);
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
            SnapshotObjectMap map = SnapshotObjectMap.FromArena(root);
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
            SnapshotObjectMap map = SnapshotObjectMap.FromArena(root);
            MutateHero(map.Hero);
            MutateBoss(map.Boss);
            MutateHub(map.Hub);
        }

        public static string BuildReportJson(GameObject root, byte[] snapshot)
        {
            SnapshotObjectMap map = SnapshotObjectMap.FromArena(root);
            var report = new ArenaReport
            {
                prefab = PrefabName,
                snapshot = new SnapshotReport
                {
                    length = snapshot == null ? 0 : snapshot.Length,
                    sha256 = Sha256(snapshot),
                },
                hero = ActorReport.From(map.Hero, map),
                boss = BossReport.From(map.Boss, map),
                hub = HubReport.From(map.Hub, map),
            };

            return JsonUtility.ToJson(report, true).Replace("\r\n", "\n") + "\n";
        }

        public static string BuildIgnoredCounterReport(GameObject root)
        {
            SnapshotObjectMap map = SnapshotObjectMap.FromArena(root);
            return map.Hero.RuntimeCacheVersion.ToString(CultureInfo.InvariantCulture)
                + ","
                + map.Hero.NonSerializedCounter.ToString(CultureInfo.InvariantCulture)
                + ","
                + map.Boss.RuntimeCacheVersion.ToString(CultureInfo.InvariantCulture)
                + ","
                + map.Boss.NonSerializedCounter.ToString(CultureInfo.InvariantCulture);
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void FillHero(
            AssemblyCSharpSnapshotHero hero,
            GameObject weapon,
            GameObject markerA,
            GameObject markerB,
            Transform anchor,
            AssemblyCSharpSnapshotActorBase assistTarget)
        {
            hero.ActorId = 501;
            hero.ActorName = "DefaultAssemblyHero";
            hero.Faction = AssemblyCSharpSnapshotFaction.Guard;
            hero.RuntimeCacheVersion = 5101;
            hero.NonSerializedCounter = 5201;
            hero.SetBaseSnapshotState(
                new Vector3(1.25f, 2.25f, 3.25f),
                Stats(50, 501.5f, "default-base-hero", AssemblyCSharpSnapshotFaction.Guard),
                new List<int> { 13, 21, 34 });
            FillCombat(hero, weapon, anchor, assistTarget, 70);
            hero.HeroKey = "assembly-csharp-hero";
            hero.CooldownMultiplier = 3.5d;
            hero.EquipmentSlots = new List<GameObject> { markerA, null, markerB };
            hero.UpgradeStats = new[]
            {
                Stats(71, 711.5f, "hero-upgrade-a", AssemblyCSharpSnapshotFaction.Guard),
                null,
                Stats(72, 721.5f, "hero-upgrade-b", AssemblyCSharpSnapshotFaction.Guard),
            };
            hero.SetHeroPrivateSnapshotState(
                Stats(73, 731.5f, "hero-private-growth", AssemblyCSharpSnapshotFaction.Guard),
                new List<int> { 55, 89, 144 });
        }

        private static void FillBoss(
            AssemblyCSharpSnapshotBoss boss,
            GameObject weapon,
            GameObject markerB,
            Transform anchor,
            AssemblyCSharpSnapshotHero hero)
        {
            boss.ActorId = 601;
            boss.ActorName = "DefaultAssemblyBoss";
            boss.Faction = AssemblyCSharpSnapshotFaction.Demon;
            boss.RuntimeCacheVersion = 6101;
            boss.NonSerializedCounter = 6201;
            boss.SetBaseSnapshotState(
                new Vector3(4.25f, 5.25f, 6.25f),
                Stats(60, 601.5f, "default-base-boss", AssemblyCSharpSnapshotFaction.Demon),
                new List<int> { 233, 377, 610 });
            FillCombat(boss, weapon, anchor, hero, 90);
            boss.Phase = 5;
            boss.EnrageSeconds = 88.125d;
            boss.Summons = new[] { hero, null };
            boss.ReserveSummons = new List<AssemblyCSharpSnapshotHero> { null, hero };
            boss.ThreatTable = new AssemblyCSharpSnapshotActorBase[] { boss, hero };
            boss.ThreatList = new List<AssemblyCSharpSnapshotActorBase> { hero, null, boss };
            boss.PhaseColors = new[] { new Color(0.35f, 0.45f, 0.55f, 0.65f), Color.cyan };
            boss.ArenaCells = new List<Vector2Int> { new Vector2Int(101, 102), new Vector2Int(103, 104) };
            boss.PhaseStats = new[]
            {
                Stats(91, 911.5f, "boss-phase-a", AssemblyCSharpSnapshotFaction.Demon),
                Stats(92, 921.5f, "boss-phase-b", AssemblyCSharpSnapshotFaction.Demon),
            };
            boss.SetBossPrivateSnapshotState(
                Stats(93, 931.5f, "boss-private", AssemblyCSharpSnapshotFaction.Demon),
                new List<AssemblyCSharpSnapshotRange> { Range(35, 45), Range(55, 65) },
                markerB);
        }

        private static void FillCombat(
            AssemblyCSharpSnapshotCombatantBase actor,
            GameObject weapon,
            Transform anchor,
            AssemblyCSharpSnapshotActorBase assistTarget,
            int seed)
        {
            actor.AbilityFlags = AssemblyCSharpSnapshotAbilityFlags.Melee
                | AssemblyCSharpSnapshotAbilityFlags.Shield
                | AssemblyCSharpSnapshotAbilityFlags.Ultimate;
            actor.IsElite = seed % 2 == 0;
            actor.Grade = (char)('A' + seed % 5);
            actor.Slot = (byte)(seed % 10);
            actor.Alignment = (sbyte)(seed % 7 - 3);
            actor.Armor = (short)(seed + 100);
            actor.ArmorPierce = (ushort)(seed + 30);
            actor.ScoreSeed = (uint)(seed * 1000 + 7);
            actor.SpawnToken = (ulong)(seed * 100000 + 99);
            actor.MoveInput = new Vector2(seed + 0.125f, seed + 0.875f);
            actor.Tuning = new Vector4(seed + 1f, seed + 2f, seed + 3f, seed + 4f);
            actor.AimRotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
            actor.AuraColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            actor.TeamColor = new Color32(12, 34, 56, 78);
            actor.PatrolRect = new Rect(seed + 1f, seed + 2f, seed + 3f, seed + 4f);
            actor.PatrolBounds = new Bounds(new Vector3(seed + 5f, seed + 6f, seed + 7f), new Vector3(8f, 9f, 10f));
            actor.GridRect = new RectInt(seed + 1, seed + 2, seed + 3, seed + 4);
            actor.GridBounds = new BoundsInt(new Vector3Int(seed + 5, seed + 6, seed + 7), new Vector3Int(8, 9, 10));
            actor.TargetMask = new LayerMask { value = (1 << 3) | (1 << 7) };
            actor.LocalMatrix = Matrix4x4.identity;
            actor.LocalMatrix[0, 3] = seed + 11f;
            actor.LocalMatrix[1, 3] = seed + 12f;
            actor.LocalMatrix[2, 3] = seed + 13f;
            actor.GridCell = new Vector2Int(seed + 14, seed + 15);
            actor.GridVolume = new Vector3Int(seed + 16, seed + 17, seed + 18);
            actor.DamageHistory = new[] { seed + 1, seed + 2, seed + 3 };
            actor.PatrolPath = new[] { new Vector3(seed + 19f, seed + 20f, seed + 21f), new Vector3(seed + 22f, seed + 23f, seed + 24f) };
            actor.Tags = new List<string> { "default", null, "seed-" + seed.ToString(CultureInfo.InvariantCulture) };
            actor.NamedPoints = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
            {
                ["Alpha"] = new Vector3(seed + 25f, seed + 26f, seed + 27f),
                ["beta"] = new Vector3(seed + 28f, seed + 29f, seed + 30f),
            };
            actor.UniqueBuffIds = new HashSet<int> { seed + 31, seed + 32, seed + 33 };
            actor.PendingCells = new Queue<Vector2Int>(new[] { new Vector2Int(seed + 34, seed + 35), new Vector2Int(seed + 36, seed + 37) });
            actor.ReservedRects = new Stack<RectInt>();
            actor.ReservedRects.Push(new RectInt(seed + 38, seed + 39, 2, 3));
            actor.ReservedRects.Push(new RectInt(seed + 40, seed + 41, 4, 5));
            actor.AttackRange = Range((short)(seed + 1), (short)(seed + 9));
            actor.SkillRanges = new List<AssemblyCSharpSnapshotRange>
            {
                Range((short)(seed + 2), (short)(seed + 6)),
                Range((short)(seed + 7), (short)(seed + 11)),
            };
            actor.WeaponPrefab = weapon;
            actor.AimTarget = anchor;
            actor.AssistTarget = assistTarget;
            actor.PrimaryEffect = new AssemblyCSharpSnapshotDamageEffect
            {
                Id = seed + 42,
                Damage = seed + 43,
                Element = "fire",
            };
            actor.EffectTimeline = new List<AssemblyCSharpSnapshotManagedEffect>
            {
                new AssemblyCSharpSnapshotBuffEffect
                {
                    Id = seed + 44,
                    Multiplier = 1.5f,
                    Duration = Range((short)(seed + 45), (short)(seed + 46)),
                },
                null,
                new AssemblyCSharpSnapshotDamageEffect
                {
                    Id = seed + 47,
                    Damage = seed + 48,
                    Element = "water",
                },
            };
            actor.GenericEffect = new AssemblyCSharpSnapshotIntEffect { Value = seed + 49, Extra = seed + 50 };
            actor.ConcreteEffect = new AssemblyCSharpSnapshotConcreteDerivedEffect
            {
                BaseValue = seed + 51,
                DerivedValue = seed + 52,
            };
            actor.ConcreteEffects = new List<AssemblyCSharpSnapshotConcreteEffect>
            {
                new AssemblyCSharpSnapshotConcreteEffect { BaseValue = seed + 53 },
                new AssemblyCSharpSnapshotConcreteDerivedEffect { BaseValue = seed + 54, DerivedValue = seed + 55 },
            };
            actor.SetCombatSnapshotState(
                Stats(seed, seed + 0.5f, "combat-" + seed.ToString(CultureInfo.InvariantCulture), actor.Faction),
                new List<AssemblyCSharpSnapshotStats>
                {
                    Stats(seed + 1, seed + 1.5f, "loadout-a", actor.Faction),
                    null,
                    Stats(seed + 2, seed + 2.5f, "loadout-b", actor.Faction),
                },
                new List<Vector2Int> { new Vector2Int(seed + 56, seed + 57), new Vector2Int(seed + 58, seed + 59) });
        }

        private static void FillHub(
            AssemblyCSharpSnapshotHub hub,
            AssemblyCSharpSnapshotHero hero,
            AssemblyCSharpSnapshotBoss boss,
            GameObject markerA,
            GameObject markerB,
            Transform anchorA,
            Transform anchorB,
            Camera camera)
        {
            hub.PrimaryActor = boss;
            hub.Actors = new List<AssemblyCSharpSnapshotActorBase> { hero, null, boss };
            hub.Heroes = new[] { hero, null };
            hub.Boss = boss;
            hub.Markers = new[] { markerA, null, markerB };
            hub.MarkerList = new List<GameObject> { markerB, markerA };
            hub.Anchors = new[] { anchorA, anchorB };
            hub.CameraRef = camera;
            hub.SetPrivateReferenceState(hero, new List<AssemblyCSharpSnapshotBoss> { boss, null });
        }

        private static void MutateHero(AssemblyCSharpSnapshotHero hero)
        {
            MutateCombat(hero);
            hero.ActorId = -501;
            hero.ActorName = "mutated-hero";
            hero.Faction = AssemblyCSharpSnapshotFaction.Neutral;
            hero.RuntimeCacheVersion = -5101;
            hero.NonSerializedCounter = -5201;
            hero.HeroKey = null;
            hero.CooldownMultiplier = 0d;
            hero.EquipmentSlots = null;
            hero.UpgradeStats = null;
            hero.SetHeroPrivateSnapshotState(null, null);
        }

        private static void MutateBoss(AssemblyCSharpSnapshotBoss boss)
        {
            MutateCombat(boss);
            boss.ActorId = -601;
            boss.ActorName = "mutated-boss";
            boss.Faction = AssemblyCSharpSnapshotFaction.Neutral;
            boss.RuntimeCacheVersion = -6101;
            boss.NonSerializedCounter = -6201;
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

        private static void MutateCombat(AssemblyCSharpSnapshotCombatantBase actor)
        {
            actor.SetBaseSnapshotState(Vector3.zero, null, null);
            actor.AbilityFlags = AssemblyCSharpSnapshotAbilityFlags.None;
            actor.IsElite = false;
            actor.Grade = 'x';
            actor.Slot = 0;
            actor.Alignment = 0;
            actor.Armor = 0;
            actor.ArmorPierce = 0;
            actor.ScoreSeed = 0u;
            actor.SpawnToken = 0ul;
            actor.MoveInput = Vector2.zero;
            actor.Tuning = Vector4.zero;
            actor.AimRotation = Quaternion.identity;
            actor.AuraColor = Color.clear;
            actor.TeamColor = new Color32(0, 0, 0, 0);
            actor.PatrolRect = Rect.zero;
            actor.PatrolBounds = default;
            actor.GridRect = default;
            actor.GridBounds = default;
            actor.TargetMask = default;
            actor.LocalMatrix = Matrix4x4.zero;
            actor.GridCell = Vector2Int.zero;
            actor.GridVolume = Vector3Int.zero;
            actor.DamageHistory = null;
            actor.PatrolPath = null;
            actor.Tags = null;
            actor.NamedPoints = null;
            actor.UniqueBuffIds = null;
            actor.PendingCells = null;
            actor.ReservedRects = null;
            actor.AttackRange = default;
            actor.SkillRanges = null;
            actor.WeaponPrefab = null;
            actor.AimTarget = null;
            actor.AssistTarget = null;
            actor.PrimaryEffect = null;
            actor.EffectTimeline = null;
            actor.GenericEffect = null;
            actor.ConcreteEffect = null;
            actor.ConcreteEffects = null;
            actor.SetCombatSnapshotState(null, null, null);
        }

        private static void MutateHub(AssemblyCSharpSnapshotHub hub)
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

        private static AssemblyCSharpSnapshotStats Stats(
            int level,
            float health,
            string label,
            AssemblyCSharpSnapshotFaction faction)
        {
            return new AssemblyCSharpSnapshotStats
            {
                Level = level,
                Health = health,
                Label = label,
                Faction = faction,
            };
        }

        private static AssemblyCSharpSnapshotRange Range(short min, short max)
        {
            return new AssemblyCSharpSnapshotRange { Min = min, Max = max };
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

        public sealed class SnapshotObjectMap : IUnityObjectReferenceResolver
        {
            private readonly Dictionary<string, UnityEngine.Object> m_ObjectByReference
                = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
            private readonly Dictionary<int, string> m_ReferenceByObject = new Dictionary<int, string>();

            private SnapshotObjectMap()
            {
            }

            public AssemblyCSharpSnapshotHero Hero { get; private set; }
            public AssemblyCSharpSnapshotBoss Boss { get; private set; }
            public AssemblyCSharpSnapshotHub Hub { get; private set; }
            public GameObject Weapon { get; private set; }
            public GameObject MarkerA { get; private set; }
            public GameObject MarkerB { get; private set; }
            public Transform AnchorA { get; private set; }
            public Transform AnchorB { get; private set; }
            public Camera Camera { get; private set; }

            public static SnapshotObjectMap FromArena(GameObject root)
            {
                if (root == null) throw new ArgumentNullException(nameof(root));

                var map = new SnapshotObjectMap();
                map.Hub = root.GetComponent<AssemblyCSharpSnapshotHub>();
                map.Hero = RequireChild(root, "Hero").GetComponent<AssemblyCSharpSnapshotHero>();
                map.Boss = RequireChild(root, "Boss").GetComponent<AssemblyCSharpSnapshotBoss>();
                map.Weapon = RequireChild(root, "Weapon");
                map.MarkerA = RequireChild(root, "MarkerA");
                map.MarkerB = RequireChild(root, "MarkerB");
                map.AnchorA = RequireChild(root, "AnchorA").transform;
                map.AnchorB = RequireChild(root, "AnchorB").transform;
                map.Camera = RequireChild(root, "CameraRig").GetComponent<Camera>();

                map.Register("component:hero", map.Hero);
                map.Register("component:boss", map.Boss);
                map.Register("gameobject:weapon", map.Weapon);
                map.Register("gameobject:marker-a", map.MarkerA);
                map.Register("gameobject:marker-b", map.MarkerB);
                map.Register("transform:anchor-a", map.AnchorA);
                map.Register("transform:anchor-b", map.AnchorB);
                map.Register("component:camera", map.Camera);
                return map;
            }

            public string ToReference(UnityEngine.Object value)
            {
                if (value == null) return null;
                int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
                if (m_ReferenceByObject.TryGetValue(key, out string reference)) return reference;
                throw new InvalidOperationException("Object is not registered: " + value.name);
            }

            public UnityEngine.Object FromReference(string reference, Type expectedType)
            {
                if (reference == null) return null;
                if (!m_ObjectByReference.TryGetValue(reference, out UnityEngine.Object value))
                {
                    throw new InvalidOperationException("Reference is not registered: " + reference);
                }
                if (expectedType != null && value != null && !expectedType.IsInstanceOfType(value))
                {
                    throw new InvalidOperationException(
                        "Reference '" + reference + "' resolved to " + value.GetType().FullName
                        + " but expected " + expectedType.FullName + ".");
                }
                return value;
            }

            private void Register(string reference, UnityEngine.Object value)
            {
                if (value == null) throw new ArgumentNullException(nameof(value), reference);
                m_ObjectByReference[reference] = value;
                m_ReferenceByObject[System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value)] = reference;
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
        }

        [Serializable]
        private sealed class ArenaReport
        {
            public string prefab;
            public SnapshotReport snapshot;
            public ActorReport hero;
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
        private class ActorReport
        {
            public int actorId;
            public string actorName;
            public string faction;
            public string spawnPoint;
            public StatsReport baseStats;
            public int[] baseBuffIds;
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
            public string patrolBounds;
            public string gridRect;
            public string gridBounds;
            public int targetMask;
            public string localMatrix;
            public string gridCell;
            public string gridVolume;
            public int[] damageHistory;
            public string[] patrolPath;
            public string[] tags;
            public NamedPointReport[] namedPoints;
            public int[] uniqueBuffIds;
            public string[] pendingCells;
            public string[] reservedRects;
            public RangeReport attackRange;
            public RangeReport[] skillRanges;
            public string weaponPrefab;
            public string aimTarget;
            public string assistTarget;
            public EffectReport primaryEffect;
            public EffectReport[] effectTimeline;
            public EffectReport genericEffect;
            public EffectReport concreteEffect;
            public EffectReport[] concreteEffects;
            public StatsReport combatStats;
            public StatsReport[] loadoutStats;
            public string[] controlCells;
            public string heroKey;
            public string cooldownMultiplier;
            public string[] equipmentSlots;
            public StatsReport[] upgradeStats;
            public StatsReport privateGrowthStats;
            public int[] privateTalentIds;

            public static ActorReport From(AssemblyCSharpSnapshotHero hero, SnapshotObjectMap map)
            {
                ActorReport report = FromCombat(hero, map);
                report.heroKey = hero.HeroKey;
                report.cooldownMultiplier = D(hero.CooldownMultiplier);
                report.equipmentSlots = RefArray(hero.EquipmentSlots, map);
                report.upgradeStats = StatsArray(hero.UpgradeStats);
                report.privateGrowthStats = StatsReport.From(hero.PrivateGrowthStats);
                report.privateTalentIds = ToArray(hero.PrivateTalentIds);
                return report;
            }

            protected static ActorReport FromCombat(AssemblyCSharpSnapshotCombatantBase actor, SnapshotObjectMap map)
            {
                return new ActorReport
                {
                    actorId = actor.ActorId,
                    actorName = actor.ActorName,
                    faction = actor.Faction.ToString(),
                    spawnPoint = Vec(actor.SpawnPoint),
                    baseStats = StatsReport.From(actor.BaseStats),
                    baseBuffIds = ToArray(actor.BaseBuffIds),
                    abilityFlags = actor.AbilityFlags.ToString(),
                    isElite = actor.IsElite,
                    grade = actor.Grade.ToString(),
                    slot = actor.Slot,
                    alignment = actor.Alignment,
                    armor = actor.Armor,
                    armorPierce = actor.ArmorPierce,
                    scoreSeed = actor.ScoreSeed.ToString(CultureInfo.InvariantCulture),
                    spawnToken = actor.SpawnToken.ToString(CultureInfo.InvariantCulture),
                    moveInput = Vec(actor.MoveInput),
                    tuning = Vec(actor.Tuning),
                    aimRotation = Quat(actor.AimRotation),
                    auraColor = ColorValue(actor.AuraColor),
                    teamColor = ColorValue(actor.TeamColor),
                    patrolRect = RectValue(actor.PatrolRect),
                    patrolBounds = BoundsValue(actor.PatrolBounds),
                    gridRect = RectValue(actor.GridRect),
                    gridBounds = BoundsValue(actor.GridBounds),
                    targetMask = actor.TargetMask.value,
                    localMatrix = MatrixValue(actor.LocalMatrix),
                    gridCell = Vec(actor.GridCell),
                    gridVolume = Vec(actor.GridVolume),
                    damageHistory = actor.DamageHistory,
                    patrolPath = VecArray(actor.PatrolPath),
                    tags = ToArray(actor.Tags),
                    namedPoints = NamedPoints(actor.NamedPoints),
                    uniqueBuffIds = ToSortedArray(actor.UniqueBuffIds),
                    pendingCells = VecArray(actor.PendingCells == null ? null : actor.PendingCells.ToArray()),
                    reservedRects = RectArray(actor.ReservedRects == null ? null : actor.ReservedRects.ToArray()),
                    attackRange = RangeReport.From(actor.AttackRange),
                    skillRanges = RangeArray(actor.SkillRanges),
                    weaponPrefab = map.ToReference(actor.WeaponPrefab),
                    aimTarget = map.ToReference(actor.AimTarget),
                    assistTarget = map.ToReference(actor.AssistTarget),
                    primaryEffect = EffectReport.From(actor.PrimaryEffect),
                    effectTimeline = EffectArray(actor.EffectTimeline),
                    genericEffect = EffectReport.From(actor.GenericEffect),
                    concreteEffect = EffectReport.From(actor.ConcreteEffect),
                    concreteEffects = EffectArray(actor.ConcreteEffects),
                    combatStats = StatsReport.From(actor.CombatStats),
                    loadoutStats = StatsArray(actor.LoadoutStats),
                    controlCells = VecArray(actor.ControlCells),
                };
            }
        }

        [Serializable]
        private sealed class BossReport : ActorReport
        {
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

            public static BossReport From(AssemblyCSharpSnapshotBoss boss, SnapshotObjectMap map)
            {
                ActorReport actor = FromCombat(boss, map);
                return new BossReport
                {
                    actorId = actor.actorId,
                    actorName = actor.actorName,
                    faction = actor.faction,
                    spawnPoint = actor.spawnPoint,
                    baseStats = actor.baseStats,
                    baseBuffIds = actor.baseBuffIds,
                    abilityFlags = actor.abilityFlags,
                    isElite = actor.isElite,
                    grade = actor.grade,
                    slot = actor.slot,
                    alignment = actor.alignment,
                    armor = actor.armor,
                    armorPierce = actor.armorPierce,
                    scoreSeed = actor.scoreSeed,
                    spawnToken = actor.spawnToken,
                    moveInput = actor.moveInput,
                    tuning = actor.tuning,
                    aimRotation = actor.aimRotation,
                    auraColor = actor.auraColor,
                    teamColor = actor.teamColor,
                    patrolRect = actor.patrolRect,
                    patrolBounds = actor.patrolBounds,
                    gridRect = actor.gridRect,
                    gridBounds = actor.gridBounds,
                    targetMask = actor.targetMask,
                    localMatrix = actor.localMatrix,
                    gridCell = actor.gridCell,
                    gridVolume = actor.gridVolume,
                    damageHistory = actor.damageHistory,
                    patrolPath = actor.patrolPath,
                    tags = actor.tags,
                    namedPoints = actor.namedPoints,
                    uniqueBuffIds = actor.uniqueBuffIds,
                    pendingCells = actor.pendingCells,
                    reservedRects = actor.reservedRects,
                    attackRange = actor.attackRange,
                    skillRanges = actor.skillRanges,
                    weaponPrefab = actor.weaponPrefab,
                    aimTarget = actor.aimTarget,
                    assistTarget = actor.assistTarget,
                    primaryEffect = actor.primaryEffect,
                    effectTimeline = actor.effectTimeline,
                    genericEffect = actor.genericEffect,
                    concreteEffect = actor.concreteEffect,
                    concreteEffects = actor.concreteEffects,
                    combatStats = actor.combatStats,
                    loadoutStats = actor.loadoutStats,
                    controlCells = actor.controlCells,
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

            public static HubReport From(AssemblyCSharpSnapshotHub hub, SnapshotObjectMap map)
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
        }

        [Serializable]
        private sealed class StatsReport
        {
            public int level;
            public string health;
            public string label;
            public string faction;

            public static StatsReport From(AssemblyCSharpSnapshotStats value)
            {
                if (value == null) return null;
                return new StatsReport
                {
                    level = value.Level,
                    health = F(value.Health),
                    label = value.Label,
                    faction = value.Faction.ToString(),
                };
            }
        }

        [Serializable]
        private sealed class RangeReport
        {
            public int min;
            public int max;

            public static RangeReport From(AssemblyCSharpSnapshotRange value)
            {
                return new RangeReport { min = value.Min, max = value.Max };
            }
        }

        [Serializable]
        private sealed class NamedPointReport
        {
            public string key;
            public string value;
        }

        [Serializable]
        private sealed class EffectReport
        {
            public string type;
            public int id;
            public int damage;
            public string element;
            public string multiplier;
            public RangeReport duration;
            public int value;
            public int extra;
            public int baseValue;
            public int derivedValue;

            public static EffectReport From(AssemblyCSharpSnapshotManagedEffect value)
            {
                if (value == null) return null;
                if (value is AssemblyCSharpSnapshotDamageEffect damage)
                {
                    return new EffectReport
                    {
                        type = "damage",
                        id = damage.Id,
                        damage = damage.Damage,
                        element = damage.Element,
                    };
                }
                if (value is AssemblyCSharpSnapshotBuffEffect buff)
                {
                    return new EffectReport
                    {
                        type = "buff",
                        id = buff.Id,
                        multiplier = F(buff.Multiplier),
                        duration = RangeReport.From(buff.Duration),
                    };
                }
                throw new InvalidOperationException("Unsupported managed effect report type: " + value.GetType().FullName);
            }

            public static EffectReport From(AssemblyCSharpSnapshotManagedEffect<int> value)
            {
                if (value == null) return null;
                if (value is AssemblyCSharpSnapshotIntEffect intEffect)
                {
                    return new EffectReport
                    {
                        type = "int",
                        value = intEffect.Value,
                        extra = intEffect.Extra,
                    };
                }
                throw new InvalidOperationException("Unsupported generic effect report type: " + value.GetType().FullName);
            }

            public static EffectReport From(AssemblyCSharpSnapshotConcreteEffect value)
            {
                if (value == null) return null;
                var report = new EffectReport
                {
                    type = value.GetType() == typeof(AssemblyCSharpSnapshotConcreteEffect) ? "concrete" : "concrete-derived",
                    baseValue = value.BaseValue,
                };
                if (value is AssemblyCSharpSnapshotConcreteDerivedEffect derived)
                {
                    report.derivedValue = derived.DerivedValue;
                }
                return report;
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

        private static int[] ToSortedArray(HashSet<int> value)
        {
            if (value == null) return null;
            int[] result = new int[value.Count];
            value.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        private static string[] RefArray<T>(IList<T> values, SnapshotObjectMap map) where T : UnityEngine.Object
        {
            if (values == null) return null;
            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = map.ToReference(values[i]);
            return result;
        }

        private static string[] RefArray<T>(T[] values, SnapshotObjectMap map) where T : UnityEngine.Object
        {
            if (values == null) return null;
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = map.ToReference(values[i]);
            return result;
        }

        private static NamedPointReport[] NamedPoints(Dictionary<string, Vector3> values)
        {
            if (values == null) return null;
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            var result = new NamedPointReport[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                result[i] = new NamedPointReport { key = keys[i], value = Vec(values[keys[i]]) };
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

        private static string[] VecArray(Vector2Int[] values)
        {
            if (values == null) return null;
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = Vec(values[i]);
            return result;
        }

        private static string[] VecArray(List<Vector2Int> values)
        {
            return values == null ? null : VecArray(values.ToArray());
        }

        private static string[] RectArray(RectInt[] values)
        {
            if (values == null) return null;
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = RectValue(values[i]);
            return result;
        }

        private static string[] ColorArray(Color[] values)
        {
            if (values == null) return null;
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = ColorValue(values[i]);
            return result;
        }

        private static StatsReport[] StatsArray(IList<AssemblyCSharpSnapshotStats> values)
        {
            if (values == null) return null;
            var result = new StatsReport[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = StatsReport.From(values[i]);
            return result;
        }

        private static StatsReport[] StatsArray(AssemblyCSharpSnapshotStats[] values)
        {
            if (values == null) return null;
            var result = new StatsReport[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = StatsReport.From(values[i]);
            return result;
        }

        private static RangeReport[] RangeArray(IList<AssemblyCSharpSnapshotRange> values)
        {
            if (values == null) return null;
            var result = new RangeReport[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = RangeReport.From(values[i]);
            return result;
        }

        private static EffectReport[] EffectArray(IList<AssemblyCSharpSnapshotManagedEffect> values)
        {
            if (values == null) return null;
            var result = new EffectReport[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = EffectReport.From(values[i]);
            return result;
        }

        private static EffectReport[] EffectArray(IList<AssemblyCSharpSnapshotConcreteEffect> values)
        {
            if (values == null) return null;
            var result = new EffectReport[values.Count];
            for (int i = 0; i < values.Count; i++) result[i] = EffectReport.From(values[i]);
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

        private static string RectValue(RectInt value)
        {
            return value.x.ToString(CultureInfo.InvariantCulture)
                + "," + value.y.ToString(CultureInfo.InvariantCulture)
                + "," + value.width.ToString(CultureInfo.InvariantCulture)
                + "," + value.height.ToString(CultureInfo.InvariantCulture);
        }

        private static string BoundsValue(Bounds value)
        {
            return Vec(value.center) + "|" + Vec(value.size);
        }

        private static string BoundsValue(BoundsInt value)
        {
            return Vec(value.position) + "|" + Vec(value.size);
        }

        private static string MatrixValue(Matrix4x4 value)
        {
            var sb = new StringBuilder(128);
            for (int i = 0; i < 16; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(F(value[i]));
            }
            return sb.ToString();
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string D(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
