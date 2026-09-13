//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// UI 根节点 helper（业务拖入 prefab 即用）。
    ///
    /// 职责：
    ///   - 持有 UICamera 引用（ScreenSpaceCamera 模式 Canvas 需要）
    ///   - 持有所有 group Canvas 引用，提供 GetGroup(name) / GetGroup(UIGroupKind) 查询
    ///   - Awake 自动确保场景内有唯一 EventSystem（自动 DDOL）
    ///   - 静态 Instance 单例 - 业务 / Framework 任意位置可访问
    ///
    /// 业务上手 3 步：
    ///   1. 在场景拖入 Packages/com.ejoy.framework/Runtime/UI/Prefabs/UIRoot.prefab
    ///   2. 通过 UIComponent.AddUIGroup 注册 group（或调 RegisterStandardGroups() 一键注册）
    ///   3. GameEntry.UI.OpenUIForm(formId) 自动落到对应 group
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/UI/Root")]
    public sealed class UIRootHelper : MonoBehaviour
    {
        public static UIRootHelper Instance { get; private set; }

        [SerializeField]
        [Tooltip("UI 相机引用（用于 ScreenSpaceCamera 模式 Canvas）。可为 null（Overlay 模式不需要）。")]
        private UICameraHelper m_UICamera;

        [SerializeField]
        [Tooltip("Awake 时若场景内没有 EventSystem，自动创建；建议保持开启。")]
        private bool m_AutoCreateEventSystem = true;

        [SerializeField]
        [Tooltip("EventSystem 跟随 Framework GameObject DontDestroyOnLoad，避免场景切换 UI 失去事件。")]
        private bool m_DontDestroyEventSystem = true;

        [SerializeField]
        [Tooltip("Awake 自动扫描子节点 UIGroupCanvasHelper 并建立 group 索引。")]
        private bool m_AutoIndexGroupsOnAwake = true;

        [SerializeField]
        [Tooltip("UIRoot 自身跟随 DontDestroyOnLoad（建议开启）。")]
        private bool m_DontDestroyOnLoad = true;

        private EventSystem m_EventSystem;
        private readonly Dictionary<string, UIGroupCanvasHelper> m_Groups
            = new Dictionary<string, UIGroupCanvasHelper>(System.StringComparer.Ordinal);

        /// <summary>场景内的 EventSystem 实例。</summary>
        public EventSystem EventSystem => m_EventSystem;

        /// <summary>UI 相机（ScreenSpaceCamera Canvas 使用）。</summary>
        public UICameraHelper UICamera => m_UICamera;

        /// <summary>当前已注册的 group 数量。</summary>
        public int GroupCount => m_Groups.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Log.Warning("UIRootHelper: duplicate instance on '{0}' destroyed.", gameObject.name);
                UnityEngine.Object.Destroy(gameObject);
                return;
            }
            Instance = this;

            if (m_DontDestroyOnLoad && transform.parent == null)
            {
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
            }

            EnsureEventSystem();
            if (m_AutoIndexGroupsOnAwake) IndexGroups();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            m_Groups.Clear();
        }

        /// <summary>按名字查 group canvas helper。</summary>
        public UIGroupCanvasHelper GetGroup(string groupName)
        {
            m_Groups.TryGetValue(groupName, out var g);
            return g;
        }

        /// <summary>按枚举查 group canvas helper（推荐）。</summary>
        public UIGroupCanvasHelper GetGroup(UIGroupKind kind)
        {
            return GetGroup(kind.ToGroupName());
        }

        /// <summary>显式重建 group 索引（业务运行时增删 group 后调用）。</summary>
        public void IndexGroups()
        {
            m_Groups.Clear();
            var helpers = GetComponentsInChildren<UIGroupCanvasHelper>(includeInactive: true);
            foreach (var h in helpers)
            {
                if (string.IsNullOrEmpty(h.GroupName))
                {
                    Log.Warning("UIRootHelper: UIGroupCanvasHelper on '{0}' has empty GroupName; skipping.", h.name);
                    continue;
                }
                if (m_Groups.ContainsKey(h.GroupName))
                {
                    Log.Warning("UIRootHelper: duplicate group '{0}'; using first found.", h.GroupName);
                    continue;
                }
                m_Groups[h.GroupName] = h;
            }
        }

        private void EnsureEventSystem()
        {
            m_EventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (m_EventSystem == null)
            {
                if (!m_AutoCreateEventSystem) return;
                var go = new GameObject("[EventSystem]");
                m_EventSystem = go.AddComponent<EventSystem>();
                go.AddComponent<StandaloneInputModule>();
                Log.Info("UIRootHelper: created EventSystem '{0}'.", go.name);
            }
            if (m_DontDestroyEventSystem && m_EventSystem != null && m_EventSystem.transform.parent == null)
            {
                UnityEngine.Object.DontDestroyOnLoad(m_EventSystem.gameObject);
            }
        }
    }
}
