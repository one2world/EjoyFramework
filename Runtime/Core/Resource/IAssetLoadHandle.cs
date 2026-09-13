//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 资源加载句柄状态。
    /// </summary>
    public enum LoadAssetStatus : byte
    {
        /// <summary>已分配但未开始（loader 调度前）。</summary>
        Pending = 0,
        /// <summary>已发起 loader 加载，等待回调。</summary>
        Loading,
        /// <summary>加载成功；Asset 可用。</summary>
        Done,
        /// <summary>加载失败；ErrorMessage 可用。</summary>
        Failed,
        /// <summary>调用方主动取消；后续 loader 回调会被丢弃，资产（若已到达）会被 UnloadAsset。</summary>
        Cancelled,
    }

    /// <summary>
    /// 资源加载句柄。LoadAssetWithHandle 返回；调用方可读取进度、监听完成、主动取消。
    /// 一次加载一个 handle，状态不可逆地从 Pending → (Loading) → Done/Failed/Cancelled。
    /// </summary>
    public interface IAssetLoadHandle
    {
        /// <summary>句柄唯一 id（Debugger 用）。</summary>
        int Id { get; }

        /// <summary>资产名（业务 path）。</summary>
        string AssetName { get; }

        /// <summary>资产类型；可为 null 表示不约束。</summary>
        Type AssetType { get; }

        /// <summary>优先级（loader 内部排队用）。</summary>
        int Priority { get; }

        /// <summary>调用方携带的 userData。</summary>
        object UserData { get; }

        /// <summary>当前状态。</summary>
        LoadAssetStatus Status { get; }

        /// <summary>进度 0..1。Done 后为 1。</summary>
        float Progress { get; }

        /// <summary>Done 状态下的资产；其他状态为 null。</summary>
        object Asset { get; }

        /// <summary>Failed 状态下的细化错误码；其他状态为 Success。</summary>
        LoadResourceStatus FailureStatus { get; }

        /// <summary>Failed/Cancelled 状态下的错误消息；其他为 null。</summary>
        string ErrorMessage { get; }

        /// <summary>已结束（Done / Failed / Cancelled）。</summary>
        bool IsDone { get; }

        /// <summary>加载耗时（秒）。Pending/Loading 阶段返回当前 elapsed。</summary>
        float Duration { get; }

        /// <summary>
        /// 主动取消。仅在 Pending/Loading 时生效；Done/Failed/Cancelled 状态调用为 no-op。
        /// 取消后：若 loader 仍最终回调成功，到达的资产会被 ResourceManager.UnloadAsset 释放。
        /// </summary>
        void Cancel();

        /// <summary>
        /// 结束回调（Done / Failed / Cancelled 都会触发一次）。
        /// 若注册时 handle 已结束，监听器会被同步同帧调用一次。
        /// </summary>
        event Action<IAssetLoadHandle> Completed;
    }
}
