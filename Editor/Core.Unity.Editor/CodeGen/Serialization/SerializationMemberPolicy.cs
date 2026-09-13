//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// Member policy used by binary serializer and deep-copy generators. This keeps persistence-specific
    /// attributes out of the generic codegen type utilities while preserving the existing public-member contract.
    /// </summary>
    public static class SerializationMemberPolicy
    {
        public static List<SerializableMember> GetSerializableMembers(Type type)
        {
            var options = new CodeGenMemberDiscoveryOptions
            {
                IgnoreAttributeType = typeof(SerializeIgnoreAttribute),
                OrderAttributeType = typeof(SerializeOrderAttribute),
            };

            List<CodeGenMemberDescriptor> discovered = CodeGenMemberDiscovery.Discover(type, options);
            var result = new List<SerializableMember>(discovered.Count);
            for (int i = 0; i < discovered.Count; i++)
            {
                result.Add(new SerializableMember
                {
                    Name = discovered[i].Name,
                    MemberType = discovered[i].MemberType,
                    IsField = discovered[i].IsField,
                });
            }
            return result;
        }
    }

    /// <summary>
    /// A serializable member view used by concrete data generators. The generic discovery layer exposes richer
    /// metadata; serializers only need stable access syntax and type information.
    /// </summary>
    public struct SerializableMember
    {
        public string Name;
        public Type MemberType;
        public bool IsField;
    }
}
