//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core;
using UnityEngine;

namespace EjoyFramework.Tests.AssemblyCSharpCodeGenSamples
{
    public abstract partial class AssemblyCSharpSnapshotActorBase : MonoBehaviour
    {
        [SerializeOrder(0)]
        public int ActorId;

        [SerializeOrder(1)]
        public string ActorName;

        [SerializeOrder(2)]
        public AssemblyCSharpSnapshotFaction Faction;

        [SerializeField]
        [SerializeOrder(3)]
        protected Vector3 m_SpawnPoint;

        [SerializeField]
        protected AssemblyCSharpSnapshotStats m_BaseStats;

        [SerializeField]
        protected List<int> m_BaseBuffIds;

        [SerializeIgnore]
        public int RuntimeCacheVersion;

        [NonSerialized]
        public int NonSerializedCounter;

        public Vector3 SpawnPoint => m_SpawnPoint;
        public AssemblyCSharpSnapshotStats BaseStats => m_BaseStats;
        public List<int> BaseBuffIds => m_BaseBuffIds;

        public void SetBaseSnapshotState(
            Vector3 spawnPoint,
            AssemblyCSharpSnapshotStats baseStats,
            List<int> baseBuffIds)
        {
            m_SpawnPoint = spawnPoint;
            m_BaseStats = baseStats;
            m_BaseBuffIds = baseBuffIds;
        }
    }
}
