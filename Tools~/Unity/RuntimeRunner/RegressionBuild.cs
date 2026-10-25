#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class RegressionBuild
{
    static string Argument(string name) { var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,name);return a[i+1]; }
    public static void Build()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,(ScriptingImplementation)Enum.Parse(typeof(ScriptingImplementation),Argument("-regressionBackend")));
        EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX),"Architecture","arm64");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Runtime NUnit bootstrap").AddComponent<RegressionBootstrap>();
        const string path="Assets/RuntimeRunner/RegressionScene.unity";EditorSceneManager.SaveScene(scene,path);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{path},locationPathName=Argument("-regressionOutput"),target=BuildTarget.StandaloneOSX,options=BuildOptions.Development|BuildOptions.IncludeTestAssemblies});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Regression player build failed: "+report.summary.result);
    }
}
#endif
