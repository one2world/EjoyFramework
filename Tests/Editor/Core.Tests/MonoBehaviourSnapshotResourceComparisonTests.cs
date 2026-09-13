//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Tests
{
    [TestFixture]
    public sealed class MonoBehaviourSnapshotResourceComparisonTests
    {
        [Test]
        public void CaptureAndRestore_MatchesConcretePrefabGoldenResource()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                MonoBehaviourSnapshotSampleResourceBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null, "Missing prefab resource: "
                + MonoBehaviourSnapshotSampleResourceBuilder.PrefabPath);
            string goldenPath = EjoyFramework.Core.Unity.Editor.CodeGen.CodeGenPath.Resolve(
                MonoBehaviourSnapshotSampleResourceBuilder.GoldenPath);
            Assert.That(File.Exists(goldenPath), Is.True,
                "Missing golden resource: " + MonoBehaviourSnapshotSampleResourceBuilder.GoldenPath);

            string expected = Normalize(File.ReadAllText(goldenPath));
            GameObject source = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject target = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            try
            {
                byte[] sourceSnapshot = MonoBehaviourSnapshotSampleResourceBuilder.CaptureArenaSnapshot(source);
                string sourceReport = MonoBehaviourSnapshotSampleResourceBuilder.BuildReportJson(source, sourceSnapshot);
                Assert.That(sourceReport, Is.EqualTo(expected), "The committed prefab no longer matches the golden report.");

                MonoBehaviourSnapshotSampleResourceBuilder.MutateArena(target);
                MonoBehaviourSnapshotSampleResourceBuilder.RestoreArenaSnapshot(target, sourceSnapshot);

                byte[] restoredSnapshot = MonoBehaviourSnapshotSampleResourceBuilder.CaptureArenaSnapshot(target);
                string restoredReport = MonoBehaviourSnapshotSampleResourceBuilder.BuildReportJson(target, restoredSnapshot);
                Assert.That(restoredReport, Is.EqualTo(expected), "Snapshot restore did not reconstruct the prefab state.");

                MonoBehaviourSnapshotSampleResourceBuilder.SnapshotSampleObjectMap restoredMap
                    = MonoBehaviourSnapshotSampleResourceBuilder.CreateObjectMap(target);
                Assert.That(restoredMap.Hero.RuntimeCacheVersion, Is.EqualTo(-9101));
                Assert.That(restoredMap.Hero.NonSerializedCounter, Is.EqualTo(-9201));
                Assert.That(restoredMap.Boss.RuntimeCacheVersion, Is.EqualTo(-9102));
                Assert.That(restoredMap.Boss.NonSerializedCounter, Is.EqualTo(-9202));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        private static string Normalize(string value)
        {
            return value.Replace("\r\n", "\n");
        }
    }
}
