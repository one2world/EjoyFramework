//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 由生成代码 <c>HelperFactoryRegistrations</c>（HelperFactoryGenerator 产出）登记的
    /// 「类型全名 → 实例工厂」表，取代按 Inspector 字符串类型名反射创建 helper / procedure
    /// （<see cref="FrameworkLog.ILogHelper"/> / <see cref="Utility.Json.IJsonHelper"/> /
    /// <see cref="EjoyFramework.Core.UI.IUIFormHelper"/> / <see cref="EjoyFramework.Core.Procedures.ProcedureBase"/>）。
    ///
    /// 玩家构建零反射、IL2CPP 不会裁剪这些类型/构造函数。<paramref name="parent"/> 仅供 MonoBehaviour 型 helper
    /// 作为挂载父节点（工厂内部新建子 GameObject 并 AddComponent）；纯类工厂忽略它。
    /// Editor 下未登记的名字退回一次性反射（GetType + new/AddComponent），便于编辑期与单测；玩家构建未登记即返回 null。
    /// 保留 Inspector 的类型名字符串配置不变 —— 仅替换「字符串 → 实例」的解析机制。
    /// </summary>
    public static class GeneratedHelperFactory
    {
        private static readonly Dictionary<string, Func<Transform, object>> s_Factories
            = new Dictionary<string, Func<Transform, object>>(StringComparer.Ordinal);

        /// <summary>登记某类型全名的实例工厂（由生成代码调用，幂等覆盖）。</summary>
        public static void Register(string fullTypeName, Func<Transform, object> factory)
        {
            if (string.IsNullOrEmpty(fullTypeName) || factory == null) return;
            s_Factories[fullTypeName] = factory;
        }

        /// <summary>
        /// 按类型全名创建实例。<paramref name="parent"/> 供 MonoBehaviour helper 挂载（可为 null）。
        /// 未登记时：Editor 反射兜底，玩家构建返回 null。
        /// </summary>
        public static object Create(string fullTypeName, Transform parent)
        {
            if (string.IsNullOrEmpty(fullTypeName)) return null;
            if (s_Factories.TryGetValue(fullTypeName, out Func<Transform, object> factory)) return factory(parent);
#if UNITY_EDITOR
            return CreateByReflectionEditorOnly(fullTypeName, parent);
#else
            return null;
#endif
        }

#if UNITY_EDITOR
        // Editor-only 反射兜底：玩家构建中此方法不存在，故设备包不依赖反射。
        private static object CreateByReflectionEditorOnly(string fullTypeName, Transform parent)
        {
            Type t = Utility.Assembly.GetType(fullTypeName);
            if (t == null) return null;
            if (typeof(MonoBehaviour).IsAssignableFrom(t))
            {
                var go = new GameObject(t.Name);
                if (parent != null) go.transform.SetParent(parent, false);
                return go.AddComponent(t);
            }
            return Activator.CreateInstance(t);
        }
#endif
    }
}
