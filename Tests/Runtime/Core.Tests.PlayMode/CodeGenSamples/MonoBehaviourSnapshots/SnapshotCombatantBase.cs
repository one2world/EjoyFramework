//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EjoyFramework.Tests.CodeGenSamples.MonoBehaviourSnapshots
{
    public abstract partial class SnapshotCombatantBase : SnapshotActorBase
    {
        public SnapshotFaction Faction;
        public SnapshotAbilityFlags AbilityFlags;
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
        public Vector2Int GridCell;
        public Vector3Int GridVolume;
        public int[] DamageHistory;
        public Vector3[] PatrolPath;
        public List<string> Tags;
        public SnapshotRange AttackRange;
        public List<SnapshotRange> SkillRanges;
        public GameObject WeaponPrefab;
        public Transform AimTarget;
        public SnapshotActorBase AssistTarget;

        [SerializeField]
        protected SnapshotStats m_CombatStats;

        [SerializeField]
        protected List<SnapshotStats> m_LoadoutStats;

        [SerializeField]
        protected List<Vector2Int> m_ControlCells;

        public SnapshotStats CombatStats
        {
            get { return m_CombatStats; }
        }

        public List<SnapshotStats> LoadoutStats
        {
            get { return m_LoadoutStats; }
        }

        public List<Vector2Int> ControlCells
        {
            get { return m_ControlCells; }
        }

        public void SetCombatSnapshotState(
            SnapshotStats combatStats,
            List<SnapshotStats> loadoutStats,
            List<Vector2Int> controlCells)
        {
            m_CombatStats = combatStats;
            m_LoadoutStats = loadoutStats;
            m_ControlCells = controlCells;
        }
    }
}
