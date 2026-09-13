//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 资源批次状态。
    /// </summary>
    public enum AssetBatchState : byte
    {
        /// <summary>收集清单中，可继续 Add。</summary>
        Collecting = 0,
        /// <summary>已 Start，等待各资产回调。</summary>
        Loading,
        /// <summary>全部资产已尘埃落定（成功或失败）。</summary>
        Done,
        /// <summary>调用方主动取消；后续到达的资产会被卸载。</summary>
        Cancelled,
        /// <summary>已 Release 并回收进 ReferencePool，实例不可再用。</summary>
        Released,
    }

    /// <summary>
    /// 资源批次：把"一批资产一起预载完再进入下一步"这件事变成一个可轮询、可取消、可整体释放的对象。
    ///
    /// 设计理由：
    ///   1) 关卡/界面进入前通常要预载几十份资产。逐个 LoadAsset 后自己数计数器是每个业务点都要重写一遍的样板代码，
    ///      而且极易漏掉"失败也要计数""取消后到达的资产要卸载"这两个坑。本类把这套聚合逻辑收敛到框架里。
    ///   2) 预载成功的资产会 Pin 进 AssetCache，之后业务层可用 IResourceManager.GetCachedAsset&lt;T&gt; 同步取用，
    ///      彻底避开热点路径上的异步往返。批次释放时逐个解 Pin，**每个 Pin 都对应一次 UnloadAsset**
    ///      （见 AssetCache 的引用计数契约）。
    ///   3) 不使用 Task/async/await：本框架的加载回调固定在主线程派发，引入 Task 只会带来状态机分配、
    ///      SynchronizationContext 依赖与异常吞噬问题。轮询请用 BatchHandle（struct，零 GC）。
    ///   4) 批次对象本身走 ReferencePool，轮询路径零分配；每次加载请求的 BatchRequest 则**刻意不池化**
    ///      （见该类型注释）。
    ///
    /// 生命周期：Collecting --Start--&gt; Loading --全部落定--&gt; Done --Release--&gt; Released（回池）。
    ///           任意未结束阶段可 Cancel 进入 Cancelled；Cancelled 同样需要 Release 回收。
    ///           Completed 在整个生命周期内只触发一次：Done、Cancel、或"尚未结束就 Release"三者中最先发生的那次。
    ///
    /// 线程契约：仅主线程。所有公开方法都假定与 loader 回调同线程，内部不加锁。
    /// </summary>
    public sealed class AssetBatch : IReference
    {
        /// <summary>
        /// 批次内单条资产的记录。池化以避免每次 Add 都产生垃圾。
        /// </summary>
        private sealed class Entry : IReference
        {
            public string AssetName;
            public Type AssetType;
            public float Progress;
            public bool Finished;
            public bool Succeeded;
            public bool Pinned;

            public void Clear()
            {
                AssetName = null;
                AssetType = null;
                Progress = 0f;
                Finished = false;
                Succeeded = false;
                Pinned = false;
            }
        }

        /// <summary>
        /// 传给 LoadAssetCallbacks 的 userData：把回调路由回"哪个批次的哪一条"。
        ///
        /// 刻意不走 ReferencePool：这个对象的生命周期由 loader 掌握，而 loader 完全可能在完成回调之后
        /// 再吐一个迟到的 progress 回调。一旦池化，那个迟到回调就会打到已被别人复用的同一实例上，
        /// 把进度写进毫不相干的批次——届时 Batch/Version/Index 三个字段全是"看起来合法"的新值，
        /// 任何下游校验都拦不住。每次 Start 每条资产多一次小对象分配（加载期行为，非每帧），
        /// 换绝不张冠李戴，这笔交易划算。Consumed 则用来防同一请求被结算两次。
        /// </summary>
        private sealed class BatchRequest
        {
            public AssetBatch Batch;
            public int Version;
            public int Index;
            public bool Consumed;
        }

        // 静态回调集：整个进程只有一份，任何批次的任何请求都复用它，杜绝 per-request 委托分配。
        private static readonly LoadAssetCallbacks s_Callbacks = new LoadAssetCallbacks(
            OnLoadSuccess, OnLoadFailure, OnLoadUpdate, null);

        private readonly List<Entry> m_Entries = new List<Entry>();

        // 注意：m_ResourceManager / m_Cache 在 Clear() 中刻意不置空。
        // 批次回收后仍可能收到 loader 的迟到回调，那份资产必须能被卸载掉，否则泄漏。
        //
        // 代价：若进程里存在多个 IResourceManager 实例（目前框架只装配一个，但测试里会各自 new），
        // 一个已回收的批次会继续持有它上一任 manager 的引用，直到被重新 Create 时覆盖。
        // 后果有二：迟到资产会被还给"当初发起加载的那个 manager"（这正是我们想要的），
        // 以及该 manager 的生命周期被池里这个对象多延长了一会儿。
        // 若将来 manager 会被频繁重建且批次池很大，应改为在 Clear 里换成弱引用或一个专用的 unloader 委托。
        private IResourceManager m_ResourceManager;
        private AssetCache m_Cache;

        private AssetBatchState m_State;
        private int m_Version;
        private int m_FinishedCount;
        private int m_FailedCount;
        private bool m_Starting;
        private bool m_Releasing;
        private Action<AssetBatch> m_Completed;

        /// <summary>
        /// 供 ReferencePool 使用的无参构造。业务方请用 IResourceManager.CreateAssetBatch()；
        /// 直接 new 出来的实例没有绑定资源管理器，Add/Start 会抛 FrameworkException。
        /// </summary>
        public AssetBatch()
        {
            m_State = AssetBatchState.Collecting;
        }

        /// <summary>
        /// 从 ReferencePool 租用一个批次并绑定资源管理器与缓存。
        /// </summary>
        internal static AssetBatch Create(IResourceManager resourceManager, AssetCache cache)
        {
            if (resourceManager == null) throw new FrameworkException("Resource manager is invalid.");
            if (cache == null) throw new FrameworkException("Asset cache is invalid.");

            AssetBatch batch = ReferencePool.Acquire<AssetBatch>();
            batch.m_ResourceManager = resourceManager;
            batch.m_Cache = cache;
            batch.m_State = AssetBatchState.Collecting;
            return batch;
        }

        /// <summary>当前状态。</summary>
        public AssetBatchState State { get { return m_State; } }

        /// <summary>清单内资产总数。</summary>
        public int TotalCount { get { return m_Entries.Count; } }

        /// <summary>已落定（成功或失败）的资产数。</summary>
        public int FinishedCount { get { return m_FinishedCount; } }

        /// <summary>加载失败的资产数。Done 之后可据此判断是否要走降级流程。</summary>
        public int FailedCount { get { return m_FailedCount; } }

        /// <summary>已结束（Done / Cancelled / Released）。</summary>
        public bool IsDone
        {
            get { return m_State == AssetBatchState.Done || m_State == AssetBatchState.Cancelled || m_State == AssetBatchState.Released; }
        }

        /// <summary>
        /// 聚合进度 0..1。空批次恒为 1；已落定的条目按 1 计。
        /// </summary>
        public float Progress
        {
            get
            {
                int count = m_Entries.Count;
                if (count <= 0) return 1f;
                if (IsDone) return 1f;

                float sum = 0f;
                for (int i = 0; i < count; i++)
                {
                    Entry e = m_Entries[i];
                    sum += e.Finished ? 1f : e.Progress;
                }
                return sum / count;
            }
        }

        /// <summary>
        /// 零 GC 轮询句柄。可安全长期持有：批次回收复用后句柄自动失效（IsValid == false）。
        /// </summary>
        public BatchHandle Handle { get { return new BatchHandle(this, m_Version); } }

        /// <summary>句柄版本号。批次每次回收进池时递增。</summary>
        internal int Version { get { return m_Version; } }

        /// <summary>
        /// 结束回调（Done / Cancelled / 未完成就 Release 都会触发一次，且仅一次）。
        /// 若注册时批次已结束，监听器会被同步立即调用一次。
        ///
        /// 监听器内允许调用 Release()（常见写法：预载完立刻用完就还）。此时 Start() 返回的句柄会正确地
        /// 报告 IsValid == false，因为句柄在任何用户回调触发之前就已取值。
        /// </summary>
        public event Action<AssetBatch> Completed
        {
            add
            {
                if (IsDone)
                {
                    try { if (value != null) value(this); }
                    catch (Exception ex) { FrameworkLog.Error("AssetBatch.Completed listener threw: {0}", ex); }
                }
                else
                {
                    m_Completed += value;
                }
            }
            remove { m_Completed -= value; }
        }

        /// <summary>
        /// 向清单追加一份资产。同名重复追加会被忽略。Start 之后调用会抛异常。
        /// </summary>
        /// <param name="assetName">资产名（业务 path）。</param>
        /// <param name="assetType">期望类型；null 表示不约束。</param>
        public AssetBatch Add(string assetName, Type assetType = null)
        {
            Framework.EnsureMainThread(nameof(Add));
            EnsureBound(nameof(Add));
            if (string.IsNullOrEmpty(assetName)) throw new FrameworkException("Asset name is invalid.");
            if (m_State != AssetBatchState.Collecting)
            {
                throw new FrameworkException(string.Format("Cannot add assets to a batch in state '{0}'; Add is only allowed before Start.", m_State));
            }

            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (string.Equals(m_Entries[i].AssetName, assetName, StringComparison.Ordinal)) return this;
            }

            Entry entry = ReferencePool.Acquire<Entry>();
            entry.AssetName = assetName;
            entry.AssetType = assetType;
            m_Entries.Add(entry);
            return this;
        }

        /// <summary>
        /// 发起整批加载。空批次立即进入 Done 并触发 Completed。
        /// </summary>
        /// <param name="priority">透传给 loader 的优先级。</param>
        public BatchHandle Start(int priority = 0)
        {
            Framework.EnsureMainThread(nameof(Start));
            EnsureBound(nameof(Start));
            if (m_State != AssetBatchState.Collecting)
            {
                throw new FrameworkException(string.Format("Cannot start a batch in state '{0}'.", m_State));
            }

            m_State = AssetBatchState.Loading;

            // 句柄必须在触发任何用户回调之前取值：监听器完全可能在 Completed 里就把批次 Release 掉，
            // 那样 m_Version 已经递增，此后再求值 Handle 会得到一个"看起来有效、实则指向已回池实例"的句柄。
            BatchHandle handle = Handle;

            if (m_Entries.Count <= 0)
            {
                m_State = AssetBatchState.Done;
                FireCompleted();
                return handle;
            }

            // m_Starting 期间抑制完成判定：loader 可能同步回调失败（例如未初始化），
            // 否则计数会在循环中途满足完成条件，把尚未发起的条目落下。
            m_Starting = true;
            try
            {
                for (int i = 0; i < m_Entries.Count; i++)
                {
                    Entry entry = m_Entries[i];
                    BatchRequest request = new BatchRequest
                    {
                        Batch = this,
                        Version = m_Version,
                        Index = i,
                    };

                    try
                    {
                        m_ResourceManager.LoadAsset(entry.AssetName, entry.AssetType, priority, s_Callbacks, request);
                    }
                    catch (Exception ex)
                    {
                        FrameworkLog.Error("AssetBatch: LoadAsset('{0}') threw: {1}", entry.AssetName, ex);

                        // 抛出之前 loader 可能已经同步回调过结果了，那条已经结算过。
                        // 只有确实没结算的才在这里补记，否则同一条资产会被数两遍。
                        //
                        // 已知取舍：置 Consumed 后，若这个抛了异常的 loader 事后又异步把资产送达，
                        // 该回调会被静默丢弃且资产不会被卸载（泄漏一份）。这里选择容忍——
                        // LoadAssetAsync 的契约是"失败必须显式回调，绝不抛出穿透 Manager"，
                        // 会走到这个 catch 的 loader 已经违约，再为它维护一条"抛异常后仍可能送达"的
                        // 回收路径，等于把错误契约当正常路径供养。真出现时日志里有上面那条 Error 可查。
                        if (!entry.Finished)
                        {
                            request.Consumed = true;
                            MarkFinished(entry, false);
                        }
                    }
                }
            }
            finally
            {
                m_Starting = false;
            }

            TryFinish();
            return handle;
        }

        /// <summary>
        /// 同步取本批次预载成功的资产。仅当该资产属于本批次、本批次加载成功、且 Pin 仍然持有时返回 true。
        /// 想取别的批次预载的资产请走 IResourceManager.TryGetCachedAsset。
        /// </summary>
        public bool TryGetAsset<T>(string assetName, out T asset) where T : class
        {
            asset = null;
            if (m_Cache == null || m_State == AssetBatchState.Released) return false;
            if (string.IsNullOrEmpty(assetName)) return false;

            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry entry = m_Entries[i];
                if (!string.Equals(entry.AssetName, assetName, StringComparison.Ordinal)) continue;
                if (!entry.Succeeded || !entry.Pinned) return false;
                return m_Cache.TryGet(assetName, out asset);
            }
            return false;
        }

        /// <summary>
        /// 主动取消。仅在 Collecting / Loading 时生效；已结束时为 no-op。
        ///
        /// 语义说明：本框架的 IResourceLoader 没有撤销在途请求的能力，所以 Cancel **不会**真的让 loader 停下来。
        /// 它做的是三件事：立刻把批次判定为结束（触发一次 Completed）、把已到达并 Pin 住的资产解 Pin 卸载、
        /// 保证此后到达的资产被直接 UnloadAsset 而不进缓存。IO 该发生的还是会发生，只是结果不再被采纳。
        /// 取消后仍需调用 Release 把批次归还引用池。
        /// </summary>
        public void Cancel()
        {
            Framework.EnsureMainThread(nameof(Cancel));
            if (IsDone) return;

            m_State = AssetBatchState.Cancelled;
            UnpinAll();
            FireCompleted();
        }

        /// <summary>
        /// 释放批次：解 Pin 所有成功资产（每个 Pin 对应一次 IResourceManager.UnloadAsset），并把自身归还 ReferencePool。
        /// 调用后本实例不可再使用，已发出的 BatchHandle 自动失效。
        ///
        /// 若批次尚未结束就被 Release，会先按"取消"语义触发一次 Completed，避免监听器永远等不到通知。
        /// 该回调里再次调用 Release() 是安全的（m_Releasing 闸门挡住重入），不会把实例重复压进引用池。
        /// </summary>
        public void Release()
        {
            Framework.EnsureMainThread(nameof(Release));
            if (m_State == AssetBatchState.Released || m_Releasing) return;

            // 重入闸：下面的 FireCompleted 会把控制权交给业务监听器，而"在 Completed 里顺手 Release"
            // 是完全合理的写法。没有这道闸的话，重入的那次会看到 IsDone（状态已是 Cancelled）而直接走到
            // 池释放，外层返回后再释放一次——同一实例被压进 ReferencePool 两次，之后会被两个调用方各租一份。
            // ReferencePool.EnableStrictCheck 默认关闭，这种双租不会报错，只会在远处诡异地炸掉。
            m_Releasing = true;

            // 未结束就释放等价于取消：先落状态再触发回调，监听器看到的 State 是 Cancelled。
            if (!IsDone)
            {
                m_State = AssetBatchState.Cancelled;
                FireCompleted();
            }

            UnpinAll();
            m_State = AssetBatchState.Released;
            m_Completed = null;
            ReferencePool.Release(this);
        }

        /// <summary>
        /// IReference 实现：由 ReferencePool 在回收时调用。版本号在此递增，使旧 BatchHandle 失效。
        /// 状态保持 Released——实例此刻躺在引用池里，任何残留引用对它的操作都必须是 no-op；
        /// 重新租用时由 Create() 置回 Collecting。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < m_Entries.Count; i++)
            {
                ReferencePool.Release(m_Entries[i]);
            }
            m_Entries.Clear();

            m_State = AssetBatchState.Released;
            m_FinishedCount = 0;
            m_FailedCount = 0;
            m_Starting = false;
            m_Releasing = false;
            m_Completed = null;
            m_Version++;
        }

        // ===== 静态回调入口（零闭包） =====

        private static void OnLoadSuccess(string assetName, object asset, float duration, object userData)
        {
            BatchRequest request = userData as BatchRequest;
            if (request == null || request.Consumed) return;
            request.Consumed = true;
            if (request.Batch != null) request.Batch.HandleSuccess(request.Version, request.Index, assetName, asset);
        }

        private static void OnLoadFailure(string assetName, LoadResourceStatus status, string errorMessage, object userData)
        {
            BatchRequest request = userData as BatchRequest;
            if (request == null || request.Consumed) return;
            request.Consumed = true;
            if (request.Batch != null) request.Batch.HandleFailure(request.Version, request.Index, assetName, status, errorMessage);
        }

        private static void OnLoadUpdate(string assetName, float progress, object userData)
        {
            BatchRequest request = userData as BatchRequest;
            if (request == null || request.Consumed || request.Batch == null) return;
            request.Batch.HandleProgress(request.Version, request.Index, progress);
        }

        // ===== 实例侧状态推进 =====

        private void HandleSuccess(int version, int index, string assetName, object asset)
        {
            // 版本不匹配 = 批次已回收复用，这是上一轮的迟到回调：资产无人认领，直接卸载。
            if (version != m_Version || m_State == AssetBatchState.Cancelled || m_State == AssetBatchState.Released)
            {
                UnloadOrphan(asset);
                return;
            }
            if (index < 0 || index >= m_Entries.Count)
            {
                UnloadOrphan(asset);
                return;
            }

            Entry entry = m_Entries[index];
            if (entry.Finished)
            {
                UnloadOrphan(asset);
                return;
            }

            if (asset != null)
            {
                object redundant;
                bool pinned = m_Cache.Pin(entry.AssetName, asset, out redundant);
                entry.Pinned = pinned;
                if (redundant != null) UnloadOrphan(redundant);

                // 没 Pin 住（缓存已封存 / 同名异实例）时资产已被退还卸载，本条按失败计，
                // 免得业务以为还能从缓存里同步取到它。
                MarkFinished(entry, pinned);
            }
            else
            {
                FrameworkLog.Warning("AssetBatch: loader reported success with a null asset for '{0}'.", assetName);
                MarkFinished(entry, false);
            }

            TryFinish();
        }

        private void HandleFailure(int version, int index, string assetName, LoadResourceStatus status, string errorMessage)
        {
            if (version != m_Version || m_State == AssetBatchState.Cancelled || m_State == AssetBatchState.Released) return;
            if (index < 0 || index >= m_Entries.Count) return;

            Entry entry = m_Entries[index];
            if (entry.Finished) return;

            FrameworkLog.Warning("AssetBatch: failed to load '{0}' ({1}): {2}", assetName, status, errorMessage);
            MarkFinished(entry, false);
            TryFinish();
        }

        private void HandleProgress(int version, int index, float progress)
        {
            if (version != m_Version || m_State != AssetBatchState.Loading) return;
            if (index < 0 || index >= m_Entries.Count) return;

            Entry entry = m_Entries[index];
            if (entry.Finished) return;

            if (progress < 0f) progress = 0f;
            else if (progress > 1f) progress = 1f;
            entry.Progress = progress;
        }

        private void MarkFinished(Entry entry, bool succeeded)
        {
            entry.Finished = true;
            entry.Succeeded = succeeded;
            entry.Progress = 1f;
            m_FinishedCount++;
            if (!succeeded) m_FailedCount++;
        }

        private void TryFinish()
        {
            if (m_Starting) return;
            if (m_State != AssetBatchState.Loading) return;
            if (m_FinishedCount < m_Entries.Count) return;

            m_State = AssetBatchState.Done;
            FireCompleted();
        }

        private void FireCompleted()
        {
            Action<AssetBatch> completed = m_Completed;
            m_Completed = null; // 一次性
            if (completed == null) return;
            try { completed(this); }
            catch (Exception ex) { FrameworkLog.Error("AssetBatch.Completed threw: {0}", ex); }
        }

        private void UnpinAll()
        {
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry entry = m_Entries[i];
                if (!entry.Pinned) continue;
                entry.Pinned = false;

                // 每个 Pin 都对应 loader 侧的一次引用，所以只要解 Pin 成功就必须卸载一次，
                // 不能只在 PinCount 归零时卸——那样 loader 的引用计数会卡住，bundle 永不释放。
                object released;
                if (m_Cache.Unpin(entry.AssetName, out released))
                {
                    UnloadOrphan(released);
                }
            }
        }

        private void UnloadOrphan(object asset)
        {
            if (asset == null || m_ResourceManager == null) return;
            try { m_ResourceManager.UnloadAsset(asset); }
            catch (Exception ex) { FrameworkLog.Warning("AssetBatch: UnloadAsset threw: {0}", ex); }
        }

        private void EnsureBound(string apiName)
        {
            if (m_ResourceManager == null || m_Cache == null)
            {
                throw new FrameworkException(string.Format(
                    "AssetBatch.{0} called on an unbound batch. Do not construct AssetBatch directly; use IResourceManager.CreateAssetBatch().",
                    apiName));
            }
        }
    }
}
