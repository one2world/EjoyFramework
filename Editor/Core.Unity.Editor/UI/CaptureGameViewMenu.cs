//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.UI
{
    /// <summary>
    /// 给"GameView 窗口像素截图（含 Simulator 设备外框/notch/safe area）"提供一个 MenuItem 入口，
    /// 方便在 MCP 工具 capture_game_view 还没在 Python 端暴露时（首次添加后需 reconnect）也能手动触发，
    /// 同时也作为业务侧验证 UI 在目标设备上呈现效果的快捷出口。
    ///
    /// 反射调用 MCPForUnity.Editor.Helpers.EditorWindowScreenshotUtility.CaptureGameViewWindowToProject —
    /// 避免在本程序集硬依赖 MCP 包；若 MCP 不在项目中，菜单点击会给出明确错误而不是编译失败。
    /// </summary>
    public static class CaptureGameViewMenu
    {
        private const string MenuPath = "EjoyFramework/Core/UI/Capture Game View (Simulator)";

        [MenuItem(MenuPath)]
        public static void Capture()
        {
            string folder = "Assets/Screenshots";
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                // 反射定位 MCP 的 helper —— 跨程序集软依赖
                var helperType = ResolveHelperType();
                if (helperType == null)
                {
                    EditorUtility.DisplayDialog("Capture Game View",
                        "MCP For Unity helper not found. Install com.coplaydev.unity-mcp or check assembly references.",
                        "OK");
                    return;
                }

                var method = helperType.GetMethod("CaptureGameViewWindowToProject",
                    BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    EditorUtility.DisplayDialog("Capture Game View",
                        "EditorWindowScreenshotUtility.CaptureGameViewWindowToProject not found. The MCP package may be outdated.",
                        "OK");
                    return;
                }

                string fileName = "gameview-sim-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
                // Match the latest CaptureGameViewWindowToProject signature, which has an
                // additional `cropToDevice` parameter at the end. In Simulator mode this crops to
                // the DeviceView preview element. If the parameter list mismatches (older MCP
                // package), fall back to the 7-arg version.
                object[] args;
                var paramInfos = method.GetParameters();
                if (paramInfos.Length == 8)
                {
                    args = new object[]
                    {
                        fileName,
                        /*ensureUniqueFileName*/ true,
                        /*includeImage*/        false,
                        /*maxResolution*/       640,
                        /*folderOverride*/      folder,
                        /*viewportWidth*/       0,   // out
                        /*viewportHeight*/      0,   // out
                        /*cropToDevice*/        true,
                    };
                }
                else
                {
                    args = new object[]
                    {
                        fileName,
                        /*ensureUniqueFileName*/ true,
                        /*includeImage*/        false,
                        /*maxResolution*/       640,
                        /*folderOverride*/      folder,
                        /*viewportWidth*/       0,
                        /*viewportHeight*/      0,
                    };
                }
                object result = method.Invoke(null, args);

                // Reflection-extract returned ScreenshotCaptureResult.ProjectRelativePath / FullPath
                if (result != null)
                {
                    var resType = result.GetType();
                    var pathProp = resType.GetProperty("ProjectRelativePath");
                    var fullProp = resType.GetProperty("FullPath");
                    string rel = pathProp?.GetValue(result) as string;
                    string full = fullProp?.GetValue(result) as string;
                    int w = (int)args[5];
                    int h = (int)args[6];

                    Debug.Log($"[CaptureGameView] Saved {w}x{h} → {rel}");
                    if (!string.IsNullOrEmpty(rel))
                    {
                        AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceSynchronousImport);
                        var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                        if (asset != null) EditorGUIUtility.PingObject(asset);
                    }
                }
            }
            catch (TargetInvocationException tie)
            {
                Debug.LogError($"[CaptureGameView] {tie.InnerException?.Message ?? tie.Message}");
                EditorUtility.DisplayDialog("Capture Game View",
                    "Capture failed: " + (tie.InnerException?.Message ?? tie.Message),
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CaptureGameView] {ex.Message}");
                EditorUtility.DisplayDialog("Capture Game View", "Capture failed: " + ex.Message, "OK");
            }
        }

        private static Type ResolveHelperType()
        {
            // Try direct qualified name first
            Type t = Type.GetType("MCPForUnity.Editor.Helpers.EditorWindowScreenshotUtility,MCPForUnity.Editor");
            if (t != null) return t;

            // Fall back to scanning loaded assemblies
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var candidate = a.GetType("MCPForUnity.Editor.Helpers.EditorWindowScreenshotUtility");
                    if (candidate != null) return candidate;
                }
                catch { /* swallow */ }
            }
            return null;
        }
    }
}
