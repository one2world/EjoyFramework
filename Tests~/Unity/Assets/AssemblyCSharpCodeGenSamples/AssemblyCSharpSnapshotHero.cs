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
    [AddComponentMenu("EjoyFramework/Tests/CodeGen Samples/Assembly-CSharp Snapshot Hero")]
    [GenerateMonoBehaviourSnapshot]
    public partial class AssemblyCSharpSnapshotHero : AssemblyCSharpSnapshotCombatantBase
    {
        public string HeroKey;
        public double CooldownMultiplier;
        public List<GameObject> EquipmentSlots;
        public AssemblyCSharpSnapshotStats[] UpgradeStats;

        [SerializeField]
        private AssemblyCSharpSnapshotStats m_PrivateGrowthStats;

        [SerializeField]
        private List<int> m_PrivateTalentIds;

        public AssemblyCSharpSnapshotStats PrivateGrowthStats => m_PrivateGrowthStats;
        public List<int> PrivateTalentIds => m_PrivateTalentIds;

        public void SetHeroPrivateSnapshotState(
            AssemblyCSharpSnapshotStats growthStats,
            List<int> talentIds)
        {
            m_PrivateGrowthStats = growthStats;
            m_PrivateTalentIds = talentIds;
        }
    }
}
