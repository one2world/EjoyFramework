//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Analytics
{
    /// <summary>
    /// 数据统计管理器实现。
    ///
    /// 默认无后端：Track / SetUserProperty 仅写调试日志（Release 构建中 FrameworkLog.Debug 被编译期裁剪，
    /// 因此默认形态接近 no-op，零运行时开销）。
    /// 注入后端后转发到后端；后端抛出的异常被捕获并记日志，绝不让埋点路径成为崩溃源。
    ///
    /// 核心层引擎无关：本类仅使用 System.* ，不引用 UnityEngine。
    /// </summary>
    internal sealed class AnalyticsManager : FrameworkModule, IAnalyticsManager
    {
        private IAnalyticsBackend m_Backend;

        public AnalyticsManager()
        {
            m_Backend = null;
        }

        // Priority 0：基础服务模块，无依赖。
        public override int Priority { get { return 0; } }

        public void Track(string eventName, IDictionary<string, object> properties = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                FrameworkLog.Warning("Analytics.Track: event name is invalid.");
                return;
            }

            IAnalyticsBackend backend = m_Backend;
            if (backend == null)
            {
                FrameworkLog.Debug("Analytics.Track (no backend): event='{0}' props={1}",
                    eventName, properties != null ? properties.Count : 0);
                return;
            }

            try { backend.OnTrack(eventName, properties); }
            catch (Exception ex) { FrameworkLog.Error("Analytics backend OnTrack threw: {0}", ex); }
        }

        public void SetUserProperty(string key, object value)
        {
            if (string.IsNullOrEmpty(key))
            {
                FrameworkLog.Warning("Analytics.SetUserProperty: key is invalid.");
                return;
            }

            IAnalyticsBackend backend = m_Backend;
            if (backend == null)
            {
                FrameworkLog.Debug("Analytics.SetUserProperty (no backend): key='{0}'", key);
                return;
            }

            try { backend.OnUserProperty(key, value); }
            catch (Exception ex) { FrameworkLog.Error("Analytics backend OnUserProperty threw: {0}", ex); }
        }

        public void SetBackend(IAnalyticsBackend backend)
        {
            Framework.EnsureMainThread(nameof(SetBackend));
            m_Backend = backend;
        }

        public override void Update(float elapseSeconds, float realElapseSeconds) { }

        public override void Shutdown()
        {
            m_Backend = null;
        }
    }
}
