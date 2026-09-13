//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Notification
{
    /// <summary>
    /// 通知管理器接口（本地 + 远程推送统一出口）。
    /// 框架仅提供统一的调度/取消/授权出口，具体平台行为由注入的 <see cref="INotificationPlatform"/> 实现
    /// （真实形态桥接 Unity Mobile Notifications，iOS/Android）。未注入平台时全部 no-op，
    /// <see cref="IsSupported"/> 为 false，调用安全不抛异常。
    /// </summary>
    public interface INotificationManager
    {
        /// <summary>
        /// 设置平台辅助器。传 null 表示清除（回到内置空实现）。
        /// </summary>
        void SetPlatform(INotificationPlatform platform);

        /// <summary>
        /// 请求通知授权（iOS 运行时授权）。<paramref name="onResult"/> 回传是否被允许。
        /// 未设置平台时回调 false。
        /// </summary>
        void RequestAuthorization(Action<bool> onResult = null);

        /// <summary>
        /// 调度一条本地通知，返回最终使用的通知 id。
        /// 若 <see cref="LocalNotification.Id"/> 为 0，则自动分配一个递增 id 并回写。
        /// </summary>
        int ScheduleLocal(LocalNotification n);

        /// <summary>
        /// 便捷：延迟 <paramref name="delaySeconds"/> 秒后触发一条本地通知，自动分配 id，返回该 id。
        /// </summary>
        int ScheduleAfter(string title, string body, double delaySeconds);

        /// <summary>
        /// 取消指定 id 的本地通知。
        /// </summary>
        void CancelLocal(int id);

        /// <summary>
        /// 取消全部已调度的本地通知。
        /// </summary>
        void CancelAllLocal();

        /// <summary>
        /// 注册远程推送，成功回传设备 push token，失败回传错误描述。
        /// 未设置平台时回调 <paramref name="onError"/>。
        /// </summary>
        void RegisterForRemote(Action<string> onToken, Action<string> onError = null);

        /// <summary>
        /// 清除应用角标。
        /// </summary>
        void ClearBadge();

        /// <summary>
        /// 当前是否有支持通知的平台被注入并就绪。
        /// </summary>
        bool IsSupported { get; }
    }
}
