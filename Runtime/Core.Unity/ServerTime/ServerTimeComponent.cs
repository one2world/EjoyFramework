//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.ServerTime;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 服务器时间组件。转发到 <see cref="IServerTimeManager"/>。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Server Time")]
    public sealed class ServerTimeComponent : GameFrameworkComponent
    {
        private IServerTimeManager m_ServerTimeManager;

        protected override void Awake()
        {
            base.Awake();
            m_ServerTimeManager = Framework.GetModule<IServerTimeManager>();
            if (m_ServerTimeManager == null)
            {
                Log.Fatal("Server time manager is invalid.");
                return;
            }
        }

        /// <summary>
        /// 是否已与服务器同步过至少一次。
        /// </summary>
        public bool Synced
        {
            get { return m_ServerTimeManager.Synced; }
        }

        /// <summary>
        /// 获取相对本地系统时钟的偏移量（毫秒）。
        /// </summary>
        public long OffsetMillis
        {
            get { return m_ServerTimeManager.OffsetMillis; }
        }

        /// <summary>
        /// 获取校正后的当前 UTC Unix 毫秒。
        /// </summary>
        public long NowUnixMillis
        {
            get { return m_ServerTimeManager.NowUnixMillis; }
        }

        /// <summary>
        /// 获取校正后的当前 UTC 时间。
        /// </summary>
        public DateTime UtcNow
        {
            get { return m_ServerTimeManager.UtcNow; }
        }

        /// <summary>
        /// 与服务器同步时间。serverUnixMillis 为服务器侧的 UTC Unix 毫秒。
        /// </summary>
        public void Sync(long serverUnixMillis)
        {
            m_ServerTimeManager.Sync(serverUnixMillis);
        }
    }
}
