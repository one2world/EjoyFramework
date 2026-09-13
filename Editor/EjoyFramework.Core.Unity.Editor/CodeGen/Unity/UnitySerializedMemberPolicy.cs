//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// Unity-style member policy for future generators that operate on MonoBehaviour / ScriptableObject state.
    /// It discovers fields Unity would serialize: public instance fields and non-public fields marked with
    /// <see cref="SerializeField"/> or <see cref="SerializeReference"/>, excluding static, const, readonly,
    /// and [NonSerialized] fields.
    /// Concrete generators still decide how to encode UnityEngine.Object references.
    /// </summary>
    public static class UnitySerializedMemberPolicy
    {
        public static List<CodeGenMemberDescriptor> GetSerializedFields(Type type)
        {
            var options = new CodeGenMemberDiscoveryOptions
            {
                IncludeFields = true,
                IncludeProperties = false,
                IncludeBaseTypes = true,
                FieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                IgnoreAttributeType = typeof(SerializeIgnoreAttribute),
                OrderAttributeType = typeof(SerializeOrderAttribute),
                FieldPredicate = IsUnitySerializedField,
            };
            return CodeGenMemberDiscovery.Discover(type, options);
        }

        private static bool IsUnitySerializedField(FieldInfo field)
        {
            if (field == null) return false;
            if (field.IsStatic || field.IsLiteral || field.IsInitOnly) return false;
            if (field.IsNotSerialized || Attribute.IsDefined(field, typeof(NonSerializedAttribute), false)) return false;
            return field.IsPublic
                || Attribute.IsDefined(field, typeof(SerializeField), false)
                || Attribute.IsDefined(field, typeof(SerializeReference), false);
        }
    }
}
