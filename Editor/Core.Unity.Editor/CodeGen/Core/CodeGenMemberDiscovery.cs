//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// Shared member-discovery infrastructure for code generators. It deliberately knows nothing about
    /// binary serialization, MVVM, snapshots, or networking; each concrete generator supplies a policy.
    /// </summary>
    public static class CodeGenMemberDiscovery
    {
        public static List<CodeGenMemberDescriptor> Discover(Type type, CodeGenMemberDiscoveryOptions options)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var ordered = new List<MemberEntry>();
            var unordered = new List<MemberEntry>();

            if (options.IncludeFields)
            {
                CollectFields(type, options, ordered, unordered);
            }

            if (options.IncludeProperties)
            {
                CollectProperties(type, options, ordered, unordered);
            }

            ordered.Sort(CompareOrdered);
            unordered.Sort(CompareUnordered);

            var result = new List<CodeGenMemberDescriptor>(ordered.Count + unordered.Count);
            for (int i = 0; i < ordered.Count; i++) result.Add(ordered[i].Member);
            for (int i = 0; i < unordered.Count; i++) result.Add(unordered[i].Member);
            return result;
        }

        private static void CollectFields(
            Type type,
            CodeGenMemberDiscoveryOptions options,
            List<MemberEntry> ordered,
            List<MemberEntry> unordered)
        {
            if (options.IncludeBaseTypes)
            {
                int depth = 0;
                for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                {
                    BindingFlags flags = (options.FieldFlags | BindingFlags.DeclaredOnly) & ~BindingFlags.FlattenHierarchy;
                    AddFields(current.GetFields(flags), options, depth, ordered, unordered);
                    depth++;
                }
                return;
            }

            AddFields(type.GetFields(options.FieldFlags), options, 0, ordered, unordered);
        }

        private static void AddFields(
            FieldInfo[] fields,
            CodeGenMemberDiscoveryOptions options,
            int depth,
            List<MemberEntry> ordered,
            List<MemberEntry> unordered)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic) continue;
                if (!options.IncludeReadonlyFields && field.IsInitOnly) continue;
                if (!options.IncludeConstFields && field.IsLiteral) continue;
                if (HasAttribute(field, options.IgnoreAttributeType)) continue;
                if (options.FieldPredicate != null && !options.FieldPredicate(field)) continue;

                AddMember(ordered, unordered, new CodeGenMemberDescriptor
                {
                    Name = field.Name,
                    MemberType = field.FieldType,
                    IsField = true,
                    CanRead = true,
                    CanWrite = !field.IsInitOnly && !field.IsLiteral,
                    MemberInfo = field,
                    DeclaringDepth = depth,
                    MetadataToken = SafeMetadataToken(field),
                }, field, options.OrderAttributeType);
            }
        }

        private static void CollectProperties(
            Type type,
            CodeGenMemberDiscoveryOptions options,
            List<MemberEntry> ordered,
            List<MemberEntry> unordered)
        {
            if (options.IncludeBaseTypes)
            {
                int depth = 0;
                for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                {
                    BindingFlags flags = (options.PropertyFlags | BindingFlags.DeclaredOnly) & ~BindingFlags.FlattenHierarchy;
                    AddProperties(current.GetProperties(flags), options, depth, ordered, unordered);
                    depth++;
                }
                return;
            }

            AddProperties(type.GetProperties(options.PropertyFlags), options, 0, ordered, unordered);
        }

        private static void AddProperties(
            PropertyInfo[] properties,
            CodeGenMemberDiscoveryOptions options,
            int depth,
            List<MemberEntry> ordered,
            List<MemberEntry> unordered)
        {
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                bool canRead = property.CanRead;
                bool canWrite = property.CanWrite;
                if (!canRead) continue;
                if (options.RequireWritableProperties && !canWrite) continue;
                if (!options.IncludeIndexerProperties && property.GetIndexParameters().Length != 0) continue;
                if (HasAttribute(property, options.IgnoreAttributeType)) continue;
                if (options.PropertyPredicate != null && !options.PropertyPredicate(property)) continue;

                AddMember(ordered, unordered, new CodeGenMemberDescriptor
                {
                    Name = property.Name,
                    MemberType = property.PropertyType,
                    IsField = false,
                    CanRead = canRead,
                    CanWrite = canWrite,
                    MemberInfo = property,
                    DeclaringDepth = depth,
                    MetadataToken = SafeMetadataToken(property),
                }, property, options.OrderAttributeType);
            }
        }

        private static void AddMember(
            List<MemberEntry> ordered,
            List<MemberEntry> unordered,
            CodeGenMemberDescriptor member,
            MemberInfo memberInfo,
            Type orderAttributeType)
        {
            var entry = new MemberEntry { Member = member };
            if (TryReadOrder(memberInfo, orderAttributeType, out int order))
            {
                entry.HasOrder = true;
                entry.Order = order;
                ordered.Add(entry);
            }
            else
            {
                unordered.Add(entry);
            }
        }

        private static bool HasAttribute(MemberInfo member, Type attributeType)
        {
            return attributeType != null && Attribute.IsDefined(member, attributeType, false);
        }

        private static bool TryReadOrder(MemberInfo member, Type attributeType, out int order)
        {
            order = 0;
            if (attributeType == null) return false;

            Attribute attr = Attribute.GetCustomAttribute(member, attributeType, false);
            if (attr == null) return false;

            PropertyInfo property = attributeType.GetProperty("Order", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(int)) return false;

            order = (int)property.GetValue(attr, null);
            return true;
        }

        private static int CompareOrdered(MemberEntry x, MemberEntry y)
        {
            int c = x.Order.CompareTo(y.Order);
            if (c != 0) return c;
            return CompareUnordered(x, y);
        }

        private static int CompareUnordered(MemberEntry x, MemberEntry y)
        {
            int c = x.Member.DeclaringDepth.CompareTo(y.Member.DeclaringDepth);
            if (c != 0) return c;
            c = x.Member.MetadataToken.CompareTo(y.Member.MetadataToken);
            if (c != 0) return c;
            return string.CompareOrdinal(x.Member.Name, y.Member.Name);
        }

        private static int SafeMetadataToken(MemberInfo member)
        {
            try { return member.MetadataToken; }
            catch (InvalidOperationException) { return 0; }
        }

        private struct MemberEntry
        {
            public CodeGenMemberDescriptor Member;
            public bool HasOrder;
            public int Order;
        }
    }

    public sealed class CodeGenMemberDiscoveryOptions
    {
        public bool IncludeFields = true;
        public bool IncludeProperties = true;
        public bool IncludeBaseTypes;
        public bool IncludeReadonlyFields;
        public bool IncludeConstFields;
        public bool RequireWritableProperties = true;
        public bool IncludeIndexerProperties;
        public BindingFlags FieldFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        public BindingFlags PropertyFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        public Type IgnoreAttributeType;
        public Type OrderAttributeType;
        public Predicate<FieldInfo> FieldPredicate;
        public Predicate<PropertyInfo> PropertyPredicate;
    }

    public struct CodeGenMemberDescriptor
    {
        public string Name;
        public Type MemberType;
        public bool IsField;
        public bool CanRead;
        public bool CanWrite;
        public MemberInfo MemberInfo;
        public int DeclaringDepth;
        public int MetadataToken;
    }
}
