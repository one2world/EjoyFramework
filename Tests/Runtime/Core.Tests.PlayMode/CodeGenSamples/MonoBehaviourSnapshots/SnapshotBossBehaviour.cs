//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Unity;
using UnityEngine;

namespace EjoyFramework.Tests.CodeGenSamples.MonoBehaviourSnapshots
{
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Tests/Snapshot Boss")]
    [GenerateMonoBehaviourSnapshot]
    public partial class SnapshotBossBehaviour : SnapshotCombatantBase
    {
        public int Phase;
        public double EnrageSeconds;
        public SnapshotHeroBehaviour[] Summons;
        public List<SnapshotHeroBehaviour> ReserveSummons;
        public SnapshotActorBase[] ThreatTable;
        public List<SnapshotActorBase> ThreatList;
        public Color[] PhaseColors;
        public List<Vector2Int> ArenaCells;
        public SnapshotStats[] PhaseStats;

        [SerializeField]
        private SnapshotStats m_BossStats;

        [SerializeField]
        private List<SnapshotRange> m_EnrageRanges;

        [SerializeField]
        private GameObject m_PrivateArenaMarker;

        public SnapshotStats BossStats
        {
            get { return m_BossStats; }
        }

        public List<SnapshotRange> EnrageRanges
        {
            get { return m_EnrageRanges; }
        }

        public GameObject PrivateArenaMarker
        {
            get { return m_PrivateArenaMarker; }
        }

        public void SetBossPrivateSnapshotState(
            SnapshotStats bossStats,
            List<SnapshotRange> enrageRanges,
            GameObject privateArenaMarker)
        {
            m_BossStats = bossStats;
            m_EnrageRanges = enrageRanges;
            m_PrivateArenaMarker = privateArenaMarker;
        }
    }
}
