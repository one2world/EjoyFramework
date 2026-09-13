//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EjoyFramework.Tests
{
    [TestFixture]
    public sealed class AssemblyCSharpSnapshotResourceComparisonTests
    {
        private const string BuilderTypeName =
            "EjoyFramework.Tests.AssemblyCSharpCodeGenSamples.Editor.AssemblyCSharpSnapshotSampleResourceBuilder";

        [Test]
        public void CaptureAndRestore_MatchesAssemblyCSharpPrefabGoldenResource()
        {
            Type builder = RequireBuilderType();
            string prefabPath = GetPublicStringField(builder, "PrefabPath");
            string goldenPath = GetPublicStringField(builder, "GoldenPath");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, "Missing prefab resource: " + prefabPath);
            Assert.That(File.Exists(goldenPath), Is.True, "Missing golden resource: " + goldenPath);

            string expected = Normalize(File.ReadAllText(goldenPath));
            GameObject source = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject target = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            try
            {
                Invoke(builder, "RefreshRuntimeData", source);
                Invoke(builder, "RefreshRuntimeData", target);

                byte[] sourceSnapshot = (byte[])Invoke(builder, "CaptureArenaSnapshot", source);
                string sourceReport = (string)Invoke(builder, "BuildReportJson", source, sourceSnapshot);
                Assert.That(sourceReport, Is.EqualTo(expected),
                    "The committed Assembly-CSharp prefab no longer matches the golden report.");

                Invoke(builder, "MutateArena", target);
                Invoke(builder, "RestoreArenaSnapshot", target, sourceSnapshot);

                byte[] restoredSnapshot = (byte[])Invoke(builder, "CaptureArenaSnapshot", target);
                string restoredReport = (string)Invoke(builder, "BuildReportJson", target, restoredSnapshot);
                Assert.That(restoredReport, Is.EqualTo(expected),
                    "Assembly-CSharp snapshot restore did not reconstruct the complex runtime state.");

                string ignoredCounters = (string)Invoke(builder, "BuildIgnoredCounterReport", target);
                Assert.That(ignoredCounters, Is.EqualTo("-5101,-5201,-6101,-6201"));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        private static Type RequireBuilderType()
        {
            Type type = Type.GetType(BuilderTypeName + ", Assembly-CSharp-Editor");
            if (type != null) return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(BuilderTypeName, false);
                if (type != null) return type;
            }

            Assert.Fail("Missing Assembly-CSharp editor builder type: " + BuilderTypeName);
            return null;
        }

        private static string GetPublicStringField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "Missing public static field: " + fieldName);
            return (string)field.GetValue(null);
        }

        private static object Invoke(Type type, string methodName, params object[] args)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Missing public static method: " + methodName);
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static string Normalize(string value)
        {
            return value.Replace("\r\n", "\n");
        }
    }
}
