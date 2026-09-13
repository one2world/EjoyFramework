//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.CloudSave
{
    /// <summary>
    /// 云存档后端抽象。真实后端（Google Play Games / iCloud / 自建服务器）实现此接口并被注入，
    /// 默认提供 <see cref="NullCloudSaveBackend"/> 空实现；冲突解决逻辑与具体后端解耦、可独立测试。
    /// 回调式 API：上传/下载完成后通过传入的 <see cref="Action{T}"/> 通知调用方。
    /// </summary>
    public interface ICloudSaveBackend
    {
        /// <summary>
        /// 上传一份存档。
        /// </summary>
        /// <param name="data">待上传的存档数据。</param>
        /// <param name="onComplete">完成回调，参数为是否成功。</param>
        void Upload(CloudSaveData data, Action<bool> onComplete);

        /// <summary>
        /// 下载指定键的存档。
        /// </summary>
        /// <param name="key">存档键。</param>
        /// <param name="onComplete">完成回调，参数为下载到的存档；不存在时回调 null。</param>
        void Download(string key, Action<CloudSaveData> onComplete);

        /// <summary>
        /// 后端当前是否可用（如已登录、网络可达）。不可用时上传应失败、下载应返回 null。
        /// </summary>
        bool IsAvailable { get; }
    }
}
