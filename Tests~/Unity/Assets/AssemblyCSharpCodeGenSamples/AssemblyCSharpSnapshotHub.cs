//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Unity;
using UnityEngine;

namespace EjoyFramework.Tests.AssemblyCSharpCodeGenSamples
{
    [AddComponentMenu("EjoyFramework/Tests/CodeGen Samples/Assembly-CSharp Snapshot Hub")]
    [GenerateMonoBehaviourSnapshot]
    public partial class AssemblyCSharpSnapshotHub : MonoBehaviour
    {
        public AssemblyCSharpSnapshotActorBase PrimaryActor;
        public List<AssemblyCSharpSnapshotActorBase> Actors;
        public AssemblyCSharpSnapshotHero[] Heroes;
        public AssemblyCSharpSnapshotBoss Boss;
        public GameObject[] Markers;
        public List<GameObject> MarkerList;
        public Transform[] Anchors;
        public Camera CameraRef;

        [SerializeField]
        private AssemblyCSharpSnapshotActorBase m_PrivateActor;

        [SerializeField]
        private List<AssemblyCSharpSnapshotBoss> m_PrivateBosses;

        public AssemblyCSharpSnapshotActorBase PrivateActor => m_PrivateActor;
        public List<AssemblyCSharpSnapshotBoss> PrivateBosses => m_PrivateBosses;

        public void SetPrivateReferenceState(
            AssemblyCSharpSnapshotActorBase privateActor,
            List<AssemblyCSharpSnapshotBoss> privateBosses)
        {
            m_PrivateActor = privateActor;
            m_PrivateBosses = privateBosses;
        }
    }
}
