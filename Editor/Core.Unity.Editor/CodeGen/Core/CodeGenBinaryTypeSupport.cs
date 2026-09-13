//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EjoyFramework.Core.Serialization;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// Shared ByteBuffer-oriented type support for code generators. Concrete generators still decide
    /// context-specific behavior such as UnityEngine.Object reference resolution and composite type rules.
    /// </summary>
    public static class CodeGenBinaryTypeSupport
    {
        public static bool TryGetCollectionShape(Type type, out CodeGenCollectionShape shape)
        {
            shape = default;
            if (type == null) return false;

            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1) return false;
                shape = CodeGenCollectionShape.Array(type.GetElementType());
                return true;
            }

            if (!type.IsGenericType || type.ContainsGenericParameters) return false;

            Type genericDefinition = type.GetGenericTypeDefinition();
            Type[] arguments = type.GetGenericArguments();
            if (genericDefinition == typeof(List<>))
            {
                shape = CodeGenCollectionShape.List(arguments[0]);
                return true;
            }
            if (genericDefinition == typeof(HashSet<>))
            {
                shape = CodeGenCollectionShape.HashSet(arguments[0]);
                return true;
            }
            if (genericDefinition == typeof(Queue<>))
            {
                shape = CodeGenCollectionShape.Queue(arguments[0]);
                return true;
            }
            if (genericDefinition == typeof(Stack<>))
            {
                shape = CodeGenCollectionShape.Stack(arguments[0]);
                return true;
            }
            if (genericDefinition == typeof(Dictionary<,>))
            {
                shape = CodeGenCollectionShape.Dictionary(arguments[0], arguments[1]);
                return true;
            }

            return false;
        }

        public static string ScalarWriteCall(Type type, string expr)
        {
            string method = ScalarWriteMethod(type);
            if (method == null) return null;
            if (type == typeof(char)) return "buffer.WriteUShort((ushort)" + expr + ");";
            return "buffer." + method + "(" + expr + ");";
        }

        public static string ScalarWriteMethod(Type type)
        {
            if (type == typeof(bool)) return "WriteBool";
            if (type == typeof(byte)) return "WriteByte";
            if (type == typeof(sbyte)) return "WriteSByte";
            if (type == typeof(short)) return "WriteShort";
            if (type == typeof(ushort)) return "WriteUShort";
            if (type == typeof(char)) return "WriteUShort";
            if (type == typeof(int)) return "WriteInt";
            if (type == typeof(uint)) return "WriteUInt";
            if (type == typeof(long)) return "WriteLong";
            if (type == typeof(ulong)) return "WriteULong";
            if (type == typeof(float)) return "WriteFloat";
            if (type == typeof(double)) return "WriteDouble";
            if (type == typeof(string)) return "WriteString";
            return null;
        }

        public static string ScalarReadExpr(Type type)
        {
            if (type == typeof(char)) return "(char)buffer.ReadUShort()";
            string method = ScalarReadMethod(type);
            return method == null ? null : "buffer." + method + "()";
        }

        public static string ScalarReadMethod(Type type)
        {
            if (type == typeof(bool)) return "ReadBool";
            if (type == typeof(byte)) return "ReadByte";
            if (type == typeof(sbyte)) return "ReadSByte";
            if (type == typeof(short)) return "ReadShort";
            if (type == typeof(ushort)) return "ReadUShort";
            if (type == typeof(char)) return "ReadUShort";
            if (type == typeof(int)) return "ReadInt";
            if (type == typeof(uint)) return "ReadUInt";
            if (type == typeof(long)) return "ReadLong";
            if (type == typeof(ulong)) return "ReadULong";
            if (type == typeof(float)) return "ReadFloat";
            if (type == typeof(double)) return "ReadDouble";
            if (type == typeof(string)) return "ReadString";
            return null;
        }

        public static bool IsUnityValueType(Type type)
        {
            return type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Vector2Int)
                || type == typeof(Vector3Int)
                || type == typeof(Quaternion)
                || type == typeof(Color)
                || type == typeof(Color32)
                || type == typeof(Rect)
                || type == typeof(RectInt)
                || type == typeof(Bounds)
                || type == typeof(BoundsInt)
                || type == typeof(LayerMask)
                || type == typeof(Matrix4x4);
        }

        public static bool TryWriteUnityValue(CodeBuilder cb, Type type, string expr)
        {
            if (type == typeof(Vector2))
            {
                WriteFloats(cb, expr, "x", "y");
                return true;
            }
            if (type == typeof(Vector3))
            {
                WriteFloats(cb, expr, "x", "y", "z");
                return true;
            }
            if (type == typeof(Vector4))
            {
                WriteFloats(cb, expr, "x", "y", "z", "w");
                return true;
            }
            if (type == typeof(Vector2Int))
            {
                WriteInts(cb, expr, "x", "y");
                return true;
            }
            if (type == typeof(Vector3Int))
            {
                WriteInts(cb, expr, "x", "y", "z");
                return true;
            }
            if (type == typeof(Quaternion))
            {
                WriteFloats(cb, expr, "x", "y", "z", "w");
                return true;
            }
            if (type == typeof(Color))
            {
                WriteFloats(cb, expr, "r", "g", "b", "a");
                return true;
            }
            if (type == typeof(Color32))
            {
                cb.Line("buffer.WriteByte(" + expr + ".r);");
                cb.Line("buffer.WriteByte(" + expr + ".g);");
                cb.Line("buffer.WriteByte(" + expr + ".b);");
                cb.Line("buffer.WriteByte(" + expr + ".a);");
                return true;
            }
            if (type == typeof(Rect))
            {
                WriteFloats(cb, expr, "x", "y", "width", "height");
                return true;
            }
            if (type == typeof(RectInt))
            {
                WriteInts(cb, expr, "x", "y", "width", "height");
                return true;
            }
            if (type == typeof(Bounds))
            {
                WriteFloats(cb, expr + ".center", "x", "y", "z");
                WriteFloats(cb, expr + ".size", "x", "y", "z");
                return true;
            }
            if (type == typeof(BoundsInt))
            {
                WriteInts(cb, expr + ".position", "x", "y", "z");
                WriteInts(cb, expr + ".size", "x", "y", "z");
                return true;
            }
            if (type == typeof(LayerMask))
            {
                cb.Line("buffer.WriteInt(" + expr + ".value);");
                return true;
            }
            if (type == typeof(Matrix4x4))
            {
                cb.Line("for (int _m = 0; _m < 16; _m++) buffer.WriteFloat(" + expr + "[_m]);");
                return true;
            }

            return false;
        }

        public static bool TryReadUnityValue(CodeBuilder cb, Type type, string target, string localSuffix)
        {
            if (type == typeof(Vector2))
            {
                cb.Line(target + " = new UnityEngine.Vector2(buffer.ReadFloat(), buffer.ReadFloat());");
                return true;
            }
            if (type == typeof(Vector3))
            {
                cb.Line(target + " = new UnityEngine.Vector3(buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat());");
                return true;
            }
            if (type == typeof(Vector4))
            {
                cb.Line(target + " = new UnityEngine.Vector4(buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat());");
                return true;
            }
            if (type == typeof(Vector2Int))
            {
                cb.Line(target + " = new UnityEngine.Vector2Int(buffer.ReadInt(), buffer.ReadInt());");
                return true;
            }
            if (type == typeof(Vector3Int))
            {
                cb.Line(target + " = new UnityEngine.Vector3Int(buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt());");
                return true;
            }
            if (type == typeof(Quaternion))
            {
                cb.Line(target + " = new UnityEngine.Quaternion(buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat());");
                return true;
            }
            if (type == typeof(Color))
            {
                cb.Line(target + " = new UnityEngine.Color(buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat());");
                return true;
            }
            if (type == typeof(Color32))
            {
                cb.Line(target + " = new UnityEngine.Color32(buffer.ReadByte(), buffer.ReadByte(), buffer.ReadByte(), buffer.ReadByte());");
                return true;
            }
            if (type == typeof(Rect))
            {
                cb.Line(target + " = new UnityEngine.Rect(buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat());");
                return true;
            }
            if (type == typeof(RectInt))
            {
                cb.Line(target + " = new UnityEngine.RectInt(buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt());");
                return true;
            }
            if (type == typeof(Bounds))
            {
                cb.Line(target + " = new UnityEngine.Bounds(new UnityEngine.Vector3(buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat()), new UnityEngine.Vector3(buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadFloat()));");
                return true;
            }
            if (type == typeof(BoundsInt))
            {
                cb.Line(target + " = new UnityEngine.BoundsInt(new UnityEngine.Vector3Int(buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt()), new UnityEngine.Vector3Int(buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt()));");
                return true;
            }
            if (type == typeof(LayerMask))
            {
                string mask = "_mask" + localSuffix;
                cb.Line("var " + mask + " = new UnityEngine.LayerMask();");
                cb.Line(mask + ".value = buffer.ReadInt();");
                cb.Line(target + " = " + mask + ";");
                return true;
            }
            if (type == typeof(Matrix4x4))
            {
                string matrix = "_matrix" + localSuffix;
                string index = "_m" + localSuffix;
                cb.Line("var " + matrix + " = new UnityEngine.Matrix4x4();");
                cb.Line("for (int " + index + " = 0; " + index + " < 16; " + index + "++) "
                    + matrix + "[" + index + "] = buffer.ReadFloat();");
                cb.Line(target + " = " + matrix + ";");
                return true;
            }

            return false;
        }

        public static bool TryGetEqualityComparerElementType(CodeGenCollectionShape shape, out Type elementType)
        {
            if (shape.Kind == CodeGenCollectionKind.Dictionary)
            {
                elementType = shape.KeyType;
                return true;
            }
            if (shape.Kind == CodeGenCollectionKind.HashSet)
            {
                elementType = shape.ElementType;
                return true;
            }

            elementType = null;
            return false;
        }

        public static void EmitWriteEqualityComparerGuard(
            CodeBuilder cb,
            CodeGenCollectionShape shape,
            string expr,
            int localIndex,
            string memberName)
        {
            if (!TryGetEqualityComparerElementType(shape, out Type elementType)) return;

            string typeName = CodeGenTypeUtil.TypeName(elementType);
            string comparer = "_comparer" + localIndex;
            cb.Line("var " + comparer + " = " + expr + ".Comparer;");

            if (elementType == typeof(string))
            {
                string comparerKind = "_comparerKind" + localIndex;
                cb.Line("byte " + comparerKind + ";");
                cb.BeginBlock("if (object.ReferenceEquals(" + comparer
                    + ", System.Collections.Generic.EqualityComparer<System.String>.Default))");
                cb.Line(comparerKind + " = 0;");
                cb.EndBlock();
                cb.BeginBlock("else if (object.ReferenceEquals(" + comparer + ", System.StringComparer.Ordinal))");
                cb.Line(comparerKind + " = 1;");
                cb.EndBlock();
                cb.BeginBlock("else if (object.ReferenceEquals(" + comparer + ", System.StringComparer.OrdinalIgnoreCase))");
                cb.Line(comparerKind + " = 2;");
                cb.EndBlock();
                cb.BeginBlock("else");
                cb.Line("throw new EjoyFramework.Core.FrameworkException(\"Unsupported string comparer for generated member '"
                    + EscapeString(memberName) + "'.\");");
                cb.EndBlock();
                cb.Line("buffer.WriteByte(" + comparerKind + ");");
                return;
            }

            cb.BeginBlock("if (!object.ReferenceEquals(" + comparer
                + ", System.Collections.Generic.EqualityComparer<" + typeName + ">.Default))");
            cb.Line("throw new EjoyFramework.Core.FrameworkException(\"Unsupported comparer for generated member '"
                + EscapeString(memberName) + "'.\");");
            cb.EndBlock();
        }

        public static string EmitReadEqualityComparer(
            CodeBuilder cb,
            CodeGenCollectionShape shape,
            int localIndex,
            string memberName)
        {
            if (!TryGetEqualityComparerElementType(shape, out Type elementType)) return null;

            string comparer = "_comparer" + localIndex;
            if (elementType == typeof(string))
            {
                string comparerKind = "_comparerKind" + localIndex;
                cb.Line("byte " + comparerKind + " = buffer.ReadByte();");
                cb.Line("System.Collections.Generic.IEqualityComparer<System.String> " + comparer + ";");
                cb.BeginBlock("switch (" + comparerKind + ")");
                cb.Line("case 0: " + comparer + " = System.Collections.Generic.EqualityComparer<System.String>.Default; break;");
                cb.Line("case 1: " + comparer + " = System.StringComparer.Ordinal; break;");
                cb.Line("case 2: " + comparer + " = System.StringComparer.OrdinalIgnoreCase; break;");
                cb.Line("default: throw new EjoyFramework.Core.FrameworkException(\"Unsupported string comparer kind for generated member '"
                    + EscapeString(memberName) + "'.\");");
                cb.EndBlock();
                return comparer;
            }

            cb.Line("var " + comparer + " = System.Collections.Generic.EqualityComparer<"
                + CodeGenTypeUtil.TypeName(elementType) + ">.Default;");
            return comparer;
        }

        private static void WriteFloats(CodeBuilder cb, string expr, params string[] members)
        {
            for (int i = 0; i < members.Length; i++)
            {
                cb.Line("buffer.WriteFloat(" + expr + "." + members[i] + ");");
            }
        }

        private static void WriteInts(CodeBuilder cb, string expr, params string[] members)
        {
            for (int i = 0; i < members.Length; i++)
            {
                cb.Line("buffer.WriteInt(" + expr + "." + members[i] + ");");
            }
        }

        private static string EscapeString(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    internal static class CodeGenPolymorphicTypeSupport
    {
        public static bool IsSerializableComposite(Type type)
        {
            return Attribute.IsDefined(type, typeof(GenerateSerializerAttribute), false)
                || typeof(IBinarySerializable).IsAssignableFrom(type);
        }

        public static bool IsPolymorphicContract(Type type)
        {
            return type != null && !type.IsValueType && (type.IsInterface || type.IsAbstract);
        }

        public static bool CanDeserializeConcreteComposite(
            Type type,
            Assembly generatedAssembly,
            out string reason)
        {
            reason = null;
            if (type == null)
            {
                reason = "null type";
                return false;
            }

            bool generatedOnly = Attribute.IsDefined(type, typeof(GenerateSerializerAttribute), false)
                && !typeof(IBinarySerializable).IsAssignableFrom(type);
            if (generatedOnly && type.IsNested)
            {
                reason = "nested [GenerateSerializer] types are skipped by SerializerGenerator";
                return false;
            }
            if (generatedOnly && type.ContainsGenericParameters)
            {
                reason = "open generic [GenerateSerializer] types are skipped by SerializerGenerator";
                return false;
            }
            if (type.IsValueType) return true;
            if (type.IsInterface || type.IsAbstract)
            {
                reason = "managed polymorphic composite types require a generated concrete type discriminator";
                return false;
            }
            if (type.ContainsGenericParameters)
            {
                reason = "open generic concrete types cannot be instantiated by generated code";
                return false;
            }
            if (!CodeGenTypeUtil.CanReferenceTypeFrom(type, generatedAssembly))
            {
                reason = "type is not accessible from generated code";
                return false;
            }
            if (!CodeGenTypeUtil.HasCallableParameterlessConstructor(type, generatedAssembly))
            {
                reason = "missing accessible parameterless constructor";
                return false;
            }

            return true;
        }

        public static bool TryBuildCandidates(
            Type declaredType,
            Assembly generatedAssembly,
            out List<Type> candidates,
            out string reason,
            bool allowConcreteBase = false)
        {
            candidates = null;
            reason = null;

            if (declaredType == null)
            {
                reason = "null polymorphic contract type";
                return false;
            }
            if (declaredType == typeof(object) || declaredType == typeof(IBinarySerializable))
            {
                reason = "polymorphic contract is too broad; declare a narrower abstract class or interface";
                return false;
            }
            if (!IsPolymorphicContract(declaredType) && !IsConcretePolymorphicBase(declaredType, allowConcreteBase))
            {
                reason = "type is not an abstract/interface contract or allowed concrete base";
                return false;
            }
            if (declaredType.ContainsGenericParameters)
            {
                reason = "open generic polymorphic contracts are not supported";
                return false;
            }
            if (!CodeGenTypeUtil.CanReferenceTypeFrom(declaredType, generatedAssembly))
            {
                reason = "declared polymorphic contract is not accessible from generated code";
                return false;
            }

            var result = new List<Type>();
            foreach (Type candidate in CodeGenTypeUtil.EnumerateAllTypes())
            {
                if (candidate == null) continue;
                if (candidate.IsInterface || candidate.IsAbstract) continue;
                if (candidate.ContainsGenericParameters) continue;
                if (!declaredType.IsAssignableFrom(candidate)) continue;
                if (!IsSerializableComposite(candidate)) continue;
                if (!CanDeserializeConcreteComposite(candidate, generatedAssembly, out _)) continue;

                result.Add(candidate);
            }

            if (result.Count == 0)
            {
                reason = "no concrete serializable candidates were found";
                return false;
            }

            var usedTypeIds = new Dictionary<int, Type>();
            for (int i = 0; i < result.Count; i++)
            {
                int typeId = StableTypeId(result[i]);
                if (usedTypeIds.TryGetValue(typeId, out Type existing))
                {
                    reason = "polymorphic type id collision between '"
                        + CodeGenTypeUtil.TypeName(existing)
                        + "' and '"
                        + CodeGenTypeUtil.TypeName(result[i])
                        + "'";
                    return false;
                }
                usedTypeIds.Add(typeId, result[i]);
            }

            result.Sort((left, right) =>
            {
                int idCompare = StableTypeId(left).CompareTo(StableTypeId(right));
                if (idCompare != 0) return idCompare;
                return string.Compare(left.FullName ?? left.Name, right.FullName ?? right.Name, StringComparison.Ordinal);
            });
            candidates = result;
            return true;
        }

        public static void EmitWrite(
            CodeBuilder cb,
            IReadOnlyList<Type> candidates,
            string expr,
            string memberName,
            int localIndex)
        {
            string value = "_polyValue" + localIndex;
            string valueType = "_polyType" + localIndex;
            cb.Line("var " + value + " = " + expr + ";");
            cb.BeginBlock("if (" + value + " == null)");
            cb.Line("buffer.WriteInt(0);");
            cb.EndBlock();
            cb.BeginBlock("else");
            cb.Line("System.Type " + valueType + " = " + value + ".GetType();");

            for (int i = 0; i < candidates.Count; i++)
            {
                string typed = "_polyTyped" + localIndex + "_" + i;
                string branch = i == 0 ? "if" : "else if";
                cb.BeginBlock(branch + " (" + valueType + " == typeof(" + CodeGenTypeUtil.TypeName(candidates[i]) + "))");
                cb.Line(CodeGenTypeUtil.TypeName(candidates[i]) + " " + typed
                    + " = (" + CodeGenTypeUtil.TypeName(candidates[i]) + ")" + value + ";");
                cb.Line("buffer.WriteInt(" + StableTypeId(candidates[i]) + ");");
                cb.Line(typed + ".Serialize(buffer);");
                cb.EndBlock();
            }

            cb.BeginBlock("else");
            cb.Line("throw new EjoyFramework.Core.FrameworkException(\"Unsupported polymorphic type for generated member '"
                + EscapeString(memberName) + "': \" + " + value + ".GetType().FullName);");
            cb.EndBlock();
            cb.EndBlock();
        }

        public static void EmitRead(
            CodeBuilder cb,
            IReadOnlyList<Type> candidates,
            string target,
            string memberName,
            int localIndex)
        {
            string typeId = "_polyTypeId" + localIndex;
            cb.Line("int " + typeId + " = buffer.ReadInt();");
            cb.Line("switch (" + typeId + ")");
            cb.Line("{");
            cb.Indent();
            cb.Line("case 0:");
            cb.Indent();
            cb.Line(target + " = null;");
            cb.Line("break;");
            cb.Outdent();

            for (int i = 0; i < candidates.Count; i++)
            {
                string value = "_polyValue" + localIndex + "_" + i;
                cb.Line("case " + StableTypeId(candidates[i]) + ":");
                cb.Indent();
                cb.Line("var " + value + " = new " + CodeGenTypeUtil.TypeName(candidates[i]) + "();");
                cb.Line(value + ".Deserialize(buffer);");
                cb.Line(target + " = " + value + ";");
                cb.Line("break;");
                cb.Outdent();
            }

            cb.Line("default:");
            cb.Indent();
            cb.Line("throw new EjoyFramework.Core.FrameworkException(\"Unsupported polymorphic type id for generated member '"
                + EscapeString(memberName) + "': \" + " + typeId + ");");
            cb.Outdent();
            cb.Outdent();
            cb.Line("}");
        }

        private static string EscapeString(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool IsConcretePolymorphicBase(Type type, bool allowConcreteBase)
        {
            return allowConcreteBase
                && type != null
                && !type.IsValueType
                && type.IsClass
                && !type.IsAbstract
                && !type.IsSealed;
        }

        private static int StableTypeId(Type type)
        {
            string value = (type.Assembly.GetName().Name ?? string.Empty) + ":" + (type.FullName ?? type.Name);
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                int typeId = (int)(hash & 0x7fffffff);
                return typeId == 0 ? 1 : typeId;
            }
        }
    }

    public struct CodeGenCollectionShape
    {
        public CodeGenCollectionKind Kind;
        public Type ElementType;
        public Type KeyType;
        public Type ValueType;

        public bool IsDictionary => Kind == CodeGenCollectionKind.Dictionary;

        public static CodeGenCollectionShape Array(Type elementType)
        {
            return new CodeGenCollectionShape { Kind = CodeGenCollectionKind.Array, ElementType = elementType };
        }

        public static CodeGenCollectionShape List(Type elementType)
        {
            return new CodeGenCollectionShape { Kind = CodeGenCollectionKind.List, ElementType = elementType };
        }

        public static CodeGenCollectionShape HashSet(Type elementType)
        {
            return new CodeGenCollectionShape { Kind = CodeGenCollectionKind.HashSet, ElementType = elementType };
        }

        public static CodeGenCollectionShape Queue(Type elementType)
        {
            return new CodeGenCollectionShape { Kind = CodeGenCollectionKind.Queue, ElementType = elementType };
        }

        public static CodeGenCollectionShape Stack(Type elementType)
        {
            return new CodeGenCollectionShape { Kind = CodeGenCollectionKind.Stack, ElementType = elementType };
        }

        public static CodeGenCollectionShape Dictionary(Type keyType, Type valueType)
        {
            return new CodeGenCollectionShape
            {
                Kind = CodeGenCollectionKind.Dictionary,
                KeyType = keyType,
                ValueType = valueType,
            };
        }
    }

    public enum CodeGenCollectionKind
    {
        Array,
        List,
        HashSet,
        Queue,
        Stack,
        Dictionary,
    }
}
