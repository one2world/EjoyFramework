//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Unity;
using UnityEngine;

namespace EjoyFramework.Tests.CodeGenSamples.MonoBehaviourSnapshots
{
    [AddComponentMenu("EjoyFramework/Tests/Snapshot Reference Hub")]
    [GenerateMonoBehaviourSnapshot]
    public partial class SnapshotReferenceHubBehaviour : MonoBehaviour
    {
        public SnapshotActorBase PrimaryActor;
        public List<SnapshotActorBase> Actors;
        public SnapshotHeroBehaviour[] Heroes;
        public SnapshotBossBehaviour Boss;
        public GameObject[] Markers;
        public List<GameObject> MarkerList;
        public Transform[] Anchors;
        public Camera CameraRef;

        [SerializeField]
        private SnapshotActorBase m_PrivateActor;

        [SerializeField]
        private List<SnapshotBossBehaviour> m_PrivateBosses;

        public SnapshotActorBase PrivateActor
        {
            get { return m_PrivateActor; }
        }

        public List<SnapshotBossBehaviour> PrivateBosses
        {
            get { return m_PrivateBosses; }
        }

        public void SetPrivateReferenceState(
            SnapshotActorBase privateActor,
            List<SnapshotBossBehaviour> privateBosses)
        {
            m_PrivateActor = privateActor;
            m_PrivateBosses = privateBosses;
        }
    }
}
