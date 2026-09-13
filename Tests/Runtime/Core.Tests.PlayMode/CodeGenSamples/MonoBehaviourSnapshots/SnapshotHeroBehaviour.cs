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
    [AddComponentMenu("EjoyFramework/Tests/Snapshot Hero")]
    [GenerateMonoBehaviourSnapshot]
    public partial class SnapshotHeroBehaviour : SnapshotCombatantBase
    {
        public string HeroKey;
        public double CooldownMultiplier;
        public List<GameObject> EquipmentSlots;
        public SnapshotStats[] UpgradeStats;

        [SerializeField]
        private SnapshotStats m_PrivateGrowthStats;

        [SerializeField]
        private List<int> m_PrivateTalentIds;

        public SnapshotStats PrivateGrowthStats
        {
            get { return m_PrivateGrowthStats; }
        }

        public List<int> PrivateTalentIds
        {
            get { return m_PrivateTalentIds; }
        }

        public void SetHeroPrivateSnapshotState(SnapshotStats growthStats, List<int> talentIds)
        {
            m_PrivateGrowthStats = growthStats;
            m_PrivateTalentIds = talentIds;
        }
    }
}
