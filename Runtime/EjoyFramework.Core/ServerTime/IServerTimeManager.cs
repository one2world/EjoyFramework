//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.ServerTime
{
    /// <summary>
    /// 服务器时间管理器接口。
    /// 以服务器下发的 Unix 毫秒为锚点，叠加本地单调时钟流逝，得到不受本地系统时钟（玩家手动改时间）篡改影响的"校正时间"。
    /// </summary>
    public interface IServerTimeManager
    {
        /// <summary>
        /// 与服务器同步时间。serverUnixMillis 为服务器侧的 UTC Unix 毫秒。
        /// 同步时同时捕获本地单调时钟基准，之后 NowUnixMillis 在服务器锚点上叠加本地流逝量。
        /// </summary>
        void Sync(long serverUnixMillis);

        /// <summary>
        /// 获取校正后的当前 UTC Unix 毫秒（= 服务器锚点 + 本地单调流逝量）。未同步时回退为本地系统 UTC 毫秒。
        /// </summary>
        long NowUnixMillis { get; }

        /// <summary>
        /// 获取校正后的当前 UTC 时间。
        /// </summary>
        DateTime UtcNow { get; }

        /// <summary>
        /// 是否已与服务器同步过至少一次。
        /// </summary>
        bool Synced { get; }

        /// <summary>
        /// 获取相对本地系统时钟的偏移量（毫秒，= 服务器锚点 - 同步时刻的本地系统时钟）。
        /// 正值表示服务器时间领先于本地系统时钟。未同步时为 0。
        /// </summary>
        long OffsetMillis { get; }
    }
}
