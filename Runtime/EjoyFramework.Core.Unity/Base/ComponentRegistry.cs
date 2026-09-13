//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Unity 层组件注册表：保存 BaseComponent 系列 GameFrameworkComponent 实例，供游戏层 GameEntry 静态访问器拉取。
    /// 旧名 GameEntry（与游戏层 EjoyGame.GameEntry 命名冲突，2026-05-17 重命名为 ComponentRegistry）。
    /// </summary>
    public static class ComponentRegistry
    {
        // 类型 → 组件，O(1) 查找。GameEntry.X 每次属性访问都走 GetComponent，原线性扫描在热路径上不划算。
        private static readonly Dictionary<Type, GameFrameworkComponent> s_ByType = new Dictionary<Type, GameFrameworkComponent>();

        /// <summary>
        /// 游戏框架所在的场景编号。
        /// </summary>
        internal const int GameFrameworkSceneId = 0;

        /// <summary>
        /// 获取游戏框架组件。
        /// </summary>
        /// <typeparam name="T">要获取的游戏框架组件类型。</typeparam>
        /// <returns>要获取的游戏框架组件。</returns>
        public static T GetComponent<T>() where T : GameFrameworkComponent
        {
            return (T)GetComponent(typeof(T));
        }

        /// <summary>
        /// Tries to resolve an optional framework component.
        /// </summary>
        public static bool TryGetComponent<T>(out T component) where T : GameFrameworkComponent
        {
            component = GetComponent<T>();
            return component != null;
        }

        /// <summary>
        /// Resolves a required framework component.
        /// </summary>
        /// <exception cref="FrameworkException">The component is not registered or has been destroyed.</exception>
        public static T RequireComponent<T>() where T : GameFrameworkComponent
        {
            T component = GetComponent<T>();
            if (component == null)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "Required framework component '{0}' is not registered. "
                    + "Add it to the EjoyFramework prefab or use TryGetComponent<T> for optional features.",
                    typeof(T).FullName));
            }

            return component;
        }

        /// <summary>
        /// 获取游戏框架组件（O(1)）。
        /// </summary>
        /// <param name="type">要获取的游戏框架组件类型。</param>
        /// <returns>要获取的游戏框架组件。</returns>
        public static GameFrameworkComponent GetComponent(Type type)
        {
            if (type == null || !s_ByType.TryGetValue(type, out var c))
            {
                return null;
            }

            // 读时自愈（防御性兜底）：Unity 销毁的 MonoBehaviour 仍持有 C# 引用，但 Unity 重载的 == 视其为 null。
            // 正常路径下，子类经 GameFrameworkComponent.OnDestroy（protected virtual 模板，子类 override 须调用
            // base.OnDestroy）已确定性注销；此处仅兜底极端情形（OnDestroy 未及运行等），就地剔除已销毁项并返回 null，
            // 而非返回失效旧实例。
            if (IsDestroyed(c))
            {
                s_ByType.Remove(type);
                return null;
            }

            return c;
        }

        /// <summary>
        /// 获取游戏框架组件（按类型名，少用路径，线性匹配）。
        /// </summary>
        /// <param name="typeName">要获取的游戏框架组件类型名称。</param>
        /// <returns>要获取的游戏框架组件。</returns>
        public static GameFrameworkComponent GetComponent(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            foreach (var kv in s_ByType)
            {
                Type type = kv.Key;
                if (type.FullName == typeName || type.Name == typeName)
                {
                    // 读时自愈：跳过已被 Unity 销毁的实例（延迟到下一次 O(1) 访问再剔除，避免遍历中改字典）。
                    return IsDestroyed(kv.Value) ? null : kv.Value;
                }
            }
            return null;
        }

        /// <summary>
        /// 组件是否已被 Unity 销毁。利用 UnityEngine.Object 重载的 == 判定（销毁后 C# 引用仍在，但视为 null）。
        /// </summary>
        private static bool IsDestroyed(GameFrameworkComponent component)
        {
            return component == null;
        }

        /// <summary>
        /// 注册游戏框架组件。重复注册返回 false。
        /// </summary>
        internal static bool RegisterComponent(GameFrameworkComponent gameFrameworkComponent)
        {
            // Component registration is main-thread by Unity contract (called from Awake). The unlocked
            // dictionary insert below is only safe under that assumption; assert it explicitly to match
            // the locked threading contract of Framework.GetModule.
            Framework.EnsureMainThread(nameof(RegisterComponent));

            if (gameFrameworkComponent == null)
            {
                Log.Error("Game Framework component is invalid.");
                return false;
            }

            Type type = gameFrameworkComponent.GetType();
            if (s_ByType.TryGetValue(type, out var existing))
            {
                // 已被 Unity 销毁的失效实例：允许新实例顶替（框架重启 / 场景重载后的重注册）。
                // 否则残留的死实例会让重注册永久失败、GameEntry.X 始终返回失效旧实例。
                if (!IsDestroyed(existing) && !ReferenceEquals(existing, gameFrameworkComponent))
                {
                    Log.Error("Game Framework component type '{0}' is already exist.", type.FullName);
                    return false;
                }

                s_ByType[type] = gameFrameworkComponent;
                return true;
            }

            s_ByType.Add(type, gameFrameworkComponent);
            return true;
        }

        /// <summary>
        /// 注销游戏框架组件。仅当注册项确实是传入实例时才移除（避免误删已替换的新实例）。
        /// 由 GameFrameworkComponent.OnDestroy 调用：DontDestroyOnLoad 关闭时场景卸载会销毁组件，
        /// 若不注销，s_ByType 会残留已销毁的 MonoBehaviour，导致重注册失败、GameEntry.X 永久返回失效旧实例。
        /// </summary>
        internal static void UnregisterComponent(GameFrameworkComponent gameFrameworkComponent)
        {
            // 与 RegisterComponent 对称：注销同样是主线程操作（由 OnDestroy 触发），断言以匹配锁定线程契约。
            Framework.EnsureMainThread(nameof(UnregisterComponent));

            if (gameFrameworkComponent == null)
            {
                return;
            }

            Type type = gameFrameworkComponent.GetType();
            // 仅移除自身：若该类型已被另一实例重新注册，绝不能误删替换者。
            if (s_ByType.TryGetValue(type, out var registered) && ReferenceEquals(registered, gameFrameworkComponent))
            {
                s_ByType.Remove(type);
            }
        }

        /// <summary>
        /// 清空注册表。供 BaseComponent 真退出（genuine quit）路径调用，使 Player 构建下的真正关闭
        /// 完整重置静态状态，对齐 Editor 进入 PlayMode 时 InitializeOnEnterPlayMode 的清空行为。
        /// </summary>
        internal static void Clear()
        {
            Framework.EnsureMainThread(nameof(Clear));
            s_ByType.Clear();
        }

#if UNITY_EDITOR
        // Domain Reload 关闭时进入 PlayMode，必须清空静态状态。
        [UnityEditor.InitializeOnEnterPlayMode]
        private static void OnEnterPlayMode()
        {
            s_ByType.Clear();
        }
#endif
    }
}
