using System;
using UnityEditor;
using UnityEngine;
namespace EjoyFramework.Tests
{
    public static class AssemblyCSharpSnapshotSmoke
    {
        public static void RunAssemblyCSharpSnapshotSmoke()
        {
            try
            {
                var codeGenTests = new CodeGenGeneratorTests();
                codeGenTests.CodeGenTypeUtil_ResolvesGeneratedPathForUnityDefaultAssembly();
                codeGenTests.CodeGenTypeUtil_SkipsUnknownNoAsmdefAssemblies();

                var resourceTests = new AssemblyCSharpSnapshotResourceComparisonTests();
                resourceTests.CaptureAndRestore_MatchesAssemblyCSharpPrefabGoldenResource();

                var performanceTests = new MonoBehaviourSnapshotPerformanceTests();
                performanceTests.GeneratedArenaSnapshot_RuntimePerformanceBudget();

                Debug.Log("CodeGen Assembly-CSharp snapshot smoke passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
