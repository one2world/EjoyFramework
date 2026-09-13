//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// GameObject 实例可回收时实现的回调接口。
    /// 池在取出 / 归还实例时，会在实例任意一个组件上调用对应钩子，便于业务重置状态。
    /// </summary>
    public interface ISpawnCallback
    {
        /// <summary>
        /// 实例被取出（激活）时触发。用于重置运行期状态、播放出生表现等。
        /// </summary>
        void OnSpawn();

        /// <summary>
        /// 实例被归还（停用）时触发。用于停止协程、清理引用、复位 Transform 等。
        /// </summary>
        void OnDespawn();
    }

    /// <summary>
    /// GameObject 生成池组件。以 assetName 作为池键，复用 prefab 实例化出来的 GameObject，
    /// 减少频繁 Instantiate/Destroy 带来的 GC 与卡顿。
    ///
    /// 设计要点：
    ///   - 池键：assetName（业务资源路径）。每个键独立一个空闲队列（m_Idle[key]）。
    ///   - 隐藏根：在本组件 GameObject 下创建一个常驻 inactive 的子物体 "__SpawnPool"，
    ///     归还的实例 reparent 到此根下，既保证 SetActive(false) 又避免污染场景层级。
    ///   - prefab 缓存：首次异步加载完成的 prefab（与其底层 asset 引用）缓存在 m_PrefabCache，
    ///     后续同键 Spawn 走同步 Instantiate，无需再次加载。
    ///   - 追踪表：m_SpawnedToKey 记录"已取出实例 → 池键"，用于校验 Despawn 合法性并定位回收队列。
    ///
    /// 线程约束：本组件全部 API 仅可在 Unity 主线程调用（内部使用 Instantiate/SetActive/Transform 等 Unity API）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/SpawnPool")]
    public sealed class SpawnPoolComponent : GameFrameworkComponent
    {
        // 池键 → 空闲（已归还）实例队列，等待复用。
        private readonly Dictionary<string, Queue<GameObject>> m_Idle =
            new Dictionary<string, Queue<GameObject>>(StringComparer.Ordinal);

        // 已取出实例 → 池键。用于校验 Despawn 合法性 + 定位归还队列。
        // 注意：若调用方直接 Destroy 了未归还的实例，键会变成 Unity "伪 null"，
        // 需经 PruneDeadSpawned 定期清扫，避免条目泄漏（与空闲路径的 go==null 守卫保持一致）。
        private readonly Dictionary<GameObject, string> m_SpawnedToKey =
            new Dictionary<GameObject, string>();

        // 已被 Clear(key) 清空但仍有“已取出实例”在外的池键集合。
        // 这些键的 prefab 缓存与底层 asset 已释放，故其在外实例归还（Despawn）时若再入空闲队列，
        // 将污染一个 prefab 已不存在的池。命中此集合的实例在 Despawn 时改为直接 Destroy。
        // 当该键再次被成功加载并重建 prefab 缓存（OnPrefabLoadCompleted）时移出本集合，恢复正常复用。
        private readonly HashSet<string> m_ClearedKeys =
            new HashSet<string>(StringComparer.Ordinal);

        // 清扫 m_SpawnedToKey 伪 null 键时复用的临时缓冲，避免每次扫描分配新 List。
        private static readonly List<GameObject> s_PruneBuffer = new List<GameObject>();

        // 池键 → 已缓存 prefab（首次加载后缓存，供同步 Instantiate 复用）。
        private readonly Dictionary<string, GameObject> m_PrefabCache =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        // 池键 → 底层 asset 引用（ClearAll/Clear 时交还 ResourceManager 卸载，避免泄漏）。
        private readonly Dictionary<string, object> m_PrefabAssets =
            new Dictionary<string, object>(StringComparer.Ordinal);

        // 池键 → 是否有正在进行的首次加载（防止并发首加载重复实例化与重复缓存）。
        private readonly Dictionary<string, List<Action<GameObject>>> m_Loading =
            new Dictionary<string, List<Action<GameObject>>>(StringComparer.Ordinal);

        private IResourceManager m_ResourceManager;
        private Transform m_PoolRoot;

        protected override void Awake()
        {
            base.Awake();

            m_ResourceManager = Framework.GetModule<IResourceManager>();
            if (m_ResourceManager == null)
            {
                Log.Fatal("SpawnPoolComponent: Resource manager is invalid.");
            }

            // 隐藏根：常驻 inactive 子物体，归还的实例挂到这里，自然处于停用状态且不污染场景层级。
            var rootGo = new GameObject("__SpawnPool");
            rootGo.transform.SetParent(transform, false);
            rootGo.SetActive(false);
            m_PoolRoot = rootGo.transform;
        }

        /// <summary>
        /// 同步获取一个实例（主线程）。
        ///   - 命中空闲队列：复用已归还实例；
        ///   - 否则 prefab 已缓存：Object.Instantiate；
        ///   - 否则返回 null 并告警（请先调用 Warmup 预热，或用异步 Spawn 重载触发首次加载）。
        /// </summary>
        /// <param name="assetName">资源路径，作为池键。</param>
        /// <param name="parent">父节点；为 null 则置于场景根。</param>
        /// <returns>激活后的实例；首次未预热时返回 null。</returns>
        public GameObject Spawn(string assetName, Transform parent = null)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("SpawnPoolComponent.Spawn: assetName is invalid.");
                return null;
            }

            GameObject instance = TakeFromIdle(assetName);
            if (instance == null)
            {
                GameObject prefab;
                if (!m_PrefabCache.TryGetValue(assetName, out prefab) || prefab == null)
                {
                    Log.Warning("SpawnPoolComponent.Spawn: prefab '{0}' not cached. " +
                                "Call Warmup first or use the async Spawn overload to trigger the first load.", assetName);
                    return null;
                }
                instance = UnityEngine.Object.Instantiate(prefab);
            }

            ActivateInstance(assetName, instance, parent);
            return instance;
        }

        /// <summary>
        /// 异步获取一个实例（主线程发起）。
        ///   - prefab 已缓存：直接同步生成并回调；
        ///   - 否则发起首次加载（LoadAssetWithHandle），完成后缓存 prefab + 实例化 + 回调；
        ///   - 加载失败 / 取消：回调 onSpawned(null)。
        /// </summary>
        /// <param name="assetName">资源路径，作为池键。</param>
        /// <param name="onSpawned">完成回调；失败时参数为 null。</param>
        /// <param name="parent">父节点；为 null 则置于场景根。</param>
        public void Spawn(string assetName, Action<GameObject> onSpawned, Transform parent = null)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("SpawnPoolComponent.Spawn(async): assetName is invalid.");
                if (onSpawned != null) onSpawned(null);
                return;
            }

            // prefab 已缓存或有空闲实例：走同步路径。
            if (m_PrefabCache.ContainsKey(assetName) || HasIdle(assetName))
            {
                GameObject go = Spawn(assetName, parent);
                if (onSpawned != null) onSpawned(go);
                return;
            }

            EnsurePrefabLoaded(assetName, prefab =>
            {
                if (prefab == null)
                {
                    if (onSpawned != null) onSpawned(null);
                    return;
                }

                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                ActivateInstance(assetName, instance, parent);
                if (onSpawned != null) onSpawned(instance);
            });
        }

        /// <summary>
        /// 归还一个实例（主线程）。
        ///   - 非本池追踪的实例：告警并直接 Destroy（容错，避免错误归还污染池）；
        ///   - 本池实例：触发 ISpawnCallback.OnDespawn → SetActive(false) → reparent 到隐藏根 → 入队等待复用。
        /// </summary>
        public void Despawn(GameObject go)
        {
            if (go == null)
            {
                Log.Warning("SpawnPoolComponent.Despawn: GameObject is null.");
                return;
            }

            string key;
            if (!m_SpawnedToKey.TryGetValue(go, out key))
            {
                Log.Warning("SpawnPoolComponent.Despawn: '{0}' is not a tracked spawned instance; destroying it instead.", go.name);
                UnityEngine.Object.Destroy(go);
                return;
            }

            m_SpawnedToKey.Remove(go);

            // 顺带清扫被外部直接 Destroy 的未归还实例（伪 null 键），避免追踪表泄漏。
            PruneDeadSpawned();

            InvokeSpawnCallback(go, false);

            // 该键已被 Clear：其 prefab 缓存/asset 引用已释放，不能再入空闲队列复用，否则会
            // 污染一个 prefab 已不存在的池。直接销毁此实例。
            if (m_ClearedKeys.Contains(key))
            {
                UnityEngine.Object.Destroy(go);
                // 该键的最后一个在外实例归还后，集合中已无意义的占位可随同清理；
                // 但因无法在此低成本得知是否仍有其它在外实例，保留标记由下次 Clear/加载流程自然收敛。
                return;
            }

            go.SetActive(false);
            if (m_PoolRoot != null) go.transform.SetParent(m_PoolRoot, false);

            EnqueueIdle(key, go);
        }

        /// <summary>
        /// 预热：加载一次 prefab 并实例化 count 个停用实例放入池中，
        /// 之后同步 Spawn 即可零加载、零实例化命中。
        /// </summary>
        /// <param name="assetName">资源路径，作为池键。</param>
        /// <param name="count">预热实例数量（&lt;= 0 时仅缓存 prefab，不实例化）。</param>
        /// <param name="onComplete">完成回调（成功或失败均触发）。</param>
        public void Warmup(string assetName, int count, Action onComplete = null)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("SpawnPoolComponent.Warmup: assetName is invalid.");
                if (onComplete != null) onComplete();
                return;
            }

            EnsurePrefabLoaded(assetName, prefab =>
            {
                if (prefab == null)
                {
                    if (onComplete != null) onComplete();
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    GameObject instance = UnityEngine.Object.Instantiate(prefab);
                    instance.SetActive(false);
                    if (m_PoolRoot != null) instance.transform.SetParent(m_PoolRoot, false);
                    EnqueueIdle(assetName, instance);
                }

                if (onComplete != null) onComplete();
            });
        }

        /// <summary>
        /// 清空指定键的池：销毁该键所有空闲实例，并交还底层 asset 引用给 ResourceManager 卸载。
        /// 已取出（未归还）的实例本身不受影响、可继续使用；但因 prefab 缓存与 asset 引用在此被释放，
        /// 这些在外实例随后 <see cref="Despawn"/> 时不会再入空闲队列复用，而是被直接销毁
        /// （否则会污染一个 prefab 已不存在的池）。若该键随后再次被 <see cref="Warmup"/> 或异步
        /// <see cref="Spawn(string,Action{GameObject},Transform)"/> 重新加载并重建 prefab 缓存，则恢复正常复用。
        /// </summary>
        public void Clear(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return;

            Queue<GameObject> idle;
            if (m_Idle.TryGetValue(assetName, out idle))
            {
                while (idle.Count > 0)
                {
                    GameObject go = idle.Dequeue();
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
                m_Idle.Remove(assetName);
            }

            m_PrefabCache.Remove(assetName);
            ReleasePrefabAsset(assetName);

            // 标记该键为已清空：仍在外的已取出实例在归还时改为销毁而非入队，避免污染 prefab 已失效的池。
            m_ClearedKeys.Add(assetName);
        }

        /// <summary>
        /// 清空所有键的池：销毁全部空闲实例，并交还所有底层 asset 引用。
        /// 已取出（未归还）的实例不受影响。
        /// </summary>
        public void ClearAll()
        {
            foreach (var kv in m_Idle)
            {
                Queue<GameObject> idle = kv.Value;
                while (idle.Count > 0)
                {
                    GameObject go = idle.Dequeue();
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
            }
            m_Idle.Clear();
            m_PrefabCache.Clear();

            // 交还所有缓存 prefab 的底层 asset 引用。
            foreach (var kv in m_PrefabAssets)
            {
                if (m_ResourceManager != null && kv.Value != null)
                {
                    try { m_ResourceManager.UnloadAsset(kv.Value); }
                    catch (Exception ex) { Log.Error("SpawnPoolComponent.ClearAll: UnloadAsset threw: {0}", ex); }
                }
            }
            m_PrefabAssets.Clear();

            // 与 Clear(key) 同理：所有键的 prefab 缓存/asset 已释放，仍在外的已取出实例归还时
            // 必须销毁而非入队（其池的 prefab 已不存在）。将当前所有在外实例的键标记为已清空。
            foreach (var kv in m_SpawnedToKey)
            {
                if (kv.Key != null) m_ClearedKeys.Add(kv.Value);
            }

            // 清扫被外部直接 Destroy 的未归还实例（伪 null 键）；仍存活的已取出实例保持追踪不受影响。
            PruneDeadSpawned();
        }

        protected override void OnDestroy()
        {
            // base.OnDestroy()（注销自身）置于 finally：即便 ClearAll 抛出，也必须确定性注销，
            // 否则 ComponentRegistry.s_ByType 残留已销毁实例。异常仍向上传播（Unity 会记录）。
            try
            {
                ClearAll();
            }
            finally
            {
                base.OnDestroy();
            }
        }

        // ===== 内部 =====

        /// <summary>
        /// 清扫追踪表中已被外部直接 Destroy 的未归还实例（Unity "伪 null" 键），移除泄漏条目。
        /// 与空闲路径的 go==null 守卫语义一致；复用 s_PruneBuffer，避免每次扫描分配。
        /// </summary>
        private void PruneDeadSpawned()
        {
            s_PruneBuffer.Clear();
            foreach (var kv in m_SpawnedToKey)
            {
                if (kv.Key == null) s_PruneBuffer.Add(kv.Key);
            }

            for (int i = 0; i < s_PruneBuffer.Count; i++)
            {
                m_SpawnedToKey.Remove(s_PruneBuffer[i]);
            }
            s_PruneBuffer.Clear();
        }

        /// <summary>从空闲队列取出一个实例；无可用实例返回 null。</summary>
        private GameObject TakeFromIdle(string assetName)
        {
            Queue<GameObject> idle;
            if (!m_Idle.TryGetValue(assetName, out idle)) return null;

            while (idle.Count > 0)
            {
                GameObject go = idle.Dequeue();
                if (go != null) return go; // 可能已被外部 Destroy，跳过失效实例。
            }
            return null;
        }

        private bool HasIdle(string assetName)
        {
            Queue<GameObject> idle;
            return m_Idle.TryGetValue(assetName, out idle) && idle.Count > 0;
        }

        private void EnqueueIdle(string assetName, GameObject go)
        {
            Queue<GameObject> idle;
            if (!m_Idle.TryGetValue(assetName, out idle))
            {
                idle = new Queue<GameObject>();
                m_Idle.Add(assetName, idle);
            }
            idle.Enqueue(go);
        }

        /// <summary>把实例标记为已取出、reparent、激活并触发 OnSpawn 钩子。</summary>
        private void ActivateInstance(string assetName, GameObject instance, Transform parent)
        {
            instance.transform.SetParent(parent, false);
            instance.SetActive(true);
            m_SpawnedToKey[instance] = assetName;
            InvokeSpawnCallback(instance, true);
        }

        private static void InvokeSpawnCallback(GameObject go, bool spawn)
        {
            // 在实例的任意组件上查找 ISpawnCallback 实现（包含子物体），可选钩子，缺失即跳过。
            var callbacks = go.GetComponentsInChildren<ISpawnCallback>(true);
            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    if (spawn) callbacks[i].OnSpawn();
                    else callbacks[i].OnDespawn();
                }
                catch (Exception ex)
                {
                    Log.Error("SpawnPoolComponent: ISpawnCallback.{0} threw: {1}", spawn ? "OnSpawn" : "OnDespawn", ex);
                }
            }
        }

        /// <summary>
        /// 确保 prefab 已缓存：已缓存直接回调；否则发起 LoadAssetWithHandle 首次加载，
        /// 完成后缓存 prefab + asset 引用并回调。并发首加载会合并到同一句柄的回调列表。
        /// </summary>
        private void EnsurePrefabLoaded(string assetName, Action<GameObject> onLoaded)
        {
            GameObject cached;
            if (m_PrefabCache.TryGetValue(assetName, out cached) && cached != null)
            {
                onLoaded(cached);
                return;
            }

            // 已有同键加载在途：合并回调，避免重复加载与重复缓存。
            List<Action<GameObject>> waiters;
            if (m_Loading.TryGetValue(assetName, out waiters))
            {
                waiters.Add(onLoaded);
                return;
            }

            if (m_ResourceManager == null)
            {
                Log.Error("SpawnPoolComponent: Resource manager is invalid; cannot load '{0}'.", assetName);
                onLoaded(null);
                return;
            }

            waiters = new List<Action<GameObject>> { onLoaded };
            m_Loading.Add(assetName, waiters);

            IAssetLoadHandle handle = m_ResourceManager.LoadAssetWithHandle(assetName, 0, null);
            handle.Completed += h => OnPrefabLoadCompleted(assetName, h);
        }

        private void OnPrefabLoadCompleted(string assetName, IAssetLoadHandle handle)
        {
            List<Action<GameObject>> waiters;
            m_Loading.TryGetValue(assetName, out waiters);
            m_Loading.Remove(assetName);

            GameObject prefab = null;

            if (handle.Status == LoadAssetStatus.Done)
            {
                prefab = handle.Asset as GameObject;
                if (prefab != null)
                {
                    // 仅在首次成功时缓存 prefab 与底层 asset 引用（去重，避免泄漏多份引用）。
                    if (!m_PrefabCache.ContainsKey(assetName))
                    {
                        m_PrefabCache[assetName] = prefab;
                        m_PrefabAssets[assetName] = handle.Asset;
                        // prefab 缓存已重建：撤销可能存在的 Clear 标记，恢复该键 Despawn 的正常复用路径。
                        m_ClearedKeys.Remove(assetName);
                    }
                    else
                    {
                        // 竞态：另一路已缓存。本次到达的 asset 引用立即交还，避免泄漏。
                        if (m_ResourceManager != null && handle.Asset != null)
                            m_ResourceManager.UnloadAsset(handle.Asset);
                    }
                }
                else
                {
                    Log.Error("SpawnPoolComponent: asset '{0}' loaded but is not a GameObject prefab.", assetName);
                    if (m_ResourceManager != null && handle.Asset != null)
                        m_ResourceManager.UnloadAsset(handle.Asset);
                }
            }
            else if (handle.Status == LoadAssetStatus.Failed)
            {
                Log.Error("SpawnPoolComponent: load '{0}' failed: {1} {2}", assetName, handle.FailureStatus, handle.ErrorMessage);
            }
            // Cancelled：prefab 保持 null，资产由 ResourceManager 自行释放（见 IAssetLoadHandle.Cancel 文档）。

            if (waiters != null)
            {
                for (int i = 0; i < waiters.Count; i++)
                {
                    try { waiters[i](prefab); }
                    catch (Exception ex) { Log.Error("SpawnPoolComponent: prefab load callback threw: {0}", ex); }
                }
            }
        }

        private void ReleasePrefabAsset(string assetName)
        {
            object asset;
            if (m_PrefabAssets.TryGetValue(assetName, out asset))
            {
                m_PrefabAssets.Remove(assetName);
                if (m_ResourceManager != null && asset != null)
                {
                    try { m_ResourceManager.UnloadAsset(asset); }
                    catch (Exception ex) { Log.Error("SpawnPoolComponent.Clear: UnloadAsset threw: {0}", ex); }
                }
            }
        }
    }
}
