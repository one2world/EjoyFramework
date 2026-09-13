//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.CloudSave
{
    /// <summary>
    /// 云存档后端的空对象（Null Object）实现，作为未注入真实后端时的安全默认值。
    /// <see cref="IsAvailable"/> 恒为 false；上传恒失败（回调 false）；下载恒返回 null。
    /// </summary>
    public sealed class NullCloudSaveBackend : ICloudSaveBackend
    {
        /// <summary>
        /// 空后端不可用，恒为 false。
        /// </summary>
        public bool IsAvailable
        {
            get { return false; }
        }

        /// <summary>
        /// 空实现：不做任何事，立即以失败（false）回调。
        /// </summary>
        /// <param name="data">被忽略的存档数据。</param>
        /// <param name="onComplete">完成回调，将收到 false。</param>
        public void Upload(CloudSaveData data, Action<bool> onComplete)
        {
            onComplete?.Invoke(false);
        }

        /// <summary>
        /// 空实现：不做任何事，立即以 null 回调，表示不存在。
        /// </summary>
        /// <param name="key">被忽略的存档键。</param>
        /// <param name="onComplete">完成回调，将收到 null。</param>
        public void Download(string key, Action<CloudSaveData> onComplete)
        {
            onComplete?.Invoke(null);
        }
    }
}
