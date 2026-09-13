//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Notification;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 通知组件。转发到 <see cref="INotificationManager"/>。
    ///
    /// 注意：框架未集成 Unity Mobile Notifications 包，因此默认无平台被注入，
    /// <see cref="IsSupported"/> 为 false，所有调用安全 no-op。集成移动通知包后，
    /// 业务侧实现 <see cref="INotificationPlatform"/> 并调用 <see cref="SetPlatform"/> 注入即可启用真实推送。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Notification")]
    public sealed class NotificationComponent : GameFrameworkComponent
    {
        [Tooltip("应用进入后台时自动调度一条「回来玩」本地通知。默认关闭。")]
        [SerializeField]
        private bool m_ScheduleComeBackOnPause = false;

        [Tooltip("「回来玩」通知的延迟秒数（进入后台后多久触发）。")]
        [SerializeField]
        private double m_ComeBackDelaySeconds = 86400.0;

        [Tooltip("「回来玩」通知标题。")]
        [SerializeField]
        private string m_ComeBackTitle = "Come back!";

        [Tooltip("「回来玩」通知正文。")]
        [SerializeField]
        private string m_ComeBackBody = "Your game is waiting for you.";

        private INotificationManager m_NotificationManager;
        // 后台调度的「回来玩」通知 id，回到前台时取消，避免在用户已回来时仍弹出。
        private int m_ComeBackId;

        protected override void Awake()
        {
            base.Awake();
            m_NotificationManager = Framework.GetModule<INotificationManager>();
            if (m_NotificationManager == null)
            {
                Log.Fatal("Notification manager is invalid.");
                return;
            }
        }

        /// <summary>
        /// 当前是否有支持通知的平台被注入并就绪。
        /// </summary>
        public bool IsSupported
        {
            get { return m_NotificationManager != null && m_NotificationManager.IsSupported; }
        }

        /// <summary>
        /// 设置平台辅助器（注入 Unity Mobile Notifications 桥接实现）。传 null 回退到内置空实现。
        /// </summary>
        public void SetPlatform(INotificationPlatform platform)
        {
            if (m_NotificationManager == null) { Log.Fatal("Notification manager is invalid."); return; }
            m_NotificationManager.SetPlatform(platform);
        }

        /// <summary>
        /// 请求通知授权（iOS 运行时授权）。
        /// </summary>
        public void RequestAuthorization(Action<bool> onResult = null)
        {
            if (m_NotificationManager == null) { Log.Fatal("Notification manager is invalid."); return; }
            m_NotificationManager.RequestAuthorization(onResult);
        }

        /// <summary>
        /// 调度一条本地通知，返回最终使用的通知 id（id 为 0 时自动分配）。
        /// </summary>
        public int ScheduleLocal(LocalNotification n)
        {
            if (m_NotificationManager == null) { Log.Fatal("Notification manager is invalid."); return 0; }
            return m_NotificationManager.ScheduleLocal(n);
        }

        /// <summary>
        /// 便捷：延迟 N 秒后触发一条本地通知，返回自动分配的 id。
        /// </summary>
        public int ScheduleAfter(string title, string body, double delaySeconds)
        {
            if (m_NotificationManager == null) { Log.Fatal("Notification manager is invalid."); return 0; }
            return m_NotificationManager.ScheduleAfter(title, body, delaySeconds);
        }

        /// <summary>
        /// 取消指定 id 的本地通知。
        /// </summary>
        public void CancelLocal(int id)
        {
            if (m_NotificationManager == null) { Log.Fatal("Notification manager is invalid."); return; }
            m_NotificationManager.CancelLocal(id);
        }

        /// <summary>
        /// 取消全部已调度的本地通知。
        /// </summary>
        public void CancelAllLocal()
        {
            if (m_NotificationManager == null) { Log.Fatal("Notification manager is invalid."); return; }
            m_NotificationManager.CancelAllLocal();
        }

        /// <summary>
        /// 注册远程推送，成功回传设备 push token，失败回传错误描述。
        /// </summary>
        public void RegisterForRemote(Action<string> onToken, Action<string> onError = null)
        {
            if (m_NotificationManager == null) { Log.Fatal("Notification manager is invalid."); return; }
            m_NotificationManager.RegisterForRemote(onToken, onError);
        }

        /// <summary>
        /// 清除应用角标。
        /// </summary>
        public void ClearBadge()
        {
            if (m_NotificationManager == null) { Log.Fatal("Notification manager is invalid."); return; }
            m_NotificationManager.ClearBadge();
        }

        // 进入后台时可选地调度「回来玩」通知；回到前台时取消并清角标。由序列化开关 m_ScheduleComeBackOnPause 控制（默认关）。
        private void OnApplicationPause(bool pauseStatus)
        {
            if (m_NotificationManager == null) return;
            if (!m_ScheduleComeBackOnPause) return;

            if (pauseStatus)
            {
                m_ComeBackId = m_NotificationManager.ScheduleAfter(m_ComeBackTitle, m_ComeBackBody, m_ComeBackDelaySeconds);
            }
            else
            {
                if (m_ComeBackId != 0)
                {
                    m_NotificationManager.CancelLocal(m_ComeBackId);
                    m_ComeBackId = 0;
                }
                m_NotificationManager.ClearBadge();
            }
        }
    }
}
