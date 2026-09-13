//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EjoyFramework.Tests.AssemblyCSharpCodeGenSamples
{
    public abstract partial class AssemblyCSharpSnapshotCombatantBase : AssemblyCSharpSnapshotActorBase
    {
        public AssemblyCSharpSnapshotAbilityFlags AbilityFlags;
        public bool IsElite;
        public char Grade;
        public byte Slot;
        public sbyte Alignment;
        public short Armor;
        public ushort ArmorPierce;
        public uint ScoreSeed;
        public ulong SpawnToken;
        public Vector2 MoveInput;
        public Vector4 Tuning;
        public Quaternion AimRotation;
        public Color AuraColor;
        public Color32 TeamColor;
        public Rect PatrolRect;
        public Bounds PatrolBounds;
        public RectInt GridRect;
        public BoundsInt GridBounds;
        public LayerMask TargetMask;
        public Matrix4x4 LocalMatrix;
        public Vector2Int GridCell;
        public Vector3Int GridVolume;
        public int[] DamageHistory;
        public Vector3[] PatrolPath;
        public List<string> Tags;
        public Dictionary<string, Vector3> NamedPoints;
        public HashSet<int> UniqueBuffIds;
        public Queue<Vector2Int> PendingCells;
        public Stack<RectInt> ReservedRects;
        public AssemblyCSharpSnapshotRange AttackRange;
        public List<AssemblyCSharpSnapshotRange> SkillRanges;
        public GameObject WeaponPrefab;
        public Transform AimTarget;
        public AssemblyCSharpSnapshotActorBase AssistTarget;

        [SerializeReference]
        public AssemblyCSharpSnapshotManagedEffect PrimaryEffect;

        [SerializeReference]
        public List<AssemblyCSharpSnapshotManagedEffect> EffectTimeline;

        [SerializeReference]
        public AssemblyCSharpSnapshotManagedEffect<int> GenericEffect;

        [SerializeReference]
        public AssemblyCSharpSnapshotConcreteEffect ConcreteEffect;

        [SerializeReference]
        public List<AssemblyCSharpSnapshotConcreteEffect> ConcreteEffects;

        [SerializeField]
        protected AssemblyCSharpSnapshotStats m_CombatStats;

        [SerializeField]
        protected List<AssemblyCSharpSnapshotStats> m_LoadoutStats;

        [SerializeField]
        protected List<Vector2Int> m_ControlCells;

        public AssemblyCSharpSnapshotStats CombatStats => m_CombatStats;
        public List<AssemblyCSharpSnapshotStats> LoadoutStats => m_LoadoutStats;
        public List<Vector2Int> ControlCells => m_ControlCells;

        public void SetCombatSnapshotState(
            AssemblyCSharpSnapshotStats combatStats,
            List<AssemblyCSharpSnapshotStats> loadoutStats,
            List<Vector2Int> controlCells)
        {
            m_CombatStats = combatStats;
            m_LoadoutStats = loadoutStats;
            m_ControlCells = controlCells;
        }
    }
}
