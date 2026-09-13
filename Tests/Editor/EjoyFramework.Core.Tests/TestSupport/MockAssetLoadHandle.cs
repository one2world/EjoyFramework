//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Tests.TestSupport
{
    /// <summary>
    /// 测试用 IAssetLoadHandle 实现：综合 SyncHandle / FakeHandle / TrioHandle 三种历史变体。
    ///
    /// 模式：
    ///   - 默认构造为 Loading 状态；调用 FireSuccess / FireFailure / Cancel 进入终态并通知 Completed。
    ///   - Completed 订阅时若已 done，立即同步触发，避免漏调。
    ///   - 多次终态调用幂等（IsDone 后 Fire* 是 no-op）。
    /// </summary>
    public sealed class MockAssetLoadHandle : IAssetLoadHandle
    {
        public int Id { get; set; }
        public string AssetName { get; set; }
        public Type AssetType { get; set; }
        public int Priority { get; set; }
        public object UserData { get; set; }
        public LoadAssetStatus Status { get; private set; } = LoadAssetStatus.Loading;
        public float Progress { get; set; }
        public object Asset { get; private set; }
        public LoadResourceStatus FailureStatus { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool IsDone => Status == LoadAssetStatus.Done || Status == LoadAssetStatus.Failed || Status == LoadAssetStatus.Cancelled;
        public float Duration => 0f;

        private Action<IAssetLoadHandle> m_Completed;

        public event Action<IAssetLoadHandle> Completed
        {
            add { if (IsDone) value?.Invoke(this); else m_Completed += value; }
            remove { m_Completed -= value; }
        }

        public void Cancel()
        {
            if (IsDone) return;
            Status = LoadAssetStatus.Cancelled;
            Fire();
        }

        public void FireSuccess(object asset)
        {
            if (IsDone) return;
            Status = LoadAssetStatus.Done;
            Asset = asset;
            Progress = 1f;
            Fire();
        }

        public void FireFailure(LoadResourceStatus status, string errorMessage)
        {
            if (IsDone) return;
            Status = LoadAssetStatus.Failed;
            FailureStatus = status;
            ErrorMessage = errorMessage;
            Fire();
        }

        private void Fire()
        {
            var handlers = m_Completed;
            m_Completed = null;
            handlers?.Invoke(this);
        }
    }
}
