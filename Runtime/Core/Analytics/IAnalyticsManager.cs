//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.Core.Analytics
{
    /// <summary>
    /// 数据统计管理器接口。
    /// 框架仅提供统一的埋点出口与用户属性设置，具体上报由业务注入的 <see cref="IAnalyticsBackend"/> 后端实现
    /// （如 Firebase、Adjust、ThinkingData 等）。未设置后端时埋点仅写调试日志，不产生副作用。
    /// </summary>
    public interface IAnalyticsManager
    {
        /// <summary>
        /// 上报一条事件。
        /// </summary>
        /// <param name="eventName">事件名。</param>
        /// <param name="properties">事件属性（可空）。</param>
        void Track(string eventName, IDictionary<string, object> properties = null);

        /// <summary>
        /// 设置用户属性（用户画像维度），后续事件随该属性聚合。
        /// </summary>
        void SetUserProperty(string key, object value);

        /// <summary>
        /// 设置上报后端。传 null 表示清除（回到仅日志模式）。
        /// </summary>
        void SetBackend(IAnalyticsBackend backend);
    }

    /// <summary>
    /// 数据统计上报后端接口。Unity 层或业务层实现，桥接具体 SDK。
    /// </summary>
    public interface IAnalyticsBackend
    {
        /// <summary>
        /// 处理一条事件上报。
        /// </summary>
        void OnTrack(string e, IDictionary<string, object> p);

        /// <summary>
        /// 处理一条用户属性设置。
        /// </summary>
        void OnUserProperty(string k, object v);
    }
}
