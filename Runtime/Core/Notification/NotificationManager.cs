//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Notification
{
    /// <summary>
    /// 通知管理器实现（本地 + 远程推送统一出口）。
    ///
    /// 默认平台为内置 <see cref="NullNotificationPlatform"/>：IsSupported=false，所有调用记一条调试日志后 no-op，
    /// 因此"未注入平台"是天然安全的——调用不抛异常、不产生副作用（Release 构建中 FrameworkLog.Debug 被编译期裁剪，零开销）。
    /// 注入真实平台（桥接 Unity Mobile Notifications）后转发到平台；平台抛出的异常被捕获并记日志，绝不让通知路径成为崩溃源。
    ///
    /// id 自动分配：当 ScheduleLocal 收到 Id==0 时，用一个递增计数器（从 1 起）分配并回写到通知对象。
    ///
    /// 核心层引擎无关：本类仅使用 System.* ，不引用 UnityEngine。
    /// </summary>
    internal sealed class NotificationManager : FrameworkModule, INotificationManager
    {
        private INotificationPlatform m_Platform;
        private int m_NextId;

        public NotificationManager()
        {
            m_Platform = new NullNotificationPlatform();
            m_NextId = 0;
        }

        // Priority 0：基础服务模块，无依赖。
        public override int Priority { get { return 0; } }

        public bool IsSupported
        {
            get
            {
                INotificationPlatform platform = m_Platform;
                return platform != null && platform.IsSupported;
            }
        }

        public void SetPlatform(INotificationPlatform platform)
        {
            Framework.EnsureMainThread(nameof(SetPlatform));
            // 传 null 回退到内置空实现，保证后续调用始终安全（无需到处判空）。
            m_Platform = platform ?? new NullNotificationPlatform();
        }

        public void RequestAuthorization(Action<bool> onResult = null)
        {
            Framework.EnsureMainThread(nameof(RequestAuthorization));
            INotificationPlatform platform = m_Platform;
            try
            {
                platform.RequestAuthorization(onResult);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Notification platform RequestAuthorization threw: {0}", ex);
                SafeInvoke(onResult, false);
            }
        }

        public int ScheduleLocal(LocalNotification n)
        {
            Framework.EnsureMainThread(nameof(ScheduleLocal));
            if (n == null)
            {
                FrameworkLog.Warning("Notification.ScheduleLocal: notification is null.");
                return 0;
            }

            if (n.Id == 0)
            {
                n.Id = AllocateId();
            }

            INotificationPlatform platform = m_Platform;
            try
            {
                platform.ScheduleLocal(n);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Notification platform ScheduleLocal threw: {0}", ex);
            }

            return n.Id;
        }

        public int ScheduleAfter(string title, string body, double delaySeconds)
        {
            Framework.EnsureMainThread(nameof(ScheduleAfter));
            long nowUnixMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            LocalNotification n = LocalNotification.After(0, title, body, delaySeconds, nowUnixMillis);
            return ScheduleLocal(n);
        }

        public void CancelLocal(int id)
        {
            Framework.EnsureMainThread(nameof(CancelLocal));
            INotificationPlatform platform = m_Platform;
            try
            {
                platform.CancelLocal(id);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Notification platform CancelLocal threw: {0}", ex);
            }
        }

        public void CancelAllLocal()
        {
            Framework.EnsureMainThread(nameof(CancelAllLocal));
            INotificationPlatform platform = m_Platform;
            try
            {
                platform.CancelAllLocal();
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Notification platform CancelAllLocal threw: {0}", ex);
            }
        }

        public void RegisterForRemote(Action<string> onToken, Action<string> onError = null)
        {
            Framework.EnsureMainThread(nameof(RegisterForRemote));
            INotificationPlatform platform = m_Platform;
            try
            {
                platform.RegisterForRemote(onToken, onError);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Notification platform RegisterForRemote threw: {0}", ex);
                SafeInvoke(onError, ex.Message);
            }
        }

        public void ClearBadge()
        {
            Framework.EnsureMainThread(nameof(ClearBadge));
            INotificationPlatform platform = m_Platform;
            try
            {
                platform.ClearBadge();
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Notification platform ClearBadge threw: {0}", ex);
            }
        }

        public override void Update(float elapseSeconds, float realElapseSeconds) { }

        public override void Shutdown()
        {
            m_Platform = new NullNotificationPlatform();
            m_NextId = 0;
        }

        // 递增分配 id（从 1 起，0 保留为"未分配"哨兵）。仅主线程调用，无需加锁。
        private int AllocateId()
        {
            // 防御性回绕：到达 int.MaxValue 时回到 1，避免回到 0 哨兵。
            if (m_NextId == int.MaxValue)
            {
                m_NextId = 0;
            }
            return ++m_NextId;
        }

        private static void SafeInvoke(Action<bool> cb, bool value)
        {
            if (cb == null) return;
            try { cb(value); }
            catch (Exception ex) { FrameworkLog.Error("Notification callback threw: {0}", ex); }
        }

        private static void SafeInvoke(Action<string> cb, string value)
        {
            if (cb == null) return;
            try { cb(value); }
            catch (Exception ex) { FrameworkLog.Error("Notification callback threw: {0}", ex); }
        }

        /// <summary>
        /// 内置空平台：未注入真实平台时的兜底实现。
        /// IsSupported=false；调用记一条调试日志后 no-op，授权/远程注册回调以"失败/未授权"语义安全回传。
        /// </summary>
        private sealed class NullNotificationPlatform : INotificationPlatform
        {
            public bool IsSupported { get { return false; } }

            public void ScheduleLocal(LocalNotification n)
            {
                FrameworkLog.Debug("Notification (no platform): ScheduleLocal id={0} title='{1}'",
                    n != null ? n.Id : 0, n != null ? n.Title : null);
            }

            public void CancelLocal(int id)
            {
                FrameworkLog.Debug("Notification (no platform): CancelLocal id={0}", id);
            }

            public void CancelAllLocal()
            {
                FrameworkLog.Debug("Notification (no platform): CancelAllLocal");
            }

            public void RequestAuthorization(Action<bool> onResult)
            {
                FrameworkLog.Debug("Notification (no platform): RequestAuthorization -> false");
                SafeInvoke(onResult, false);
            }

            public void RegisterForRemote(Action<string> onToken, Action<string> onError)
            {
                FrameworkLog.Debug("Notification (no platform): RegisterForRemote -> error");
                SafeInvoke(onError, "No notification platform is set.");
            }

            public void ClearBadge()
            {
                FrameworkLog.Debug("Notification (no platform): ClearBadge");
            }
        }
    }
}
