//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using EjoyFramework.Core.Unity.Editor.CodeGen;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity.Editor.UI.Mvvm
{
    /// <summary>
    /// 自动绑定生成器：扫描 prefab 内 B_&lt;Property&gt;_&lt;Widget&gt; 命名的子节点，
    /// 在 prefab 上加挂对应 binder MonoBehaviour，并把 PropertyPath 写为 &lt;Property&gt;。
    /// 命名约定：
    ///   B_HpValue_Text      → TextBinder(PropertyPath="HpValue")
    ///   B_PlayButton_Button → ButtonBinder(PropertyPath="PlayCommand") —— 自动补 "Command" 后缀
    ///   B_NameInput_Input   → InputFieldBinder(PropertyPath="Name", Mode=TwoWay)
    ///   B_Music_Toggle      → ToggleBinder(PropertyPath="Music", Mode=TwoWay)
    ///   B_Volume_Slider     → SliderBinder(PropertyPath="Volume", Mode=TwoWay)
    ///   B_Avatar_Image      → ImageBinder(PropertyPath="Avatar")
    ///   B_LoadingHint_Visible → VisibilityBinder(PropertyPath="IsLoading")
    ///
    /// 把繁琐的"手挂 binder + 填路径"自动化 —— 业务的 prefab 命好名即可，菜单一键产出可用 prefab。
    /// </summary>
    public static class MvvmBindingsGenerator
    {
        private const string BindingPrefix = "B_";

        [MenuItem("EjoyFramework/Core/UI/Auto-Wire Bindings on Selected Prefab")]
        public static void WireSelectedPrefab()
        {
            var obj = Selection.activeObject;
            if (!(obj is GameObject prefab) || !PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                EditorUtility.DisplayDialog("MVVM Auto-Wire", "Select a UIForm prefab asset first.", "OK");
                return;
            }
            string path = AssetDatabase.GetAssetPath(prefab);

            int added = WireBindersInternal(path);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("MVVM Auto-Wire",
                "Wired " + added + " binder(s) on:\n  " + path +
                "\n\nB_&lt;Property&gt;_&lt;Widget&gt; children now carry the matching binder MonoBehaviour with PropertyPath filled in.",
                "OK");
        }

        /// <summary>程序化入口：返回新增/更新的 binder 数量。</summary>
        public static int WireBindersInternal(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            int wiredCount = 0;
            try
            {
                Wire(root.transform, ref wiredCount);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return wiredCount;
        }

        private static void Wire(Transform t, ref int wiredCount)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name.StartsWith(BindingPrefix, StringComparison.Ordinal))
                {
                    if (TryWireOne(c.gameObject)) wiredCount++;
                }
                Wire(c, ref wiredCount);
            }
        }

        private static bool TryWireOne(GameObject go)
        {
            // 解析名字：B_<Property>_<Widget>
            string raw = go.name;
            string body = raw.Substring(BindingPrefix.Length);
            int sep = body.LastIndexOf('_');
            if (sep <= 0 || sep >= body.Length - 1)
            {
                // 节点名 B_XXX 但缺 _<Widget> 后缀 —— 不知道挂哪个 binder，跳过。
                return false;
            }
            string propertyName = body.Substring(0, sep);
            string widgetTag = body.Substring(sep + 1);

            switch (widgetTag.ToLowerInvariant())
            {
                case "text":
                    return WireText(go, propertyName);
                case "tmptext":
                case "tmp":
                    return WireBinder<EjoyFramework.Core.Unity.TMPTextBinder>(go, propertyName, twoWay: false);
                case "image":
                case "img":
                    return WireBinder<EjoyFramework.Core.Unity.ImageBinder>(go, propertyName, twoWay: false);
                case "rawimage":
                    return WireBinder<EjoyFramework.Core.Unity.RawImageBinder>(go, propertyName, twoWay: false);
                case "button":
                case "btn":
                    return WireButton(go, propertyName);
                case "toggle":
                    return WireBinder<EjoyFramework.Core.Unity.ToggleBinder>(go, propertyName, twoWay: true);
                case "slider":
                    return WireBinder<EjoyFramework.Core.Unity.SliderBinder>(go, propertyName, twoWay: true);
                case "dropdown":
                    return WireBinder<EjoyFramework.Core.Unity.DropdownBinder>(go, propertyName, twoWay: true);
                case "input":
                case "inputfield":
                    return WireBinder<EjoyFramework.Core.Unity.InputFieldBinder>(go, propertyName, twoWay: true);
                case "visible":
                case "visibility":
                    return WireBinder<EjoyFramework.Core.Unity.VisibilityBinder>(go, propertyName, twoWay: false);
                case "interactable":
                    return WireBinder<EjoyFramework.Core.Unity.InteractableBinder>(go, propertyName, twoWay: false);
                case "color":
                    return WireBinder<EjoyFramework.Core.Unity.ColorBinder>(go, propertyName, twoWay: false);
                case "list":
                    return WireBinder<EjoyFramework.Core.Unity.ListBinder>(go, propertyName, twoWay: false);
                default:
                    return false;
            }
        }

        private static bool WireText(GameObject go, string propertyName)
        {
            // 如果有 TMP_Text 则用 TMPTextBinder；否则用 TextBinder
            var tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro", throwOnError: false);
            if (tmpType != null && go.GetComponent(tmpType) != null)
                return WireBinder<EjoyFramework.Core.Unity.TMPTextBinder>(go, propertyName, twoWay: false);
            return WireBinder<EjoyFramework.Core.Unity.TextBinder>(go, propertyName, twoWay: false);
        }

        private static bool WireButton(GameObject go, string propertyName)
        {
            // Button 绑定 ICommand —— 属性名自动补 "Command" 后缀（若未带）。
            string cmdProp = propertyName.EndsWith("Command", StringComparison.Ordinal) ? propertyName : propertyName + "Command";
            return WireBinder<EjoyFramework.Core.Unity.ButtonBinder>(go, cmdProp, twoWay: false);
        }

        private static bool WireBinder<TBinder>(GameObject go, string propertyName, bool twoWay)
            where TBinder : EjoyFramework.Core.Unity.BinderBase
        {
            var binder = go.GetComponent<TBinder>();
            bool isNew = binder == null;
            if (isNew) binder = go.AddComponent<TBinder>();

            var so = new SerializedObject(binder);
            so.FindProperty("m_PropertyPath").stringValue = propertyName;
            // 仅在新建时设置 mode（保留用户手动覆盖）
            if (isNew)
            {
                so.FindProperty("m_Mode").enumValueIndex = twoWay
                    ? (int)EjoyFramework.Core.UI.Mvvm.BindingMode.TwoWay
                    : (int)EjoyFramework.Core.UI.Mvvm.BindingMode.OneWay;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return isNew;
        }
    }
}
