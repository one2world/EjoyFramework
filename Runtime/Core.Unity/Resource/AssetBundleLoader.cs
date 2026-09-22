//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using EjoyFramework.Core.Coroutines;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Resource
{
    /// <summary>
    /// 运行时 AssetBundle Loader。
    ///
    /// 职责：
    ///   - 加载 bundle（先 ReadWritePath 后 ReadOnlyPath，允许热更覆盖）
    ///   - 递归解析 bundle 依赖
    ///   - bundle 与 asset 双层引用计数；归零延迟卸载
    ///   - 同 asset 并发请求合并（多个 LoadAsset 同时来，仅一次加载）
    ///   - 主线程协程驱动；所有回调在主线程
    ///   - 读取 manifest 反查 assetName → bundleName → relativePath
    ///
    /// 不在本类范围：
    ///   - 热更下载（应由独立 PatchManager + 复制到 ReadWritePath 后再触发 Initialize）
    ///   - 加密/压缩自定义（用 Unity 原生 LZ4/LZMA + AssetBundle 自带）
    /// </summary>
    public sealed class AssetBundleLoader : IResourceLoader
    {
        // 配置：协程驱动由 ICoroutineManager 承担（解耦 MonoBehaviour，便于测试与单元化）
        private const string CoroutineTag = "AssetBundleLoader";
        private readonly ICoroutineManager m_Coroutine;
        private readonly int m_MaxConcurrentLoads;
        private readonly float m_BundleUnloadDelay;
        private readonly ResidentBudget m_Budget = new ResidentBudget();
        private readonly List<string> m_EvictionScratch = new List<string>();

        // 路径与 manifest
        private string m_ReadOnlyPath;
        private string m_ReadWritePath;
        private AssetManifest m_Manifest;
        private Dictionary<string, BundleInfo> m_BundleByName;
        private Dictionary<string, AssetInfo> m_AssetByName;
        private bool m_Initialized;

        // 运行时状态
        private readonly Dictionary<string, BundleHandle> m_LoadedBundles = new Dictionary<string, BundleHandle>(StringComparer.Ordinal);
        private readonly Dictionary<string, BundleLoadingTask> m_LoadingBundles = new Dictionary<string, BundleLoadingTask>(StringComparer.Ordinal);
        private readonly Dictionary<UnityEngine.Object, AssetRecord> m_LoadedAssets = new Dictionary<UnityEngine.Object, AssetRecord>();
        // 合并键为 (assetName, assetType)：同名不同类型的并发请求各自独立加载，互不串扰；
        // 仅完全相同的 (名称, 类型) 才合并为一次加载。assetType 为 null（不限类型）自成一键。
        // string 默认相等比较即序数比较（与历史 StringComparer.Ordinal 一致）；Type 默认按引用相等（每类型单例）。
        private readonly Dictionary<(string Name, Type Type), AssetLoadingTask> m_LoadingAssets = new Dictionary<(string, Type), AssetLoadingTask>();
        // sceneAssetName → 该场景加载时 ref 过的完整 bundle 集合（owner + 依赖闭包）。卸载时按同一集合 RemoveRef。
        private readonly Dictionary<string, List<BundleHandle>> m_SceneBundleRefs = new Dictionary<string, List<BundleHandle>>(StringComparer.Ordinal);
        private int m_ActiveBundleLoads;

        public AssetBundleLoader(ICoroutineManager coroutine, int maxConcurrentLoads = 4, float bundleUnloadDelay = 5f)
        {
            if (coroutine == null) throw new FrameworkException("Coroutine manager is required.");
            m_Coroutine = coroutine;
            m_MaxConcurrentLoads = Math.Max(1, maxConcurrentLoads);
            m_BundleUnloadDelay = Math.Max(0f, bundleUnloadDelay);
        }

        public bool IsInitialized { get { return m_Initialized; } }
        public bool RequiresManifest { get { return true; } }
        public int LoadedBundleCount { get { return m_LoadedBundles.Count; } }
        public int LoadedAssetCount { get { return m_LoadedAssets.Count; } }
        public int LoadingTaskCount { get { return m_LoadingAssets.Count + m_LoadingBundles.Count; } }

        /// <summary>常驻 bundle 总字节（按 manifest 的 BundleInfo.Size 计）。</summary>
        public long ResidentBytes { get { return m_Budget.ResidentBytes; } }

        /// <summary>
        /// 常驻内存预算（字节）；0 = 未启用（引用归零后按 bundleUnloadDelay 延迟卸载）。
        /// 启用后引用归零的 bundle 进入温缓存，超预算时按 LRU 淘汰；调小预算立即收缩。
        /// </summary>
        public long MemoryBudgetBytes
        {
            get { return m_Budget.BudgetBytes; }
            set { m_Budget.BudgetBytes = value; EnforceBudget(); }
        }

        /// <summary>主动把常驻收缩到 targetBytes（关卡切换 / 内存告警）。返回卸载的 bundle 数。</summary>
        public int TrimResident(long targetBytes)
        {
            int n = m_Budget.SelectEvictions(m_EvictionScratch, targetBytes);
            for (int i = 0; i < m_EvictionScratch.Count; i++)
            {
                BundleHandle h;
                if (m_LoadedBundles.TryGetValue(m_EvictionScratch[i], out h) && h.RefCount <= 0) UnloadBundleNow(h);
            }
            m_EvictionScratch.Clear();
            return n;
        }

        private void EnforceBudget()
        {
            if (!m_Budget.IsEnabled) return;
            TrimResident(m_Budget.BudgetBytes);
        }

        // ===== Initialize =====

        public void Initialize(string readOnlyPath, string readWritePath, string currentVariant,
            AssetManifest manifest, Action onComplete, Action<string> onFailure)
        {
            if (manifest == null) { if (onFailure != null) onFailure("AssetManifest is required for AssetBundleLoader."); return; }
            if (manifest.Bundles == null || manifest.Assets == null) { if (onFailure != null) onFailure("Manifest is missing bundles/assets."); return; }
            if (string.IsNullOrEmpty(readOnlyPath) && string.IsNullOrEmpty(readWritePath))
            { if (onFailure != null) onFailure("At least one of readOnlyPath/readWritePath must be set."); return; }

            try
            {
                m_ReadOnlyPath = readOnlyPath;
                m_ReadWritePath = readWritePath;
                m_Manifest = manifest;

                m_BundleByName = new Dictionary<string, BundleInfo>(manifest.Bundles.Count, StringComparer.Ordinal);
                foreach (var b in manifest.Bundles)
                {
                    if (b == null || string.IsNullOrEmpty(b.Name)) continue;
                    m_BundleByName[b.Name] = b;
                }

                m_AssetByName = new Dictionary<string, AssetInfo>(manifest.Assets.Count, StringComparer.Ordinal);
                foreach (var a in manifest.Assets)
                {
                    if (a == null || string.IsNullOrEmpty(a.Name)) continue;
                    m_AssetByName[a.Name] = a;
                }

                m_Initialized = true;
                FrameworkLog.Info("AssetBundleLoader initialized: {0} bundles, {1} assets, platform={2}",
                    m_BundleByName.Count, m_AssetByName.Count, manifest.Platform);
                if (onComplete != null) onComplete();
            }
            catch (Exception ex)
            {
                if (onFailure != null) onFailure(ex.Message);
            }
        }

        public bool HasAsset(string assetName)
        {
            return m_Initialized && !string.IsNullOrEmpty(assetName) && m_AssetByName.ContainsKey(assetName);
        }

        // ===== LoadAsset =====

        public void LoadAssetAsync(string assetName, Type assetType, int priority,
            LoadAssetCallbacks callbacks, object userData)
        {
            if (!m_Initialized) { Fail(callbacks, assetName, LoadResourceStatus.NotReady, "Loader is not initialized.", userData); return; }
            if (string.IsNullOrEmpty(assetName)) { Fail(callbacks, assetName, LoadResourceStatus.NotExist, "Asset name is invalid.", userData); return; }

            AssetInfo assetInfo;
            if (!m_AssetByName.TryGetValue(assetName, out assetInfo))
            { Fail(callbacks, assetName, LoadResourceStatus.NotExist, "Asset not in manifest: " + assetName, userData); return; }
            if (assetInfo.IsScene)
            { Fail(callbacks, assetName, LoadResourceStatus.AssetError, "Use LoadScene for scene assets.", userData); return; }

            // 同 (assetName, assetType) 已在加载中：合并请求（同名不同类型各自独立加载，互不干扰）。
            var loadKey = (assetName, assetType);
            AssetLoadingTask existing;
            if (m_LoadingAssets.TryGetValue(loadKey, out existing))
            {
                existing.Subscribe(assetType, priority, callbacks, userData);
                return;
            }

            var task = new AssetLoadingTask(assetName, assetInfo, assetType, priority, callbacks, userData);
            m_LoadingAssets.Add(task.Key, task);
            m_Coroutine.Run(LoadAssetRoutine(task), CoroutineTag, this);
        }

        private IEnumerator LoadAssetRoutine(AssetLoadingTask task)
        {
            float startTime = Time.realtimeSinceStartup;

            // 1) 确保所属 bundle 已加载（含依赖）
            BundleInfo bundleInfo;
            if (!m_BundleByName.TryGetValue(task.AssetInfo.BundleName, out bundleInfo))
            {
                m_LoadingAssets.Remove(task.Key);
                task.FailAll(LoadResourceStatus.DependencyError, "Bundle not in manifest: " + task.AssetInfo.BundleName);
                yield break;
            }

            // 引用计数不变式（refcount invariant）：
            //   一次成功的 asset 加载，对“所属 bundle + 其全部依赖闭包（去重）”各 +1。
            //   UnloadAsset 必须对完全相同的集合各 -1。
            //   因此把本次加载实际引用到的全部 bundle（owner + deps）收集到 refdBundles，
            //   存入 AssetRecord，卸载时按同一集合镜像 RemoveRef，杜绝“依赖永不卸载/提前卸载”。
            var refdBundles = new List<BundleHandle>();
            yield return EnsureBundleLoadedRoutine(bundleInfo, refdBundles);
            BundleHandle handle;
            if (!m_LoadedBundles.TryGetValue(bundleInfo.Name, out handle))
            {
                m_LoadingAssets.Remove(task.Key);
                ReleaseBundleRefs(refdBundles);
                task.FailAll(LoadResourceStatus.DependencyError, "Bundle load failed: " + bundleInfo.Name);
                yield break;
            }

            // 2) bundle 内取 asset（异步）
            AssetBundleRequest req;
            try
            {
                Type t = task.PrimaryAssetType;
                req = t != null ? handle.Bundle.LoadAssetAsync(task.AssetName, t) : handle.Bundle.LoadAssetAsync(task.AssetName);
            }
            catch (Exception ex)
            {
                m_LoadingAssets.Remove(task.Key);
                ReleaseBundleRefs(refdBundles);
                task.FailAll(LoadResourceStatus.AssetError, "AssetBundle.LoadAssetAsync threw: " + ex.Message);
                yield break;
            }

            while (!req.isDone)
            {
                task.NotifyProgress(req.progress);
                yield return null;
            }

            UnityEngine.Object asset = req.asset;
            if (asset == null)
            {
                ReleaseBundleRefs(refdBundles);
                m_LoadingAssets.Remove(task.Key);
                task.FailAll(LoadResourceStatus.AssetError, "Asset returned null from bundle: " + task.AssetName);
                yield break;
            }

            // 注册 asset 与 bundle 关联（asset 引用 owner + 全部依赖）。
            // 该协程已为 refdBundles 内每个 bundle +1 ref（EnsureBundleLoadedRoutine 内部，含依赖闭包）。
            // 如果 AssetRecord 已存在（同 asset 之前已加载过），其 RefBundles 已持 ref；
            // 当前协程持有的整套 ref 是冗余的，需整体释放，避免依赖泄漏。
            AssetRecord rec;
            bool recExisted = m_LoadedAssets.TryGetValue(asset, out rec);
            if (recExisted)
            {
                ReleaseBundleRefs(refdBundles);
            }
            else
            {
                rec = new AssetRecord { Asset = asset, AssetName = task.AssetName, OwnerBundle = handle, RefBundles = refdBundles, RefCount = 0 };
                m_LoadedAssets.Add(asset, rec);
                // 当前协程持有的整套 ref（owner + deps）作为 rec 的所有权保留
            }

            // 先把任务移出 m_LoadingAssets，再投递回调：保持原有时序（回调内对同 asset 的再次 LoadAssetAsync 会启动新加载，
            // 而非并入这个正在完成、不会再投递的任务）。
            m_LoadingAssets.Remove(task.Key);

            // 逐订阅者类型校验交由 SucceedAll：合并键含类型，故本任务订阅者类型一致，此校验为防御性兜底，
            // 应对”实际加载到的资源类型与请求类型不符”的异常情况。仅按”成功投递”的订阅者数累加引用，
            // 不符者收到 TypeError、不持有引用。
            float duration = Time.realtimeSinceStartup - startTime;
            int succeeded = task.SucceedAll(asset, duration);
            rec.RefCount += succeeded;

            // 全部订阅者类型均不符（本次新建的记录又无人成功）→ 回收记录与整套 bundle ref，避免泄漏。
            if (rec.RefCount <= 0 && !recExisted)
            {
                m_LoadedAssets.Remove(asset);
                ReleaseBundleRefs(rec.RefBundles);
            }
        }

        // ===== Bundle 加载（含依赖） =====

        // 加载 info 所属 bundle 及其依赖闭包；对每个成功获取的 bundle +1 ref，
        // 并把对应 handle 追加进 refdBundles（去重：同名只 +1，因 manifest 内依赖列表可能交叉）。
        // 调用方负责后续要么把 refdBundles 交给 AssetRecord 长期持有，要么 ReleaseBundleRefs 全部回收。
        private IEnumerator EnsureBundleLoadedRoutine(BundleInfo info, List<BundleHandle> refdBundles)
        {
            // 去重：同一次加载里依赖闭包可能多次到达同一 bundle，只 ref 一次。
            for (int i = 0; i < refdBundles.Count; i++)
            {
                if (refdBundles[i] != null && refdBundles[i].Info != null &&
                    string.Equals(refdBundles[i].Info.Name, info.Name, StringComparison.Ordinal))
                {
                    yield break;
                }
            }

            // 依赖优先：无论自身是否已加载，都必须先把依赖闭包 ref 齐，
            // 这样“parent 已加载”的快路径也会带上依赖的 ref（修复依赖 +0 的漏计）。
            if (info.Dependencies != null)
            {
                for (int i = 0; i < info.Dependencies.Count; i++)
                {
                    BundleInfo depInfo;
                    if (!m_BundleByName.TryGetValue(info.Dependencies[i], out depInfo))
                    {
                        // 清单声明了一个不存在的依赖 → 该 bundle 无法正确加载，向上传播失败（owner 将以 DependencyError 失败）。
                        FrameworkLog.Error("Missing dependency bundle '{0}' for '{1}' — aborting load.", info.Dependencies[i], info.Name);
                        yield break;
                    }
                    yield return EnsureBundleLoadedRoutine(depInfo, refdBundles);
                    // 依赖加载完未就绪 → 失败,不再加载 info 自身,使其不进 m_LoadedBundles,由上层检测为 DependencyError。
                    if (!m_LoadedBundles.ContainsKey(depInfo.Name))
                    {
                        FrameworkLog.Error("Dependency '{0}' of '{1}' failed to load — aborting load.", depInfo.Name, info.Name);
                        yield break;
                    }
                }
            }

            // 已加载：直接 +ref
            BundleHandle existing;
            if (m_LoadedBundles.TryGetValue(info.Name, out existing))
            {
                existing.AddRef();
                refdBundles.Add(existing);
                yield break;
            }

            // 加载中：等
            BundleLoadingTask loading;
            if (m_LoadingBundles.TryGetValue(info.Name, out loading))
            {
                while (!loading.IsDone) yield return null;
                // 并发加载失败 → 不 ref，直接 yield break；上层据 m_LoadedBundles 缺失判定 DependencyError。
                if (loading.Failed || loading.Result == null) yield break;
                loading.Result.AddRef();
                refdBundles.Add(loading.Result);
                yield break;
            }

            // 启动新 bundle 加载
            var newTask = new BundleLoadingTask { Name = info.Name };
            m_LoadingBundles.Add(info.Name, newTask);

            // 并发限流
            while (m_ActiveBundleLoads >= m_MaxConcurrentLoads) yield return null;
            m_ActiveBundleLoads++;

            // 解析运行时路径（先 ReadWrite 后 ReadOnly）。
            // 移动端首包资源会在 ResourceComponent 初始化时先展开到 persistentDataPath，
            // 因此 Loader 始终走本地文件 API，避免业务加载阶段混用 jar/URI 语义。
            string filePath = ResolveBundleFilePath(info);
            if (string.IsNullOrEmpty(filePath))
            {
                m_ActiveBundleLoads--;
                m_LoadingBundles.Remove(info.Name);
                newTask.Failed = true;
                newTask.IsDone = true;
                FrameworkLog.Error("Bundle file not found: {0}", info.RelativePath ?? info.Name);
                yield break;
            }

            AssetBundleCreateRequest req;
            try { req = AssetBundle.LoadFromFileAsync(filePath); }
            catch (Exception ex)
            {
                m_ActiveBundleLoads--;
                m_LoadingBundles.Remove(info.Name);
                newTask.Failed = true;
                newTask.IsDone = true;
                FrameworkLog.Error("AssetBundle.LoadFromFileAsync threw for '{0}': {1}", filePath, ex);
                yield break;
            }

            while (!req.isDone) yield return null;

            m_ActiveBundleLoads--;
            m_LoadingBundles.Remove(info.Name);

            if (req.assetBundle == null)
            {
                newTask.Failed = true;
                newTask.IsDone = true;
                FrameworkLog.Error("AssetBundle.LoadFromFileAsync returned null: {0}", filePath);
                yield break;
            }

            var handle = new BundleHandle(info, req.assetBundle, this);
            handle.AddRef();
            m_LoadedBundles.Add(info.Name, handle);
            m_Budget.OnLoaded(info.Name, info.Size, true);
            refdBundles.Add(handle);
            newTask.Result = handle;
            newTask.IsDone = true;
        }

        private string ResolveBundleFilePath(BundleInfo info)
        {
            string rel = string.IsNullOrEmpty(info.RelativePath) ? info.Name : info.RelativePath;

            // C1 路径遍历防护：manifest 声明的相对路径（RelativePath / Name）不可信，
            // 在与 ReadWrite/ReadOnly 根目录 Path.Combine 之前做白名单语法校验，拒绝 ../、绝对路径、盘符等逃逸。
            // 命中可疑路径返回 null，调用方按“文件未找到”处理（DependencyError），绝不读取根目录之外的文件。
            if (!Utility.Path.IsSafeRelativePath(rel))
            {
                FrameworkLog.Error("AssetBundleLoader rejected unsafe bundle relative path '{0}' (bundle '{1}').", rel, info.Name);
                return null;
            }

            if (!string.IsNullOrEmpty(m_ReadWritePath))
            {
                string p = Path.Combine(m_ReadWritePath, rel);
                // belt-and-braces：Path.Combine 后确认仍在根目录内（规范化兜底）。
                if (Utility.Path.IsContainedIn(m_ReadWritePath, p) && File.Exists(p)) return p;
            }
            if (!string.IsNullOrEmpty(m_ReadOnlyPath))
            {
                string p = Path.Combine(m_ReadOnlyPath, rel);
                if (Utility.Path.IsContainedIn(m_ReadOnlyPath, p) && File.Exists(p)) return p;
            }
            return null;
        }

        // ===== Scene =====

        public void LoadSceneAsync(string sceneAssetName, int priority,
            Action<float> onProgress, Action<string> onSuccess, Action<string, string> onFailure, object userData)
        {
            if (!m_Initialized) { if (onFailure != null) onFailure(sceneAssetName, "Loader is not initialized."); return; }
            AssetInfo info;
            if (!m_AssetByName.TryGetValue(sceneAssetName, out info))
            { if (onFailure != null) onFailure(sceneAssetName, "Scene not in manifest."); return; }
            if (!info.IsScene)
            { if (onFailure != null) onFailure(sceneAssetName, "Asset is not a scene."); return; }
            BundleInfo bundleInfo;
            if (!m_BundleByName.TryGetValue(info.BundleName, out bundleInfo))
            { if (onFailure != null) onFailure(sceneAssetName, "Scene bundle not in manifest: " + info.BundleName); return; }

            m_Coroutine.Run(LoadSceneRoutine(sceneAssetName, bundleInfo, onProgress, onSuccess, onFailure), CoroutineTag, this);
        }

        private IEnumerator LoadSceneRoutine(string sceneAssetName, BundleInfo bundleInfo,
            Action<float> onProgress, Action<string> onSuccess, Action<string, string> onFailure)
        {
            // 单一加载所有权（single-owner）：本 Loader 只负责把场景所在 bundle 及依赖闭包加载 + 引用计数，
            // 使场景对 Unity *可用*；真正的 UnitySceneManager.LoadSceneAsync 由 DefaultSceneHelper 执行一次。
            // 历史 bug：此处也调用过 LoadSceneAsync(Additive)，与 helper 重复触发，导致场景被加载两次。
            var refdBundles = new List<BundleHandle>();
            yield return EnsureBundleLoadedRoutine(bundleInfo, refdBundles);
            if (!m_LoadedBundles.ContainsKey(bundleInfo.Name))
            {
                ReleaseBundleRefs(refdBundles);
                if (onFailure != null) onFailure(sceneAssetName, "Scene bundle load failed: " + bundleInfo.Name);
                yield break;
            }

            // 记录该场景的 bundle 引用集合，供 UnloadScene 对称释放。
            // 同名场景重复加载时累加引用（保留旧集合），避免提前卸载。
            List<BundleHandle> existing;
            if (m_SceneBundleRefs.TryGetValue(sceneAssetName, out existing))
                existing.AddRange(refdBundles);
            else
                m_SceneBundleRefs[sceneAssetName] = refdBundles;

            // bundle 已就绪即视为成功；进度直接报满（实际场景挂载进度由 helper 上报）。
            if (onProgress != null) onProgress(1f);
            if (onSuccess != null) onSuccess(sceneAssetName);
        }

        public void UnloadSceneAsync(string sceneAssetName,
            Action<string> onSuccess, Action<string, string> onFailure, object userData)
        {
            if (string.IsNullOrEmpty(sceneAssetName)) { if (onFailure != null) onFailure(sceneAssetName, "Scene asset name is invalid."); return; }
            m_Coroutine.Run(UnloadSceneRoutine(sceneAssetName, onSuccess, onFailure), CoroutineTag, this);
        }

        private IEnumerator UnloadSceneRoutine(string sceneAssetName,
            Action<string> onSuccess, Action<string, string> onFailure)
        {
            // 单一卸载所有权：实际的 UnitySceneManager.UnloadSceneAsync 由 DefaultSceneHelper 执行；
            // 本 Loader 只对称释放该场景加载时引用过的完整 bundle 集合（owner + 依赖闭包）。
            List<BundleHandle> refs;
            if (m_SceneBundleRefs.TryGetValue(sceneAssetName, out refs))
            {
                m_SceneBundleRefs.Remove(sceneAssetName);
                ReleaseBundleRefs(refs);
            }
            if (onSuccess != null) onSuccess(sceneAssetName);
            yield break;
        }

        // ===== Unload =====

        public void UnloadAsset(object asset)
        {
            var uo = asset as UnityEngine.Object;
            if (uo == null) return;
            AssetRecord rec;
            if (!m_LoadedAssets.TryGetValue(uo, out rec)) return;
            rec.RefCount--;
            if (rec.RefCount > 0) return;
            m_LoadedAssets.Remove(uo);
            // 引用计数对称卸载：对加载时 ref 过的完整集合（owner + 依赖闭包）逐个 RemoveRef。
            ReleaseBundleRefs(rec.RefBundles);
        }

        // 对一组 bundle handle 逐个 RemoveRef，并在归零时安排卸载。
        // 是 EnsureBundleLoadedRoutine 收集的 refdBundles 的逆操作，保证 Add/Remove 严格成对。
        private void ReleaseBundleRefs(List<BundleHandle> handles)
        {
            if (handles == null) return;
            for (int i = 0; i < handles.Count; i++)
            {
                BundleHandle h = handles[i];
                if (h == null) continue;
                h.RemoveRef();
                ScheduleBundleUnloadIfUnused(h);
            }
        }

        public void UnloadUnusedAssets(bool performGCCollect)
        {
            // 主动扫描计数 0 的 bundle 立即卸载
            List<string> toUnload = null;
            foreach (var kv in m_LoadedBundles)
            {
                if (kv.Value.RefCount <= 0)
                {
                    if (toUnload == null) toUnload = new List<string>();
                    toUnload.Add(kv.Key);
                }
            }
            if (toUnload != null)
            {
                for (int i = 0; i < toUnload.Count; i++)
                {
                    BundleHandle h;
                    if (m_LoadedBundles.TryGetValue(toUnload[i], out h)) UnloadBundleNow(h);
                }
            }
            if (performGCCollect)
            {
                Resources.UnloadUnusedAssets();
                GC.Collect();
            }
        }

        public void Shutdown()
        {
            // Stop load and delayed-unload routines before clearing the dictionaries they access.
            // Otherwise a coroutine resumed after Shutdown can mutate disposed loader state or
            // invoke callbacks against an already-replaced ResourceComponent.
            m_Coroutine.StopByOwner(this);

            foreach (var kv in m_LoadedBundles)
            {
                try { if (kv.Value.Bundle != null) kv.Value.Bundle.Unload(true); }
                catch (Exception ex) { FrameworkLog.Error("Bundle.Unload threw for '{0}': {1}", kv.Key, ex); }
            }
            m_LoadedBundles.Clear();
            m_Budget.Clear();
            m_LoadedAssets.Clear();
            m_LoadingBundles.Clear();
            m_LoadingAssets.Clear();
            m_SceneBundleRefs.Clear();
            m_BundleByName = null;
            m_AssetByName = null;
            m_Manifest = null;
            m_Initialized = false;
            m_ActiveBundleLoads = 0;
        }

        // ===== Internal =====

        private void ScheduleBundleUnloadIfUnused(BundleHandle handle)
        {
            if (handle == null || handle.RefCount > 0) return;
            if (m_Budget.IsEnabled)
            {
                // 预算模式：进入温缓存，只在超预算时按 LRU 淘汰（可能淘汰的是别的更久未用的 bundle）。
                m_Budget.OnUnreferenced(handle.Info.Name);
                EnforceBudget();
                return;
            }

            if (m_BundleUnloadDelay <= 0f) { UnloadBundleNow(handle); return; }
            m_Coroutine.Run(DelayedUnloadRoutine(handle, m_BundleUnloadDelay), CoroutineTag, this);
        }

        private IEnumerator DelayedUnloadRoutine(BundleHandle handle, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (handle.RefCount > 0) yield break;
            UnloadBundleNow(handle);
        }

        private void UnloadBundleNow(BundleHandle handle)
        {
            if (handle == null) return;
            string name = handle.Info.Name;
            if (!m_LoadedBundles.Remove(name)) return;
            m_Budget.OnUnloaded(name);
            try { if (handle.Bundle != null) handle.Bundle.Unload(false); }
            catch (Exception ex) { FrameworkLog.Error("Bundle.Unload threw for '{0}': {1}", name, ex); }
            FrameworkLog.Debug("Unloaded bundle: {0}", name);
        }

        private static void Fail(LoadAssetCallbacks cb, string assetName, LoadResourceStatus status, string msg, object userData)
        {
            FrameworkLog.Warning("AssetBundleLoader fail: asset='{0}' status={1} msg={2}", assetName, status, msg);
            var f = cb != null ? cb.LoadAssetFailureCallback : null;
            if (f != null)
            {
                try { f(assetName, status, msg, userData); }
                catch (Exception ex) { FrameworkLog.Error("LoadAssetFailureCallback threw for '{0}': {1}", assetName, ex); }
            }
        }

        // ===== 内部数据结构 =====

        internal sealed class BundleHandle
        {
            public BundleInfo Info { get; private set; }
            public AssetBundle Bundle { get; private set; }
            public int RefCount { get; private set; }
            private readonly AssetBundleLoader m_Owner;
            public BundleHandle(BundleInfo info, AssetBundle bundle, AssetBundleLoader owner)
            { Info = info; Bundle = bundle; m_Owner = owner; }
            public void AddRef()
            {
                RefCount++;
                if (RefCount == 1) m_Owner.m_Budget.OnReferenced(Info.Name);
            }

            public void RemoveRef() { RefCount = Math.Max(0, RefCount - 1); }
        }

        private sealed class BundleLoadingTask
        {
            public string Name;
            public bool IsDone;
            public bool Failed;     // 加载失败（Result 为 null）；并发等待者据此向上传播 DependencyError
            public BundleHandle Result;
        }

        private sealed class AssetRecord
        {
            public UnityEngine.Object Asset;
            public string AssetName;
            public BundleHandle OwnerBundle;
            // 本资源加载时引用到的完整 bundle 集合（owner + 依赖闭包，已去重）。
            // 卸载时按同一集合镜像 RemoveRef，保证引用计数严格对称。
            public List<BundleHandle> RefBundles;
            public int RefCount;
        }

        private sealed class AssetLoadingTask
        {
            public string AssetName { get; }
            public AssetInfo AssetInfo { get; }
            public Type PrimaryAssetType { get; }
            // 合并字典键 (名称, 类型)：getter-only，构造后不可变，保证 m_LoadingAssets 的 Add 与 Remove 严格对称。
            public (string Name, Type Type) Key { get; }
            public int SubscriberCount { get { return m_Subscribers.Count; } }
            private readonly List<Subscriber> m_Subscribers = new List<Subscriber>();

            public AssetLoadingTask(string name, AssetInfo info, Type type, int priority, LoadAssetCallbacks cb, object userData)
            {
                AssetName = name; AssetInfo = info; PrimaryAssetType = type; Key = (name, type);
                m_Subscribers.Add(new Subscriber { Callbacks = cb, UserData = userData, Priority = priority, ExpectedType = type });
            }

            public void Subscribe(Type type, int priority, LoadAssetCallbacks cb, object userData)
            {
                // 合并键为 (assetName, assetType)，故并入此任务的订阅者与本任务类型一致（type == PrimaryAssetType）。
                // ExpectedType 仍逐订阅者保留，SucceedAll 内做一次防御性类型校验，应对实际加载到的资源类型与请求不符的异常。
                m_Subscribers.Add(new Subscriber { Callbacks = cb, UserData = userData, Priority = priority, ExpectedType = type });
            }

            public void NotifyProgress(float p)
            {
                for (int i = 0; i < m_Subscribers.Count; i++)
                {
                    var s = m_Subscribers[i];
                    var cb = s.Callbacks != null ? s.Callbacks.LoadAssetUpdateCallback : null;
                    if (cb != null) try { cb(AssetName, p, s.UserData); } catch (Exception ex) { FrameworkLog.Error("LoadAssetUpdateCallback threw: {0}", ex); }
                }
            }

            /// <summary>
            /// 向全部订阅者投递加载结果，并对每个订阅者做独立的期望类型校验。
            /// 返回真正成功投递（类型匹配）的订阅者数，供调用方按此数累加资源引用计数，避免类型不符者泄漏引用。
            /// </summary>
            public int SucceedAll(UnityEngine.Object asset, float duration)
            {
                int succeeded = 0;
                for (int i = 0; i < m_Subscribers.Count; i++)
                {
                    var s = m_Subscribers[i];
                    // 逐订阅者类型校验：合并请求只加载一次，但每个订阅者期望的类型可能不同。
                    // 若实际资源不满足某订阅者的期望类型，对该订阅者投递 TypeError 失败，而非静默投递错误类型。
                    if (s.ExpectedType != null && asset != null && !s.ExpectedType.IsInstanceOfType(asset))
                    {
                        var fcb = s.Callbacks != null ? s.Callbacks.LoadAssetFailureCallback : null;
                        if (fcb != null)
                        {
                            string msg = Utility.Text.Format("Asset '{0}' is {1}, expected {2}.",
                                AssetName, asset.GetType().FullName, s.ExpectedType.FullName);
                            try { fcb(AssetName, LoadResourceStatus.TypeError, msg, s.UserData); }
                            catch (Exception ex) { FrameworkLog.Error("LoadAssetFailureCallback threw: {0}", ex); }
                        }
                        continue;
                    }
                    succeeded++;
                    var cb = s.Callbacks != null ? s.Callbacks.LoadAssetSuccessCallback : null;
                    if (cb != null) try { cb(AssetName, asset, duration, s.UserData); } catch (Exception ex) { FrameworkLog.Error("LoadAssetSuccessCallback threw: {0}", ex); }
                }
                return succeeded;
            }

            public void FailAll(LoadResourceStatus status, string msg)
            {
                for (int i = 0; i < m_Subscribers.Count; i++)
                {
                    var s = m_Subscribers[i];
                    var cb = s.Callbacks != null ? s.Callbacks.LoadAssetFailureCallback : null;
                    if (cb != null) try { cb(AssetName, status, msg, s.UserData); } catch (Exception ex) { FrameworkLog.Error("LoadAssetFailureCallback threw: {0}", ex); }
                }
            }

            private struct Subscriber { public LoadAssetCallbacks Callbacks; public object UserData; public int Priority; public Type ExpectedType; }
        }
    }
}
