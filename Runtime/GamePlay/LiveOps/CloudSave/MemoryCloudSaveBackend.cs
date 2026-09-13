//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.CloudSave
{
    /// <summary>
    /// 内存版云存档后端，便于离线运行与单元测试。
    /// 所有回调同步立即触发，使测试结果确定且无需等待。
    /// 当 <see cref="IsAvailable"/> 为 false 时，行为退化为与 <see cref="NullCloudSaveBackend"/> 一致
    /// （上传失败、下载返回 null），用于模拟后端不可用。
    /// </summary>
    public sealed class MemoryCloudSaveBackend : ICloudSaveBackend
    {
        private readonly Dictionary<string, CloudSaveData> m_Store =
            new Dictionary<string, CloudSaveData>(StringComparer.Ordinal);

        /// <summary>
        /// 构造一个默认可用的内存后端。
        /// </summary>
        public MemoryCloudSaveBackend()
        {
            IsAvailable = true;
        }

        /// <summary>
        /// 后端是否可用。可在测试中置为 false 以模拟离线/未登录场景。
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// 预置一份存档（不触发任何回调），用于初始化远端状态。
        /// </summary>
        /// <param name="data">要写入存储的存档，其 <see cref="CloudSaveData.Key"/> 不可为空。</param>
        public void Seed(CloudSaveData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Key == null)
            {
                throw new ArgumentException("CloudSaveData.Key 不能为空。", nameof(data));
            }

            m_Store[data.Key] = data;
        }

        /// <summary>
        /// 上传一份存档；可用时写入内存并以 true 回调，不可用时以 false 回调。
        /// </summary>
        /// <param name="data">待上传的存档。</param>
        /// <param name="onComplete">完成回调，参数为是否成功。</param>
        public void Upload(CloudSaveData data, Action<bool> onComplete)
        {
            if (!IsAvailable || data == null || data.Key == null)
            {
                onComplete?.Invoke(false);
                return;
            }

            m_Store[data.Key] = data;
            onComplete?.Invoke(true);
        }

        /// <summary>
        /// 下载指定键的存档；命中返回对应数据，未命中或后端不可用返回 null。
        /// </summary>
        /// <param name="key">存档键。</param>
        /// <param name="onComplete">完成回调，参数为存档或 null。</param>
        public void Download(string key, Action<CloudSaveData> onComplete)
        {
            if (!IsAvailable || key == null)
            {
                onComplete?.Invoke(null);
                return;
            }

            CloudSaveData found;
            m_Store.TryGetValue(key, out found);
            onComplete?.Invoke(found);
        }
    }
}
