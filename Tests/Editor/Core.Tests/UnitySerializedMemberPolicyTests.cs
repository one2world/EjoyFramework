//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Unity.Editor.CodeGen;
using NUnit.Framework;
using UnityEngine;

namespace EjoyFramework.Tests
{
    [TestFixture]
    public sealed class UnitySerializedMemberPolicyTests
    {
        [Test]
        public void GetSerializedFields_FollowsUnityFieldVisibilityRules()
        {
            List<CodeGenMemberDescriptor> members = UnitySerializedMemberPolicy.GetSerializedFields(
                typeof(SampleComponent));

            CollectionAssert.Contains(MemberNames(members), nameof(SampleComponent.PublicValue));
            CollectionAssert.Contains(MemberNames(members), "m_PrivateSerializedValue");
            CollectionAssert.Contains(MemberNames(members), "m_PrivateSerializeReferenceValue");
            CollectionAssert.Contains(MemberNames(members), nameof(SampleComponent.BasePublicValue));

            CollectionAssert.DoesNotContain(MemberNames(members), "m_PrivatePlainValue");
            CollectionAssert.DoesNotContain(MemberNames(members), "m_NonSerializedValue");
            CollectionAssert.DoesNotContain(MemberNames(members), "m_ReadonlyValue");
            CollectionAssert.DoesNotContain(MemberNames(members), "s_StaticValue");
        }

        private static List<string> MemberNames(List<CodeGenMemberDescriptor> members)
        {
            var names = new List<string>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                names.Add(members[i].Name);
            }
            return names;
        }

        private class BaseComponentSample : MonoBehaviour
        {
            public int BasePublicValue;
        }

        private sealed class SampleComponent : BaseComponentSample
        {
            public int PublicValue;

            [SerializeField]
            private int m_PrivateSerializedValue;

            [SerializeReference]
            private object m_PrivateSerializeReferenceValue;

            private int m_PrivatePlainValue;

            [NonSerialized]
            public int m_NonSerializedValue;

            [SerializeField]
            private readonly int m_ReadonlyValue;

            [SerializeField]
            private static int s_StaticValue;
        }
    }
}
