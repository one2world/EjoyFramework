//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.ServerTime
{
    /// <summary>
    /// 服务器时间管理器实现。
    ///
    /// 防漂移设计：
    ///   Sync 时记录服务器锚点 m_ServerBaseMillis，并用单调时钟（Utility.Timestamp，基于 Stopwatch）
    ///   记录同步那一刻的单调秒数 m_SyncMonotonicSeconds。读取 NowUnixMillis 时，用
    ///   (当前单调秒数 - m_SyncMonotonicSeconds) 计算自同步以来真正流逝的毫秒，再叠加到服务器锚点：
    ///   NowUnixMillis = m_ServerBaseMillis + 流逝毫秒。
    ///   关键点：流逝量用「单调时钟」而非系统墙钟测量，因此系统时钟被改表 / NTP 校时跳变时，返回值不会跟着跳。
    ///   m_LocalBaseMillis 仅用于在 Sync 时一次性确定 m_OffsetMillis（= m_ServerBaseMillis - m_LocalBaseMillis）。
    ///
    /// 核心层引擎无关：本类仅使用 System.* （DateTimeOffset），不引用 UnityEngine。
    /// </summary>
    internal sealed class ServerTimeManager : FrameworkModule, IServerTimeManager
    {
        private long m_ServerBaseMillis;
        private long m_LocalBaseMillis;
        private long m_OffsetMillis;
        private double m_SyncMonotonicSeconds;
        private bool m_Synced;

        public ServerTimeManager()
        {
            m_ServerBaseMillis = 0L;
            m_LocalBaseMillis = 0L;
            m_OffsetMillis = 0L;
            m_SyncMonotonicSeconds = 0.0;
            m_Synced = false;
        }

        // Priority 0：基础数据模块，无依赖。
        public override int Priority { get { return 0; } }

        public bool Synced { get { return m_Synced; } }

        public long OffsetMillis { get { return m_OffsetMillis; } }

        public long NowUnixMillis
        {
            get
            {
                long localNow = LocalNowUnixMillis();
                if (!m_Synced)
                {
                    // 未同步：回退为本地系统时钟。
                    return localNow;
                }
                // 用单调时钟测「自 Sync 以来流逝的毫秒」，使系统时钟跳变（NTP/改表）不影响返回值。
                double elapsedMs = (Utility.Timestamp.Seconds - m_SyncMonotonicSeconds) * 1000.0;
                return m_ServerBaseMillis + (long)elapsedMs;
            }
        }

        public DateTime UtcNow
        {
            get { return DateTimeOffset.FromUnixTimeMilliseconds(NowUnixMillis).UtcDateTime; }
        }

        public void Sync(long serverUnixMillis)
        {
            Framework.EnsureMainThread(nameof(Sync));
            m_ServerBaseMillis = serverUnixMillis;
            m_LocalBaseMillis = LocalNowUnixMillis();
            m_SyncMonotonicSeconds = Utility.Timestamp.Seconds;
            m_OffsetMillis = m_ServerBaseMillis - m_LocalBaseMillis;
            m_Synced = true;
            FrameworkLog.Debug("ServerTime synced: serverBase={0} offset={1}ms", serverUnixMillis, m_OffsetMillis);
        }

        public override void Update(float elapseSeconds, float realElapseSeconds) { }

        public override void Shutdown()
        {
            m_ServerBaseMillis = 0L;
            m_LocalBaseMillis = 0L;
            m_OffsetMillis = 0L;
            m_SyncMonotonicSeconds = 0.0;
            m_Synced = false;
        }

        // 本地系统 UTC Unix 毫秒。System.* only —— 引擎无关，无 UnityEngine 依赖。
        private static long LocalNowUnixMillis()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
