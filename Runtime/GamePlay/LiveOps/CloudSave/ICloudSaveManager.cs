//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.CloudSave
{
    /// <summary>
    /// 云存档管理器接口。封装一个 <see cref="ICloudSaveBackend"/>，提供同步（拉取 + 冲突解决 + 回写）流程。
    /// 通过 <see cref="Framework.GetModule{T}"/> 获取实现；使用前须先调用 <see cref="SetBackend"/> 注入后端。
    /// 与引擎无关，仅依赖 System.*。
    /// </summary>
    public interface ICloudSaveManager
    {
        /// <summary>
        /// 当前使用的后端；未注入时为默认的 <see cref="NullCloudSaveBackend"/>。
        /// </summary>
        ICloudSaveBackend Backend { get; }

        /// <summary>
        /// 默认冲突解决策略，<see cref="Sync"/> 使用此策略。
        /// </summary>
        ConflictResolution DefaultPolicy { get; set; }

        /// <summary>
        /// <see cref="ConflictResolution.Manual"/> 策略下使用的自定义解析器：(local, remote) =&gt; winner。
        /// 若为 Manual 策略却未设置此解析器，则回退为保留本地。
        /// </summary>
        Func<CloudSaveData, CloudSaveData, CloudSaveData> ManualResolver { get; set; }

        /// <summary>
        /// 当本地与远端同时存在且内容相异时触发：(local, remote)。在解析出胜者之前回调。
        /// </summary>
        event Action<CloudSaveData, CloudSaveData> OnConflict;

        /// <summary>
        /// 注入云存档后端实现。真实后端（Google Play Games / iCloud / 自建服务器）通过此方法注入；
        /// 传入 null 将回退为默认的 <see cref="NullCloudSaveBackend"/>，保证调用方始终可安全使用。
        /// </summary>
        /// <param name="backend">后端实现；为 null 时回退为 <see cref="NullCloudSaveBackend"/>。</param>
        void SetBackend(ICloudSaveBackend backend);

        /// <summary>
        /// 同步流程：下载本地键对应的远端存档，与本地解析冲突，必要时回写胜者，并回调胜者。
        /// </summary>
        /// <param name="local">本地存档，不可为空（其 Key 用于定位远端）。</param>
        /// <param name="onResolved">解析完成回调，参数为最终胜出的存档。</param>
        void Sync(CloudSaveData local, Action<CloudSaveData> onResolved);
    }
}
