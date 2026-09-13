//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Notification
{
    /// <summary>
    /// 通知平台辅助器接口（注入点）。
    /// 框架核心引擎无关，不直接依赖 Unity Mobile Notifications 包；
    /// 真实实现由 Unity 层或业务层桥接 iOS/Android 原生通知 SDK（即 com.unity.mobile.notifications）。
    /// 未注入时 NotificationManager 使用内置空实现（<see cref="IsSupported"/>=false，全部 no-op）。
    /// </summary>
    public interface INotificationPlatform
    {
        /// <summary>
        /// 调度一条本地通知。
        /// </summary>
        void ScheduleLocal(LocalNotification n);

        /// <summary>
        /// 取消指定 id 的本地通知（已调度未触发的）。
        /// </summary>
        void CancelLocal(int id);

        /// <summary>
        /// 取消全部已调度的本地通知。
        /// </summary>
        void CancelAllLocal();

        /// <summary>
        /// 请求通知授权（iOS 需要运行时弹窗授权）。回调返回是否被允许。
        /// </summary>
        void RequestAuthorization(Action<bool> onResult);

        /// <summary>
        /// 注册远程推送，成功后通过 <paramref name="onToken"/> 回传设备 push token；失败回传 <paramref name="onError"/>。
        /// </summary>
        void RegisterForRemote(Action<string> onToken, Action<string> onError);

        /// <summary>
        /// 清除应用角标。
        /// </summary>
        void ClearBadge();

        /// <summary>
        /// 当前平台是否支持通知（真实实现一般在移动端返回 true，编辑器/PC 返回 false）。
        /// </summary>
        bool IsSupported { get; }
    }
}
