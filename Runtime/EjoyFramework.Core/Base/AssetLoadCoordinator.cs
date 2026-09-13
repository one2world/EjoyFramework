//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 资源加载协调器：抽取 Config / DataTable / Localization 三个数据模块共享的
    /// “LoadXxx → ResourceManager 异步加载 → Helper 解析 → 入库 → 触发成功/失败事件” 脚手架。
    ///
    /// 职责：
    ///  - 跟踪在飞加载（m_Loading），保证 <c>LoadHandle</c> 先记录进字典、再订阅 <c>Completed</c>：
    ///    当 handle 已 <c>IsDone</c>（loader 快速失败 / 缓存命中）时，<c>+=</c> 会同步回调完成处理，
    ///    此时它能从字典取到完整 entry（修复各模块此前各自实现、易出错的 B5 时序问题）。
    ///  - 幂等 / 可重入的完成派发：重复或同帧同步回调只处理一次。
    ///  - 统一用单调时钟 <see cref="Utility.Timestamp"/> 计算加载耗时，取代各模块基于 1970 epoch 的
    ///    低精度 float 计时（绝对量级巨大导致子秒精度损失）。
    ///
    /// 变化点全部由委托注入：每次加载的解析逻辑（parse），以及模块级的成功 / 失败 / 释放回调。
    /// 由各数据模块持有一个实例，复用其自身的强类型事件与 Helper。
    /// </summary>
    internal sealed class AssetLoadCoordinator
    {
        /// <summary>
        /// 解析已加载资产并入库；返回 false 表示解析失败。允许抛异常（由协调器捕获并按失败处理）。
        /// </summary>
        public delegate bool ParseHandler(string assetName, object asset, object userData);

        private readonly Dictionary<string, Entry> m_Loading = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Action<string, float, object> m_OnSuccess;   // assetName, durationSeconds, userData
        private readonly Action<string, string, object> m_OnFailure;  // assetName, error, userData
        private readonly Action<object> m_ReleaseAsset;               // asset
        private readonly string m_ParseThrewPrefix;                   // 解析抛异常时的错误前缀，如 "ConfigHelper.ReadData threw: "
        private readonly string m_ParseFalseMessage;                  // 解析返回 false 时的错误文案

        public AssetLoadCoordinator(
            Action<string, float, object> onSuccess,
            Action<string, string, object> onFailure,
            Action<object> releaseAsset,
            string parseThrewPrefix,
            string parseFalseMessage)
        {
            if (onSuccess == null) throw new FrameworkException("AssetLoadCoordinator onSuccess is invalid.");
            if (onFailure == null) throw new FrameworkException("AssetLoadCoordinator onFailure is invalid.");
            if (releaseAsset == null) throw new FrameworkException("AssetLoadCoordinator releaseAsset is invalid.");
            m_OnSuccess = onSuccess;
            m_OnFailure = onFailure;
            m_ReleaseAsset = releaseAsset;
            m_ParseThrewPrefix = parseThrewPrefix ?? string.Empty;
            m_ParseFalseMessage = parseFalseMessage ?? string.Empty;
        }

        /// <summary>当前在飞加载数量。</summary>
        public int LoadingCount { get { return m_Loading.Count; } }

        /// <summary>清空在飞加载跟踪（模块 Shutdown 时调用）。</summary>
        public void Clear()
        {
            m_Loading.Clear();
        }

        /// <summary>
        /// 开始跟踪一次加载。调用方负责先用 ResourceManager 取得 <paramref name="handle"/>，
        /// 本方法保证 LoadHandle 先记录进字典、再订阅 <c>Completed</c>（对同步完成安全）。
        /// </summary>
        public void BeginLoad(string assetName, IAssetLoadHandle handle, object userData, ParseHandler parse)
        {
            var entry = new Entry
            {
                UserData = userData,
                StartTime = Utility.Timestamp.SecondsF,
                Parse = parse,
                LoadHandle = handle
            };
            m_Loading[assetName] = entry;
            // 先把含 LoadHandle 的 entry 放进字典，再订阅 Completed：handle 已 IsDone 时 += 会同步回调
            // OnLoadCompleted，此时它能从字典取到完整 entry。
            handle.Completed += OnLoadCompleted;
        }

        private void OnLoadCompleted(IAssetLoadHandle handle)
        {
            string assetName = (string)handle.UserData;
            // 幂等 / 可重入保护：首次进入即从 m_Loading 摘除；重复回调（或同步同帧回调）找不到条目直接返回。
            if (!m_Loading.TryGetValue(assetName, out var entry)) return;
            m_Loading.Remove(assetName);

            if (handle.Status == LoadAssetStatus.Cancelled) return;
            if (handle.Status == LoadAssetStatus.Failed)
            {
                m_OnFailure(assetName, handle.FailureStatus + ": " + handle.ErrorMessage, entry.UserData);
                return;
            }

            object asset = handle.Asset;
            bool ok;
            try { ok = entry.Parse != null && entry.Parse(assetName, asset, entry.UserData); }
            catch (Exception ex)
            {
                m_OnFailure(assetName, m_ParseThrewPrefix + ex.Message, entry.UserData);
                m_ReleaseAsset(asset);
                return;
            }
            m_ReleaseAsset(asset);

            if (!ok)
            {
                m_OnFailure(assetName, m_ParseFalseMessage, entry.UserData);
                return;
            }

            float total = Utility.Timestamp.SecondsF - entry.StartTime;
            m_OnSuccess(assetName, total, entry.UserData);
        }

        private sealed class Entry
        {
            public object UserData;
            public float StartTime;
            public ParseHandler Parse;
            public IAssetLoadHandle LoadHandle;
        }
    }
}
