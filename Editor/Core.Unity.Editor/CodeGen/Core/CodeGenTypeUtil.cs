//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using CompilationPipeline = UnityEditor.Compilation.CompilationPipeline;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// 代码生成共享工具：AppDomain 类型扫描、C# 合法类型名渲染。
    /// 集中了原先散落在 EventArgsGenerator / PacketHandlerRegistryGenerator 等处的重复逻辑。
    /// </summary>
    public static class CodeGenTypeUtil
    {
        public const string AssemblyCSharpName = "Assembly-CSharp";
        public const string AssemblyCSharpEditorName = "Assembly-CSharp-Editor";
        public const string DefaultAssemblyGeneratedRoot = "Assets/Generated";
        public const string DefaultEditorAssemblyGeneratedRoot = "Assets/Editor/Generated";
        public const string GeneratedNamespaceSuffix = ".Generated";

        /// <summary>
        /// 遍历当前 AppDomain 全部已加载类型；单个程序集加载失败时救捞其可加载类型，
        /// 不让一个坏程序集吞掉所有结果（沿用既有生成器约定）。
        /// </summary>
        public static IEnumerable<Type> EnumerateAllTypes()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("CodeGenTypeUtil: skipped assembly '" + asm.FullName + "': " + ex.Message);
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    if (types[i] != null) yield return types[i];
                }
            }
        }

        /// <summary>查找标记了指定属性的所有具体（非抽象、非接口）类型。</summary>
        public static List<Type> FindTypesWithAttribute(Type attributeType, bool concreteOnly = true)
        {
            var result = new List<Type>();
            foreach (Type t in EnumerateAllTypes())
            {
                if (t.IsInterface) continue;
                if (concreteOnly && t.IsAbstract) continue;
                if (Attribute.IsDefined(t, attributeType, false)) result.Add(t);
            }
            return result;
        }

        public static bool TryGetAssemblyGeneratedPath(
            Type type,
            string category,
            string fileName,
            out string path,
            out string reason)
        {
            path = null;
            reason = null;
            if (type == null)
            {
                reason = "type is null";
                return false;
            }

            return TryGetAssemblyGeneratedPath(type.Assembly, category, fileName, out path, out reason);
        }

        public static bool TryGetAssemblyGeneratedPath(
            Assembly assembly,
            string category,
            string fileName,
            out string path,
            out string reason)
        {
            path = null;
            reason = null;
            if (assembly == null)
            {
                reason = "assembly is null";
                return false;
            }

            string assemblyName = assembly.GetName().Name;
            string asmdefPath = UnityEditor.Compilation.CompilationPipeline
                .GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            if (string.IsNullOrEmpty(asmdefPath))
            {
                if (TryGetDefaultAssemblyGeneratedPath(assemblyName, category, fileName, out path, out reason))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(reason))
                {
                    reason = "assembly '" + assemblyName + "' has no asmdef; cannot emit generated partial into declaring assembly.";
                }
                return false;
            }

            asmdefPath = CodeGenPath.Resolve(asmdefPath);
            string dir = Path.GetDirectoryName(asmdefPath);
            if (string.IsNullOrEmpty(dir))
            {
                reason = "assembly '" + assemblyName + "' asmdef path is invalid.";
                return false;
            }

            path = CombineGeneratedPath(dir.Replace('\\', '/') + "/Generated", category, fileName);
            if (!IsGeneratedPathOwnedByAssembly(path, asmdefPath, out reason))
            {
                path = null;
                return false;
            }

            return true;
        }

        public static string GeneratedNamespaceForAssembly(Assembly assembly)
        {
            if (assembly == null) return "EjoyFramework.Generated";

            string assemblyName = assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName)) return "EjoyFramework.Generated";

            return SanitizeNamespace(assemblyName) + GeneratedNamespaceSuffix;
        }

        public static string GeneratedFileName(Type type)
        {
            string name = type == null ? "Unknown" : (type.FullName ?? type.Name);
            var sb = new StringBuilder(name.Length + 5);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            sb.Append(".g.cs");
            return sb.ToString();
        }

        /// <summary>
        /// 渲染 C# 合法的类型名（递归处理泛型/数组/嵌套类型）。
        /// <c>Type.FullName</c> 对泛型给出不可编译的字符串（如 <c>List`1[[System.Int32, ...]]</c>），
        /// 本方法产出 <c>Namespace.Name&lt;T1, T2&gt;</c>，并把嵌套类型的 '+' 归一化为 '.'。
        /// （从原 EventArgsGenerator.TypeName 提取，作为公共工具。）
        /// </summary>
        public static string TypeName(Type type)
        {
            if (type == null) return "object";

            if (type.IsArray)
            {
                int rank = type.GetArrayRank();
                string commas = rank > 1 ? new string(',', rank - 1) : string.Empty;
                return TypeName(type.GetElementType()) + "[" + commas + "]";
            }

            if (type.IsGenericType)
            {
                Type def = type.GetGenericTypeDefinition();
                string fullName = def.FullName ?? def.Name;
                int tick = fullName.IndexOf('`');
                string baseName = tick >= 0 ? fullName.Substring(0, tick) : fullName;
                baseName = baseName.Replace('+', '.');
                Type[] args = type.GetGenericArguments();
                var rendered = new StringBuilder();
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) rendered.Append(", ");
                    rendered.Append(TypeName(args[i]));
                }
                return baseName + "<" + rendered + ">";
            }

            string name = type.FullName ?? type.Name;
            return name.Replace('+', '.');
        }

        public static bool TryGetDeterministicCompareExpression(
            Type type,
            string left,
            string right,
            out string expression)
        {
            expression = null;
            if (type == null) return false;

            if (type == typeof(string))
            {
                expression = "System.StringComparer.Ordinal.Compare(" + left + ", " + right + ")";
                return true;
            }

            if (type == typeof(bool)
                || type == typeof(byte)
                || type == typeof(sbyte)
                || type == typeof(short)
                || type == typeof(ushort)
                || type == typeof(char)
                || type == typeof(int)
                || type == typeof(uint)
                || type == typeof(long)
                || type == typeof(ulong)
                || type == typeof(float)
                || type == typeof(double)
                || type.IsEnum
                || ImplementsComparable(type))
            {
                expression = "System.Collections.Generic.Comparer<" + TypeName(type) + ">.Default.Compare("
                    + left + ", " + right + ")";
                return true;
            }

            return false;
        }

        public static bool HasDeterministicComparer(Type type)
        {
            string expression;
            return TryGetDeterministicCompareExpression(type, "_left", "_right", out expression);
        }

        public static bool CanReferenceTypeFrom(Type type, Assembly generatedAssembly)
        {
            if (type == null) return false;
            if (type.IsPointer || type.IsByRef || type.IsGenericParameter) return false;

            if (type.IsArray)
            {
                return CanReferenceTypeFrom(type.GetElementType(), generatedAssembly);
            }

            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                if (!CanReferenceTypeDefinitionFrom(definition, generatedAssembly)) return false;
                if (!CanAssemblyReference(generatedAssembly, definition.Assembly)) return false;

                Type[] arguments = type.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (!CanReferenceTypeFrom(arguments[i], generatedAssembly)) return false;
                }
                return true;
            }

            return CanReferenceTypeDefinitionFrom(type, generatedAssembly)
                && CanAssemblyReference(generatedAssembly, type.Assembly);
        }

        public static bool HasCallableParameterlessConstructor(Type type, Assembly generatedAssembly)
        {
            if (type == null) return false;
            if (type.IsValueType) return true;

            ConstructorInfo ctor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (ctor == null) return false;
            if (ctor.IsPublic) return true;
            return IsSameAssembly(type.Assembly, generatedAssembly)
                && (ctor.IsAssembly || ctor.IsFamilyOrAssembly);
        }

        private static bool CanReferenceTypeDefinitionFrom(Type type, Assembly generatedAssembly)
        {
            if (type == null) return false;

            if (type.IsNested)
            {
                if (!CanReferenceTypeDefinitionFrom(type.DeclaringType, generatedAssembly)) return false;
                if (type.IsNestedPublic) return true;
                return IsSameAssembly(type.Assembly, generatedAssembly)
                    && (type.IsNestedAssembly || type.IsNestedFamORAssem);
            }

            if (type.IsPublic) return true;
            return type.IsNotPublic && IsSameAssembly(type.Assembly, generatedAssembly);
        }

        private static bool IsSameAssembly(Assembly left, Assembly right)
        {
            return left != null && right != null && left == right;
        }

        public static bool CanAssemblyReference(Assembly fromAssembly, Assembly targetAssembly)
        {
            if (fromAssembly == null || targetAssembly == null) return false;
            string fromName = fromAssembly.GetName().Name;
            string targetName = targetAssembly.GetName().Name;
            if (string.IsNullOrEmpty(fromName) || string.IsNullOrEmpty(targetName)) return false;
            if (string.Equals(fromName, targetName, StringComparison.Ordinal)) return true;

            string targetAsmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(targetName);
            if (string.IsNullOrEmpty(targetAsmdef))
            {
                if (IsImplicitlyReferencedAssembly(targetAssembly)) return true;
                return IsUnityDefaultEditorAssemblyName(fromName) && IsUnityDefaultRuntimeAssemblyName(targetName);
            }

            return HasDirectAssemblyReference(fromName, targetName);
        }

        public static bool IsTestOrEditorAssembly(Assembly assembly)
        {
            if (assembly == null) return false;

            string name = assembly.GetName().Name ?? string.Empty;
            if (name.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.EndsWith(".Editor", StringComparison.Ordinal)) return true;

            AssemblyName[] references = assembly.GetReferencedAssemblies();
            for (int i = 0; i < references.Length; i++)
            {
                string referenceName = references[i].Name;
                if (referenceName != null
                    && referenceName.StartsWith("nunit", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetDefaultAssemblyGeneratedPath(
            string assemblyName,
            string category,
            string fileName,
            out string path,
            out string reason)
        {
            path = null;
            reason = null;
            string root;
            if (IsUnityDefaultRuntimeAssemblyName(assemblyName))
            {
                root = DefaultAssemblyGeneratedRoot;
            }
            else if (IsUnityDefaultEditorAssemblyName(assemblyName))
            {
                root = DefaultEditorAssemblyGeneratedRoot;
            }
            else
            {
                return false;
            }

            path = CombineGeneratedPath(root, category, fileName);
            if (!IsGeneratedPathOwnedByAssembly(path, null, out reason))
            {
                path = null;
                return false;
            }

            return true;
        }

        private static string CombineGeneratedPath(string root, string category, string fileName)
        {
            if (string.IsNullOrEmpty(category)) return root + "/" + fileName;
            return root + "/" + category.Trim('/') + "/" + fileName;
        }

        private static bool IsUnityDefaultRuntimeAssemblyName(string assemblyName)
        {
            return string.Equals(assemblyName, AssemblyCSharpName, StringComparison.Ordinal);
        }

        private static bool IsUnityDefaultEditorAssemblyName(string assemblyName)
        {
            return string.Equals(assemblyName, AssemblyCSharpEditorName, StringComparison.Ordinal);
        }

        private static bool HasDirectAssemblyReference(string fromName, string targetName)
        {
            UnityEditor.Compilation.Assembly from = FindCompilationAssembly(fromName);
            if (from == null) return false;

            UnityEditor.Compilation.Assembly[] references = from.assemblyReferences;
            if (references == null) return false;

            for (int i = 0; i < references.Length; i++)
            {
                UnityEditor.Compilation.Assembly reference = references[i];
                if (reference == null || string.IsNullOrEmpty(reference.name)) continue;
                if (string.Equals(reference.name, targetName, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static UnityEditor.Compilation.Assembly FindCompilationAssembly(string assemblyName)
        {
            UnityEditor.Compilation.Assembly[] assemblies = CompilationPipeline.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i] != null
                    && string.Equals(assemblies[i].name, assemblyName, StringComparison.Ordinal))
                {
                    return assemblies[i];
                }
            }
            return null;
        }

        private static bool IsGeneratedPathOwnedByAssembly(string generatedPath, string expectedAsmdefPath, out string reason)
        {
            reason = null;
            string generatedDir = Path.GetDirectoryName((generatedPath ?? string.Empty).Replace('\\', '/'));
            string ownerAsmdef = FindNearestAsmdefPath(generatedDir);
            string normalizedExpected = NormalizeAssetPath(expectedAsmdefPath);

            if (string.IsNullOrEmpty(normalizedExpected))
            {
                if (string.IsNullOrEmpty(ownerAsmdef)) return true;
                reason = "generated path '" + generatedPath + "' is under asmdef '" + ownerAsmdef
                    + "'; cannot emit into Unity default assembly.";
                return false;
            }

            if (string.Equals(ownerAsmdef, normalizedExpected, StringComparison.Ordinal)) return true;

            reason = "generated path '" + generatedPath + "' is owned by asmdef '"
                + (ownerAsmdef ?? "<none>") + "' instead of declaring asmdef '" + normalizedExpected + "'.";
            return false;
        }

        private static string FindNearestAsmdefPath(string assetDirectory)
        {
            string dir = NormalizeAssetPath(assetDirectory);
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(dir))
                {
                    string[] asmdefs = Directory.GetFiles(dir, "*.asmdef", SearchOption.TopDirectoryOnly);
                    if (asmdefs.Length > 0)
                    {
                        Array.Sort(asmdefs, StringComparer.Ordinal);
                        return NormalizeAssetPath(asmdefs[0]);
                    }
                }

                string parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.Ordinal)) break;
                dir = parent.Replace('\\', '/');
            }

            return null;
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static string SanitizeNamespace(string value)
        {
            var sb = new StringBuilder(value.Length);
            bool start = true;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '.')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '.') sb.Append('.');
                    start = true;
                    continue;
                }

                bool valid = start ? (char.IsLetter(c) || c == '_') : (char.IsLetterOrDigit(c) || c == '_');
                if (valid)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(start ? '_' : '_');
                }
                start = false;
            }

            string result = sb.ToString().Trim('.');
            return string.IsNullOrEmpty(result) ? "EjoyFramework" : result;
        }

        private static bool IsImplicitlyReferencedAssembly(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic) return false;

            string name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name)) return false;

            return string.Equals(name, "mscorlib", StringComparison.Ordinal)
                || string.Equals(name, "netstandard", StringComparison.Ordinal)
                || string.Equals(name, "System", StringComparison.Ordinal)
                || name.StartsWith("System.", StringComparison.Ordinal)
                || string.Equals(name, "UnityEngine", StringComparison.Ordinal)
                || name.StartsWith("UnityEngine.", StringComparison.Ordinal)
                || string.Equals(name, "UnityEditor", StringComparison.Ordinal)
                || name.StartsWith("UnityEditor.", StringComparison.Ordinal);
        }

        private static bool ImplementsComparable(Type type)
        {
            if (typeof(IComparable).IsAssignableFrom(type)) return true;
            Type comparable = typeof(IComparable<>).MakeGenericType(type);
            return comparable.IsAssignableFrom(type);
        }

    }
}
