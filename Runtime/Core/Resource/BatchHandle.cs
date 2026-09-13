//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 资源批次轮询句柄。
    ///
    /// 设计理由：
    ///   预载进度条通常在 Update 里每帧读一次进度。若直接持有 AssetBatch 引用，一旦批次被 Release 并回池复用，
    ///   持有者读到的就是别人的批次——这是这类池化对象最典型的悬垂 bug。本句柄是 readonly struct，
    ///   携带批次引用 + 租用时的版本号；批次回收时版本号递增，旧句柄立刻 IsValid == false，不会误读。
    ///   struct 且无装箱路径，每帧轮询零 GC。
    ///
    ///   失效句柄刻意报告 IsDone == true / Progress == 1f：轮询循环写成 while (!handle.IsDone) 时
    ///   不会因为批次被别处提前释放而死等。要区分"完成"与"失效"请显式查 IsValid。
    ///
    /// 线程契约：仅主线程（与 AssetBatch 一致）。
    /// </summary>
    public readonly struct BatchHandle : IEquatable<BatchHandle>
    {
        private readonly AssetBatch m_Batch;
        private readonly int m_Version;

        internal BatchHandle(AssetBatch batch, int version)
        {
            m_Batch = batch;
            m_Version = version;
        }

        /// <summary>无效句柄常量。</summary>
        public static BatchHandle Invalid { get { return default(BatchHandle); } }

        /// <summary>
        /// 句柄是否仍指向它当初绑定的那个批次（批次未被 Release 回池复用）。
        /// </summary>
        public bool IsValid { get { return m_Batch != null && m_Batch.Version == m_Version; } }

        /// <summary>
        /// 批次是否已结束。句柄失效时返回 true（见类型注释）。
        /// </summary>
        public bool IsDone { get { return !IsValid || m_Batch.IsDone; } }

        /// <summary>
        /// 聚合进度 0..1。句柄失效时返回 1f。
        /// </summary>
        public float Progress { get { return IsValid ? m_Batch.Progress : 1f; } }

        /// <summary>
        /// 批次当前状态。句柄失效时返回 Released。
        /// </summary>
        public AssetBatchState State { get { return IsValid ? m_Batch.State : AssetBatchState.Released; } }

        /// <summary>清单内资产总数；句柄失效时为 0。</summary>
        public int TotalCount { get { return IsValid ? m_Batch.TotalCount : 0; } }

        /// <summary>已落定的资产数；句柄失效时为 0。</summary>
        public int FinishedCount { get { return IsValid ? m_Batch.FinishedCount : 0; } }

        /// <summary>加载失败的资产数；句柄失效时为 0。</summary>
        public int FailedCount { get { return IsValid ? m_Batch.FailedCount : 0; } }

        /// <summary>
        /// 取回批次对象（用于注册 Completed / TryGetAsset / Release）。句柄失效时返回 null。
        /// </summary>
        public AssetBatch Batch { get { return IsValid ? m_Batch : null; } }

        /// <summary>取消批次。句柄失效时为 no-op。</summary>
        public void Cancel()
        {
            if (IsValid) m_Batch.Cancel();
        }

        /// <summary>释放批次。句柄失效时为 no-op（说明已经释放过了）。</summary>
        public void Release()
        {
            if (IsValid) m_Batch.Release();
        }

        public bool Equals(BatchHandle other)
        {
            return ReferenceEquals(m_Batch, other.m_Batch) && m_Version == other.m_Version;
        }

        public override bool Equals(object obj)
        {
            return obj is BatchHandle && Equals((BatchHandle)obj);
        }

        public override int GetHashCode()
        {
            int hash = m_Batch != null ? m_Batch.GetHashCode() : 0;
            return (hash * 397) ^ m_Version;
        }

        public static bool operator ==(BatchHandle left, BatchHandle right) { return left.Equals(right); }
        public static bool operator !=(BatchHandle left, BatchHandle right) { return !left.Equals(right); }
    }
}
