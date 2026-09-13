//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Unity;
using UnityEngine;

namespace EjoyFramework.Tests.AssemblyCSharpCodeGenSamples
{
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Tests/CodeGen Samples/Assembly-CSharp Snapshot Boss")]
    [GenerateMonoBehaviourSnapshot]
    public partial class AssemblyCSharpSnapshotBoss : AssemblyCSharpSnapshotCombatantBase
    {
        public int Phase;
        public double EnrageSeconds;
        public AssemblyCSharpSnapshotHero[] Summons;
        public List<AssemblyCSharpSnapshotHero> ReserveSummons;
        public AssemblyCSharpSnapshotActorBase[] ThreatTable;
        public List<AssemblyCSharpSnapshotActorBase> ThreatList;
        public Color[] PhaseColors;
        public List<Vector2Int> ArenaCells;
        public AssemblyCSharpSnapshotStats[] PhaseStats;

        [SerializeField]
        private AssemblyCSharpSnapshotStats m_BossStats;

        [SerializeField]
        private List<AssemblyCSharpSnapshotRange> m_EnrageRanges;

        [SerializeField]
        private GameObject m_PrivateArenaMarker;

        public AssemblyCSharpSnapshotStats BossStats => m_BossStats;
        public List<AssemblyCSharpSnapshotRange> EnrageRanges => m_EnrageRanges;
        public GameObject PrivateArenaMarker => m_PrivateArenaMarker;

        public void SetBossPrivateSnapshotState(
            AssemblyCSharpSnapshotStats bossStats,
            List<AssemblyCSharpSnapshotRange> enrageRanges,
            GameObject privateArenaMarker)
        {
            m_BossStats = bossStats;
            m_EnrageRanges = enrageRanges;
            m_PrivateArenaMarker = privateArenaMarker;
        }
    }
}
