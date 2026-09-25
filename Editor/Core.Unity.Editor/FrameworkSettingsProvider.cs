//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using EjoyFramework.Core.Unity.Editor.CodeGen;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Base
{
    /// <summary>
    /// EjoyFramework.Core 项目级配置面板，集成到 Project Settings → EjoyFramework.Core。
    /// 业务在此设置 framework 全局开关（log level / 性能采样间隔 / 调试热键 / Editor 工具偏好）。
    /// 配置存为 ScriptableObject 在 Assets/EjoyFrameworkSettings.asset。
    /// </summary>
    public static class FrameworkSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/EjoyFramework.Core", SettingsScope.Project)
            {
                label = "EjoyFramework.Core",
                guiHandler = OnGUI,
                keywords = new[] { "ejoy", "framework", "log", "debug", "performance" },
            };
        }

        private const string AssetPath = "Assets/EjoyFrameworkSettings.asset";
        private static FrameworkSettings s_Cache;

        private static FrameworkSettings LoadOrCreate()
        {
            if (s_Cache != null) return s_Cache;
            s_Cache = AssetDatabase.LoadAssetAtPath<FrameworkSettings>(AssetPath);
            if (s_Cache == null)
            {
                s_Cache = ScriptableObject.CreateInstance<FrameworkSettings>();
                AssetDatabase.CreateAsset(s_Cache, AssetPath);
                AssetDatabase.SaveAssets();
            }
            return s_Cache;
        }

        private static void OnGUI(string searchContext)
        {
            var s = LoadOrCreate();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Logging", EditorStyles.boldLabel);
            s.MinLogLevel = (EjoyFramework.Core.LogLevel)EditorGUILayout.EnumPopup("Min log level", s.MinLogLevel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Debugger", EditorStyles.boldLabel);
            s.DebuggerToggleKey = (KeyCode)EditorGUILayout.EnumPopup("Toggle key", s.DebuggerToggleKey);
            s.DebuggerShowAtStartup = EditorGUILayout.Toggle("Show at startup", s.DebuggerShowAtStartup);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Performance Monitor", EditorStyles.boldLabel);
            s.PerformanceWindowCapacity = EditorGUILayout.IntField("Sample buffer size", s.PerformanceWindowCapacity);
            s.SpikeThresholdMs = EditorGUILayout.FloatField("Spike threshold (ms)", s.SpikeThresholdMs);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("CodeGen output", EditorStyles.boldLabel);
            s.CodeGenNamespace = EditorGUILayout.TextField("Default namespace", s.CodeGenNamespace);
            s.CodeGenOutputDir = EditorGUILayout.TextField("Output directory", s.CodeGenOutputDir);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(s);
                AssetDatabase.SaveAssetIfDirty(s);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Settings saved at " + AssetPath + ". Commit it to share with the team.",
                MessageType.Info);
        }
    }

    /// <summary>Project-wide EjoyFramework.Core configuration ScriptableObject.</summary>
    public sealed class FrameworkSettings : ScriptableObject
    {
        public EjoyFramework.Core.LogLevel MinLogLevel = EjoyFramework.Core.LogLevel.Debug;
        public KeyCode DebuggerToggleKey = KeyCode.F1;
        public bool DebuggerShowAtStartup = false;
        public int PerformanceWindowCapacity = 240;
        public float SpikeThresholdMs = 33.3f;
        public string CodeGenNamespace = "EjoyFramework.Generated";
        public string CodeGenOutputDir = CodeGenTypeUtil.DefaultAssemblyGeneratedRoot;
    }
}
