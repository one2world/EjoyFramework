//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 全局缓动驱动组件。持有一个 <see cref="TweenManager"/>，在 Update 中按 Time.deltaTime 推进。
    ///
    /// <see cref="Active"/> 在首次访问时惰性创建一个隐藏的、DontDestroyOnLoad 的宿主 GameObject。
    /// 仅在运行态（Application.isPlaying）创建宿主；编辑态下直接返回一个独立的 TweenManager，
    /// 以便编辑器脚本 / 测试可安全引用而不产生场景副作用。对域重载保持健壮（单例为空即重建）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TweenRunner : MonoBehaviour
    {
        private static TweenRunner s_Instance;
        private static TweenManager s_EditorManager;

        private readonly TweenManager m_Manager = new TweenManager();

        /// <summary>
        /// 本组件持有的缓动管理器实例。
        /// </summary>
        public TweenManager Manager
        {
            get { return m_Manager; }
        }

        /// <summary>
        /// 全局活跃的缓动管理器。运行态返回宿主组件的管理器（必要时创建宿主）；
        /// 编辑态返回一个进程内共享的管理器（无场景对象、无 Update 驱动，需手动 Tick）。
        /// </summary>
        public static TweenManager Active
        {
            get
            {
                if (!Application.isPlaying)
                {
                    if (s_EditorManager == null)
                    {
                        s_EditorManager = new TweenManager();
                    }
                    return s_EditorManager;
                }

                if (s_Instance == null)
                {
                    GameObject host = new GameObject("[TweenRunner]");
                    host.hideFlags = HideFlags.HideAndDontSave;
                    DontDestroyOnLoad(host);
                    s_Instance = host.AddComponent<TweenRunner>();
                }

                return s_Instance.m_Manager;
            }
        }

        private void Awake()
        {
            // 防止重复实例（域重载或重复添加时保留首个）。
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
        }

        private void Update()
        {
            m_Manager.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }
    }
}
