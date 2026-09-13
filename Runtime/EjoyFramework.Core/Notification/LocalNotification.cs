//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Notification
{
    /// <summary>
    /// 本地通知数据对象（纯数据，引擎无关）。
    /// 描述一条待调度的本地推送：标题/正文/触发时刻/角标等。由 <see cref="INotificationPlatform"/> 翻译为平台原生通知。
    /// 触发时刻以 UTC Unix 毫秒表示，保持 System-only，便于单测与跨平台。
    /// </summary>
    public sealed class LocalNotification
    {
        /// <summary>
        /// 通知 id。用于后续取消/去重。为 0 时由 NotificationManager 自动分配。
        /// </summary>
        public int Id;

        /// <summary>
        /// 通知标题。
        /// </summary>
        public string Title;

        /// <summary>
        /// 通知正文。
        /// </summary>
        public string Body;

        /// <summary>
        /// 通知通道（Android Channel / iOS category）。默认 "default"。
        /// </summary>
        public string Channel = "default";

        /// <summary>
        /// 触发时刻（UTC Unix 毫秒）。
        /// </summary>
        public long FireAtUnixMillis;

        /// <summary>
        /// 小图标资源名（Android 状态栏小图标）。可空。
        /// </summary>
        public string SmallIcon;

        /// <summary>
        /// 大图标资源名（Android 通知大图标）。可空。
        /// </summary>
        public string LargeIcon;

        /// <summary>
        /// 应用角标数字（iOS / 部分 Android 启动器）。0 表示不设置。
        /// </summary>
        public int BadgeNumber;

        /// <summary>
        /// 透传业务数据（点击通知唤起时回传）。可空。
        /// </summary>
        public Dictionary<string, string> Data;

        /// <summary>
        /// 构造一条"延迟 N 秒后触发"的本地通知。
        /// FireAt = nowUnixMillis + delaySeconds * 1000。
        /// 接受显式的 <paramref name="nowUnixMillis"/> 锚点以保持 System-only 与可测试；
        /// 业务侧通常传入 <c>IServerTimeManager.NowUnixMillis</c> 或 <c>DateTimeOffset.UtcNow</c>。
        /// </summary>
        /// <param name="id">通知 id；0 表示交由 NotificationManager 自动分配。</param>
        /// <param name="title">标题。</param>
        /// <param name="body">正文。</param>
        /// <param name="delaySeconds">延迟秒数（相对 <paramref name="nowUnixMillis"/>）。</param>
        /// <param name="nowUnixMillis">当前 UTC Unix 毫秒锚点。</param>
        public static LocalNotification After(int id, string title, string body, double delaySeconds, long nowUnixMillis)
        {
            return new LocalNotification
            {
                Id = id,
                Title = title,
                Body = body,
                FireAtUnixMillis = nowUnixMillis + (long)(delaySeconds * 1000.0),
            };
        }

        /// <summary>
        /// 构造一条"延迟 N 秒后触发"的本地通知，now 取本地系统 UTC 时钟。
        /// 便捷重载；对时钟篡改敏感的业务应使用接受 nowUnixMillis 的重载并传入校正时间。
        /// </summary>
        public static LocalNotification After(int id, string title, string body, double delaySeconds)
        {
            long nowUnixMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return After(id, title, body, delaySeconds, nowUnixMillis);
        }
    }
}
