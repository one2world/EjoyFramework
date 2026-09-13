//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core;
using UnityEngine;

namespace EjoyFramework.Tests.CodeGenSamples.MonoBehaviourSnapshots
{
    public abstract partial class SnapshotActorBase : MonoBehaviour
    {
        [SerializeOrder(0)]
        public int ActorId;

        [SerializeOrder(1)]
        public string ActorName;

        [SerializeField]
        [SerializeOrder(2)]
        protected Vector3 m_SpawnPoint;

        [SerializeField]
        [SerializeOrder(3)]
        protected SnapshotStats m_BaseStats;

        [SerializeField]
        protected List<int> m_BaseBuffIds;

        [SerializeIgnore]
        public int RuntimeCacheVersion;

        [NonSerialized]
        public int NonSerializedCounter;

        public Vector3 SpawnPoint
        {
            get { return m_SpawnPoint; }
        }

        public SnapshotStats BaseStats
        {
            get { return m_BaseStats; }
        }

        public List<int> BaseBuffIds
        {
            get { return m_BaseBuffIds; }
        }

        public void SetBaseSnapshotState(Vector3 spawnPoint, SnapshotStats baseStats, List<int> baseBuffIds)
        {
            m_SpawnPoint = spawnPoint;
            m_BaseStats = baseStats;
            m_BaseBuffIds = baseBuffIds;
        }
    }
}
