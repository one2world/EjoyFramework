//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.CloudSave
{
    /// <summary>
    /// 云存档冲突解决策略。当本地与远端同时存在且内容相异时，决定保留哪一份。
    /// 具体语义见 <see cref="CloudSaveManager.ResolveConflict"/>。
    /// </summary>
    public enum ConflictResolution
    {
        /// <summary>
        /// 始终保留本地存档。
        /// </summary>
        PreferLocal,

        /// <summary>
        /// 始终保留远端存档。
        /// </summary>
        PreferRemote,

        /// <summary>
        /// 保留时间戳更新的一份；时间戳相同则保留本地。
        /// </summary>
        PreferNewerTimestamp,

        /// <summary>
        /// 保留版本号更高的一份；版本号相同则保留本地。
        /// </summary>
        PreferHigherVersion,

        /// <summary>
        /// 交由外部自定义解析器（local, remote） =&gt; winner 决定。
        /// 若未提供解析器，则回退为保留本地。
        /// </summary>
        Manual,
    }
}
