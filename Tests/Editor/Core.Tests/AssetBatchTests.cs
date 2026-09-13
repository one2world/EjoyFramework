//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 验证 AssetBatch / AssetCache / BatchHandle：
    /// 全成功、部分失败、取消、进度聚合、Release 解 Pin 触发 Unload、句柄版本号失效、同步 Get 命中与未命中。
    /// </summary>
    public class AssetBatchTests
    {
        /// <summary>
        /// 可按 assetName 分别放行的 loader。AssetLoadHandleTests 里的 ManualLoader 只能挂起一条请求，
        /// 批次测试需要同时挂起多条，故单独实现一份。
        /// </summary>
        private sealed class MultiLoader : IResourceLoader
        {
            private sealed class Pending
            {
                public string Name;
                public LoadAssetCallbacks Callbacks;
                public object UserData;
            }

            private readonly List<Pending> m_Pending = new List<Pending>();

            public bool IsInitialized { get; private set; } = true;
            public bool RequiresManifest => false;
            public int LoadedBundleCount => 0;
            public int LoadedAssetCount => 0;
            public int LoadingTaskCount => m_Pending.Count;

            public int UnloadCalls;
            public readonly List<object> UnloadedAssets = new List<object>();

            public void Initialize(string ro, string rw, string v, AssetManifest m, Action onComplete, Action<string> onFailure) { onComplete?.Invoke(); }
            public bool HasAsset(string n) => true;

            public void LoadAssetAsync(string assetName, Type assetType, int priority, LoadAssetCallbacks callbacks, object userData)
            {
                m_Pending.Add(new Pending { Name = assetName, Callbacks = callbacks, UserData = userData });
            }

            public void LoadSceneAsync(string n, int p, Action<float> oP, Action<string> oS, Action<string, string> oF, object ud) { }
            public void UnloadSceneAsync(string n, Action<string> oS, Action<string, string> oF, object ud) { }
            public void UnloadAsset(object asset) { UnloadCalls++; UnloadedAssets.Add(asset); }
            public void UnloadUnusedAssets(bool gc) { }
            public void Shutdown() { }

            public int PendingCount => m_Pending.Count;

            private Pending Take(string assetName)
            {
                for (int i = 0; i < m_Pending.Count; i++)
                {
                    if (m_Pending[i].Name == assetName)
                    {
                        Pending p = m_Pending[i];
                        m_Pending.RemoveAt(i);
                        return p;
                    }
                }
                throw new InvalidOperationException("No pending request for " + assetName);
            }

            private Pending Peek(string assetName)
            {
                for (int i = 0; i < m_Pending.Count; i++)
                {
                    if (m_Pending[i].Name == assetName) return m_Pending[i];
                }
                throw new InvalidOperationException("No pending request for " + assetName);
            }

            public object Succeed(string assetName)
            {
                object asset = new FakeAsset { Name = assetName };
                Succeed(assetName, asset);
                return asset;
            }

            public void Succeed(string assetName, object asset)
            {
                Pending p = Take(assetName);
                p.Callbacks.LoadAssetSuccessCallback?.Invoke(assetName, asset, 0.1f, p.UserData);
            }

            public void Fail(string assetName, LoadResourceStatus status = LoadResourceStatus.NotExist, string err = "missing")
            {
                Pending p = Take(assetName);
                p.Callbacks.LoadAssetFailureCallback?.Invoke(assetName, status, err, p.UserData);
            }

            public void Report(string assetName, float progress)
            {
                Pending p = Peek(assetName);
                p.Callbacks.LoadAssetUpdateCallback?.Invoke(assetName, progress, p.UserData);
            }
        }

        private sealed class FakeAsset
        {
            public string Name;
        }

        private static (IResourceManager, MultiLoader) NewManager()
        {
            Type t = typeof(IResourceManager).Assembly.GetType("EjoyFramework.Core.Resource.ResourceManager");
            var rm = (IResourceManager)Activator.CreateInstance(t, true);
            var loader = new MultiLoader();
            rm.SetMode(ResourceMode.EditorSimulation);
            rm.SetLoader(loader);
            rm.InitializeAsync(null, null, null);
            return (rm, loader);
        }

        /// <summary>ResourceManager 是 internal 类型，Shutdown 只能反射调用。</summary>
        private static void ShutdownManager(IResourceManager rm)
        {
            rm.GetType().GetMethod("Shutdown").Invoke(rm, null);
        }

        private static int UsingCount<T>()
        {
            return UsingCount(typeof(T));
        }

        private static int UsingCount(Type type)
        {
            foreach (ReferencePoolInfo info in ReferencePool.GetAllReferencePoolInfos())
            {
                if (info.Type == type) return info.UsingReferenceCount;
            }
            return 0;
        }

        /// <summary>AssetBatch.Entry 是私有嵌套类型，测试程序集拿不到，只能按简单名查。</summary>
        private static int UsingEntryCount()
        {
            foreach (ReferencePoolInfo info in ReferencePool.GetAllReferencePoolInfos())
            {
                if (info.Type.Name == "Entry" && info.Type.DeclaringType == typeof(AssetBatch))
                {
                    return info.UsingReferenceCount;
                }
            }
            return 0;
        }

        [SetUp]
        public void SetUp()
        {
            // 批次与其 Entry 都走全局 ReferencePool，用例之间必须隔离，否则收支断言会互相干扰。
            ReferencePool.ClearAll();
        }

        // ===== 全成功 =====

        [Test]
        public void Batch_AllSucceed_CompletesOnceWithZeroFailures()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");

            int completedCalls = 0;
            batch.Completed += _ => completedCalls++;
            batch.Start();

            Assert.AreEqual(AssetBatchState.Loading, batch.State);
            Assert.AreEqual(0, completedCalls);

            loader.Succeed("a");
            Assert.AreEqual(0, completedCalls, "批次不应在部分完成时触发 Completed");

            loader.Succeed("b");
            Assert.AreEqual(1, completedCalls);
            Assert.AreEqual(AssetBatchState.Done, batch.State);
            Assert.IsTrue(batch.IsDone);
            Assert.AreEqual(0, batch.FailedCount);
            Assert.AreEqual(2, batch.TotalCount);
            Assert.AreEqual(1f, batch.Progress, 0.0001f);
        }

        [Test]
        public void Batch_Empty_CompletesImmediately()
        {
            var (rm, _) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch();
            int completedCalls = 0;
            batch.Completed += _ => completedCalls++;

            BatchHandle handle = batch.Start();

            Assert.AreEqual(1, completedCalls);
            Assert.AreEqual(AssetBatchState.Done, batch.State);
            Assert.AreEqual(1f, handle.Progress, 0.0001f);
            Assert.IsTrue(handle.IsDone);
        }

        [Test]
        public void Batch_DuplicateAdd_IsIgnored()
        {
            var (rm, _) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("a");
            Assert.AreEqual(1, batch.TotalCount);
        }

        [Test]
        public void Batch_AddAfterStart_Throws()
        {
            var (rm, _) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            batch.Start();
            Assert.Throws<FrameworkException>(() => batch.Add("b"));
        }

        [Test]
        public void Completed_RegisteredAfterDone_FiresSynchronously()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            batch.Start();
            loader.Succeed("a");

            AssetBatch captured = null;
            batch.Completed += b => captured = b;
            Assert.AreSame(batch, captured);
        }

        // ===== 部分失败 =====

        [Test]
        public void Batch_PartialFailure_StillCompletesAndCountsFailures()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("ok").Add("bad");
            int completedCalls = 0;
            batch.Completed += _ => completedCalls++;
            batch.Start();

            loader.Succeed("ok");
            loader.Fail("bad");

            Assert.AreEqual(1, completedCalls);
            Assert.AreEqual(AssetBatchState.Done, batch.State);
            Assert.AreEqual(1, batch.FailedCount);
            Assert.AreEqual(2, batch.FinishedCount);
            Assert.AreEqual(1, rm.CachedAssetCount, "只有成功的资产被 Pin");
        }

        [Test]
        public void Batch_LoaderNotInitialized_FailsAllSynchronouslyWithoutPrematureCompletion()
        {
            Type t = typeof(IResourceManager).Assembly.GetType("EjoyFramework.Core.Resource.ResourceManager");
            var rm = (IResourceManager)Activator.CreateInstance(t, true);
            // 故意不 SetLoader/InitializeAsync：每次 LoadAsset 会同步回失败。

            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b").Add("c");
            int completedCalls = 0;
            batch.Completed += _ => completedCalls++;
            batch.Start();

            Assert.AreEqual(1, completedCalls, "Completed 只能触发一次，且必须在所有条目发起后");
            Assert.AreEqual(3, batch.FailedCount);
            Assert.AreEqual(AssetBatchState.Done, batch.State);
        }

        // ===== 进度聚合 =====

        [Test]
        public void Batch_Progress_AggregatesAcrossEntries()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");
            batch.Start();

            Assert.AreEqual(0f, batch.Progress, 0.0001f);

            loader.Report("a", 0.5f);
            Assert.AreEqual(0.25f, batch.Progress, 0.0001f);

            loader.Report("b", 0.5f);
            Assert.AreEqual(0.5f, batch.Progress, 0.0001f);

            loader.Succeed("a"); // 已落定条目按 1 计
            Assert.AreEqual(0.75f, batch.Progress, 0.0001f);

            loader.Succeed("b");
            Assert.AreEqual(1f, batch.Progress, 0.0001f);
        }

        // ===== 同步 Get =====

        [Test]
        public void GetCachedAsset_HitAfterBatchCompletes()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("hero");
            batch.Start();
            object asset = loader.Succeed("hero");

            FakeAsset got;
            Assert.IsTrue(rm.TryGetCachedAsset("hero", out got));
            Assert.AreSame(asset, got);
            Assert.AreSame(asset, rm.GetCachedAsset<FakeAsset>("hero"));
            Assert.IsTrue(batch.TryGetAsset("hero", out got));
            Assert.AreSame(asset, got);
        }

        [Test]
        public void GetCachedAsset_MissThrows()
        {
            var (rm, _) = NewManager();

            FakeAsset got;
            Assert.IsFalse(rm.TryGetCachedAsset("nope", out got));

            var ex = Assert.Throws<FrameworkException>(() => rm.GetCachedAsset<FakeAsset>("nope"));
            StringAssert.Contains("not preloaded", ex.Message);
        }

        [Test]
        public void GetCachedAsset_WrongType_MissesAndThrows()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("hero");
            batch.Start();
            loader.Succeed("hero");

            string wrong;
            Assert.IsFalse(rm.TryGetCachedAsset("hero", out wrong));
            Assert.Throws<FrameworkException>(() => rm.GetCachedAsset<string>("hero"));
        }

        // ===== Release / Pin 计数 =====

        [Test]
        public void Release_UnpinsAndUnloadsAssets()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");
            batch.Start();
            loader.Succeed("a");
            loader.Succeed("b");

            Assert.AreEqual(2, rm.CachedAssetCount);
            Assert.AreEqual(0, loader.UnloadCalls);

            batch.Release();

            Assert.AreEqual(0, rm.CachedAssetCount);
            Assert.AreEqual(2, loader.UnloadCalls);

            FakeAsset got;
            Assert.IsFalse(rm.TryGetCachedAsset("a", out got), "Release 后缓存应命中失败");
        }

        [Test]
        public void Release_WithSharedAsset_UnloadsOncePerPin()
        {
            var (rm, loader) = NewManager();
            var shared = new FakeAsset { Name = "shared" };

            AssetBatch first = rm.CreateAssetBatch().Add("shared");
            first.Start();
            loader.Succeed("shared", shared);

            AssetBatch second = rm.CreateAssetBatch().Add("shared");
            second.Start();
            loader.Succeed("shared", shared);

            Assert.AreEqual(1, rm.CachedAssetCount, "同一资产在缓存里只占一个条目");

            // 每次成功加载都让 loader 的 RefCount +1，所以每次 Release 都必须还一次，
            // 否则 loader 计数永远归不了零，bundle 不会被卸载。
            first.Release();
            Assert.AreEqual(1, loader.UnloadCalls, "第一次 Release 必须还回它自己那一次引用");
            Assert.AreEqual(1, rm.CachedAssetCount, "另一批次仍持有 Pin，条目还在，同步 Get 仍应命中");

            FakeAsset stillThere;
            Assert.IsTrue(rm.TryGetCachedAsset("shared", out stillThere));

            second.Release();
            Assert.AreEqual(2, loader.UnloadCalls, "Pin 与 UnloadAsset 必须 1:1");
            Assert.AreEqual(0, rm.CachedAssetCount);
        }

        [Test]
        public void Pin_SameNameDifferentInstance_KeepsFirstAndUnloadsSecond()
        {
            var (rm, loader) = NewManager();
            var firstAsset = new FakeAsset { Name = "dup" };
            var secondAsset = new FakeAsset { Name = "dup" };

            AssetBatch first = rm.CreateAssetBatch().Add("dup");
            first.Start();
            loader.Succeed("dup", firstAsset);

            AssetBatch second = rm.CreateAssetBatch().Add("dup");
            second.Start();
            loader.Succeed("dup", secondAsset);

            Assert.AreEqual(1, loader.UnloadCalls, "后到的异实例应当场退还");
            Assert.AreSame(secondAsset, loader.UnloadedAssets[0]);
            Assert.AreSame(firstAsset, rm.GetCachedAsset<FakeAsset>("dup"), "先到者继续有效");
            Assert.AreEqual(1, second.FailedCount, "没 Pin 住的条目按失败计");

            FakeAsset unused;
            Assert.IsFalse(second.TryGetAsset("dup", out unused), "第二个批次不持有 Pin，不应声称拥有它");

            second.Release();
            Assert.AreEqual(1, loader.UnloadCalls, "第二个批次没 Pin，Release 不应再还一次");

            first.Release();
            Assert.AreEqual(2, loader.UnloadCalls);
            Assert.AreEqual(0, rm.CachedAssetCount);
        }

        [Test]
        public void Release_IsIdempotent()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            batch.Start();
            loader.Succeed("a");

            batch.Release();
            batch.Release(); // 第二次必须是 no-op，不能重复 Unload

            Assert.AreEqual(1, loader.UnloadCalls);
        }

        // ===== 取消 =====

        [Test]
        public void Cancel_FiresCompletedAndDiscardsLateAssets()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");
            AssetBatchState observed = AssetBatchState.Collecting;
            int completedCalls = 0;
            batch.Completed += b => { completedCalls++; observed = b.State; };
            batch.Start();

            loader.Succeed("a"); // 先成功一份，会被 Pin
            Assert.AreEqual(1, rm.CachedAssetCount);

            batch.Cancel();

            Assert.AreEqual(1, completedCalls);
            Assert.AreEqual(AssetBatchState.Cancelled, observed);
            Assert.IsTrue(batch.IsDone);
            Assert.AreEqual(0, rm.CachedAssetCount, "Cancel 应解 Pin 已到达资产");
            Assert.AreEqual(1, loader.UnloadCalls);

            // 迟到的 b 到达后应被直接卸载，不进缓存
            loader.Succeed("b");
            Assert.AreEqual(2, loader.UnloadCalls);
            Assert.AreEqual(0, rm.CachedAssetCount);
        }

        [Test]
        public void Cancel_AfterDone_IsNoOp()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            batch.Start();
            loader.Succeed("a");

            batch.Cancel();

            Assert.AreEqual(AssetBatchState.Done, batch.State);
            Assert.AreEqual(1, rm.CachedAssetCount, "Done 后 Cancel 不应解 Pin");
        }

        [Test]
        public void Cancel_BeforeStart_MarksCancelled()
        {
            var (rm, _) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            int completedCalls = 0;
            batch.Completed += _ => completedCalls++;

            batch.Cancel();

            Assert.AreEqual(AssetBatchState.Cancelled, batch.State);
            Assert.AreEqual(1, completedCalls);
            Assert.Throws<FrameworkException>(() => batch.Start());
        }

        // ===== BatchHandle 版本号 =====

        [Test]
        public void Handle_IsValidWhileBatchAlive_InvalidAfterRelease()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            BatchHandle handle = batch.Start();

            Assert.IsTrue(handle.IsValid);
            Assert.IsFalse(handle.IsDone);
            Assert.AreEqual(1, handle.TotalCount);

            loader.Succeed("a");
            Assert.IsTrue(handle.IsDone);
            Assert.AreEqual(1f, handle.Progress, 0.0001f);
            Assert.AreSame(batch, handle.Batch);

            batch.Release();

            Assert.IsFalse(handle.IsValid, "批次回池后旧句柄必须失效");
            Assert.IsTrue(handle.IsDone, "失效句柄报告 IsDone=true，避免轮询死等");
            Assert.AreEqual(1f, handle.Progress, 0.0001f);
            Assert.IsNull(handle.Batch);
            Assert.AreEqual(0, handle.TotalCount);
        }

        [Test]
        public void Handle_DoesNotAliasRecycledBatch()
        {
            var (rm, loader) = NewManager();
            AssetBatch first = rm.CreateAssetBatch().Add("a");
            BatchHandle stale = first.Start();
            loader.Succeed("a");
            first.Release();

            // 从池中再租一个——很可能就是刚回收的那个实例。
            AssetBatch second = rm.CreateAssetBatch().Add("x").Add("y");
            second.Start();

            Assert.IsFalse(stale.IsValid, "旧句柄不得读到复用后的批次");
            Assert.AreEqual(0, stale.TotalCount);
            Assert.AreEqual(2, second.Handle.TotalCount);
        }

        [Test]
        public void InvalidHandle_OperationsAreNoOps()
        {
            BatchHandle handle = BatchHandle.Invalid;

            Assert.IsFalse(handle.IsValid);
            Assert.IsTrue(handle.IsDone);
            Assert.AreEqual(1f, handle.Progress, 0.0001f);
            Assert.DoesNotThrow(() => handle.Cancel());
            Assert.DoesNotThrow(() => handle.Release());
            Assert.AreEqual(BatchHandle.Invalid, handle);
        }

        [Test]
        public void Handle_CancelAndRelease_ForwardToBatch()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            BatchHandle handle = batch.Start();

            handle.Cancel();
            Assert.AreEqual(AssetBatchState.Cancelled, batch.State);

            handle.Release();
            Assert.IsFalse(handle.IsValid);
        }

        // ===== 回调内重入 Release =====

        [Test]
        public void Release_InsideCompletedListener_LeavesReturnedHandleInvalid()
        {
            var (rm, _) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch();   // 空批次：Start 内同步完成
            batch.Completed += b => b.Release();

            BatchHandle handle = batch.Start();

            // 句柄在触发监听器之前取值，因此必须反映"批次已被回收"这一事实。
            Assert.IsFalse(handle.IsValid, "监听器里 Release 之后，Start 返回的句柄不得仍报告有效");
            Assert.IsTrue(handle.IsDone);
            Assert.IsNull(handle.Batch);
            Assert.AreEqual(0, UsingCount<AssetBatch>(), "批次应已归还引用池");
        }

        [Test]
        public void Release_InsideCompletedListener_OnAsyncPath_UnpinsEverything()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");
            batch.Completed += b => b.Release();
            BatchHandle handle = batch.Start();

            loader.Succeed("a");
            Assert.DoesNotThrow(() => loader.Succeed("b"));

            Assert.AreEqual(2, loader.UnloadCalls, "监听器内 Release 应把两份资产都解 Pin 卸载");
            Assert.AreEqual(0, rm.CachedAssetCount);
            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, UsingCount<AssetBatch>());
        }

        [Test]
        public void Release_BeforeDone_FiresCompletedAsCancelled()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");
            int completedCalls = 0;
            AssetBatchState observed = AssetBatchState.Collecting;
            batch.Completed += b => { completedCalls++; observed = b.State; };
            batch.Start();

            loader.Succeed("a");
            batch.Release();   // 还有一条在途就释放

            Assert.AreEqual(1, completedCalls, "未结束就 Release 必须补发一次 Completed");
            Assert.AreEqual(AssetBatchState.Cancelled, observed);
            Assert.AreEqual(1, loader.UnloadCalls);
        }

        // ===== 迟到资产 =====

        [Test]
        public void LateArrivingAsset_AfterRelease_IsUnloadedAndNotCached()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");
            BatchHandle handle = batch.Start();

            loader.Succeed("a");
            batch.Release();

            Assert.AreEqual(1, loader.UnloadCalls, "'a' 在 Release 时被解 Pin 卸载");
            Assert.AreEqual(0, rm.CachedAssetCount);

            // 批次已回池（甚至可能已被别人复用），'b' 此刻到达属于无人认领
            loader.Succeed("b");

            Assert.AreEqual(2, loader.UnloadCalls, "迟到资产必须被卸载");
            Assert.AreEqual(0, rm.CachedAssetCount, "迟到资产不得进缓存");
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void LateArrivingAsset_AfterRecycleAndReuse_DoesNotTouchNewBatch()
        {
            var (rm, loader) = NewManager();
            AssetBatch first = rm.CreateAssetBatch().Add("a").Add("b");
            first.Start();
            loader.Succeed("a");
            first.Release();

            // 复用同一个池对象承载完全不同的清单
            AssetBatch second = rm.CreateAssetBatch().Add("x");
            second.Start();

            loader.Succeed("b");   // 上一轮的迟到回调

            Assert.AreEqual(0, second.FinishedCount, "迟到回调不得推进复用后的批次");
            Assert.AreEqual(AssetBatchState.Loading, second.State);
            Assert.AreEqual(0, rm.CachedAssetCount);
            Assert.AreEqual(2, loader.UnloadCalls, "迟到资产必须被卸载（'a' 在 Release 时一次 + 'b' 迟到一次）");
        }

        // ===== Shutdown 之后 =====

        [Test]
        public void Shutdown_UnloadsCachedAssetsAndBlocksNewBatches()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            batch.Start();
            loader.Succeed("a");
            Assert.AreEqual(1, rm.CachedAssetCount);

            ShutdownManager(rm);

            Assert.AreEqual(1, loader.UnloadCalls, "Shutdown 必须把缓存里的资产按 Pin 计数还回去");
            Assert.AreEqual(0, rm.CachedAssetCount);
            Assert.Throws<FrameworkException>(() => rm.CreateAssetBatch());
        }

        [Test]
        public void Shutdown_LateArrivingAsset_IsNotPinnedBack()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a");
            batch.Start();

            ShutdownManager(rm);

            // loader 在 Shutdown 之后才送达：缓存已封存，不能把它 Pin 回一张再也不会被清理的表。
            Assert.DoesNotThrow(() => loader.Succeed("a"));

            Assert.AreEqual(0, rm.CachedAssetCount, "封存后的缓存不得再接受 Pin");
            Assert.AreEqual(1, batch.FailedCount, "没 Pin 住的条目按失败计");

            FakeAsset unused;
            Assert.IsFalse(rm.TryGetCachedAsset("a", out unused));
        }

        // ===== 裸构造防护 / 引用池收支 =====

        [Test]
        public void BareConstructedBatch_ThrowsOnAddAndStart()
        {
            var batch = new AssetBatch();
            Assert.Throws<FrameworkException>(() => batch.Add("a"));
            Assert.Throws<FrameworkException>(() => batch.Start());
        }

        [Test]
        public void Release_ReentrantFromCompletedListener_ReleasesToPoolExactlyOnce()
        {
            var (rm, loader) = NewManager();
            AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");

            int completedCalls = 0;
            batch.Completed += b =>
            {
                completedCalls++;
                b.Release();   // 监听器里重入 Release —— 外层那次仍在栈上
            };
            batch.Start();
            loader.Succeed("a");

            Assert.AreEqual(0, completedCalls);

            // 未完成就 Release：内部会补发 Completed，监听器又调一次 Release。
            batch.Release();

            Assert.AreEqual(1, completedCalls);
            Assert.AreEqual(0, UsingCount<AssetBatch>(), "同一实例不得被两次压进引用池");
            Assert.AreEqual(0, UsingEntryCount());
            Assert.AreEqual(1, loader.UnloadCalls, "解 Pin 不得因重入而翻倍");
            Assert.AreEqual(0, rm.CachedAssetCount);

            // 双租检测：若刚才压了两次，这两次 Create 会拿到同一个实例。
            AssetBatch first = rm.CreateAssetBatch();
            AssetBatch second = rm.CreateAssetBatch();
            Assert.AreNotSame(first, second, "引用池被双压后会把同一实例租给两个调用方");
        }

        [Test]
        public void ReferencePool_BalancesAfterFullBatchLifecycle()
        {
            var (rm, loader) = NewManager();
            Assert.AreEqual(0, UsingCount<AssetBatch>());
            Assert.AreEqual(0, UsingEntryCount());

            for (int i = 0; i < 3; i++)
            {
                AssetBatch batch = rm.CreateAssetBatch().Add("a").Add("b");
                Assert.AreEqual(1, UsingCount<AssetBatch>(), "在用批次数应为 1");
                Assert.AreEqual(2, UsingEntryCount(), "在用条目数应为 2");
                batch.Start();
                loader.Succeed("a");
                loader.Succeed("b");
                batch.Release();
                Assert.AreEqual(0, UsingCount<AssetBatch>(), "Release 后批次必须全部归还");
                Assert.AreEqual(0, UsingEntryCount(), "Release 后条目必须全部归还");
            }

            Assert.AreEqual(6, loader.UnloadCalls, "3 轮 × 2 份资产，Pin 与 Unload 严格 1:1");
            Assert.AreEqual(0, rm.CachedAssetCount);
        }
    }
}
