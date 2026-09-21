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
    /// 实现被池在实例化时缓存一次，之后每次取出/归还零分配。
    /// </summary>
    public interface ISpawnCallback
    {
        /// <summary>实例被取出（激活）时触发。用于重置运行期状态、播放出生表现等。</summary>
        void OnSpawn();

        /// <summary>实例被归还（停用）时触发。用于停止协程、清理引用、复位 Transform 等。</summary>
        void OnDespawn();
    }

    /// <summary>
    /// GameObject 生成池组件。
    ///
    /// 主路径（热路径，零字符串哈希、零分配）：
    /// <code>
    /// SpawnPoolEntry bullets = GameEntry.SpawnPool.GetOrCreateEntry("Prefabs/Bullet");
    /// GameEntry.SpawnPool.Warmup(bullets, 64);              // 加载界面预热（异步加载 + 实例化）
    /// GameObject go = GameEntry.SpawnPool.Spawn(bullets, parent);
    /// GameEntry.SpawnPool.Despawn(go);                       // 经实例上的 SpawnPoolInstance 直接定位条目
    /// </code>
    /// 字符串重载（<see cref="Spawn(string, Transform)"/> 等）为便利入口，每次多一次字典查找。
    ///
    /// 设计要点：
    ///   • 每个池键一个 <see cref="SpawnPoolEntry"/>：空闲列表（LIFO）、prefab、指标、MaxIdle/ExpireTime。
    ///   • 每个实例根上挂 <see cref="SpawnPoolInstance"/>：缓存回调数组与状态，Despawn 一次 GetComponent 定位，
    ///     外部直接 Destroy 时 OnDestroy 回报条目，计数永远真实。
    ///   • 隐藏根 "__SpawnPool"（常驻 inactive）：归还实例挂在其下。
    ///   • prefab 来源：ResourceManager 按资源路径异步加载（首次），或 <see cref="RegisterPrefab"/> 直接登记。
    ///   • 周期清扫（<see cref="SweepInterval"/>）销毁超过 ExpireTime 的空闲实例。
    ///
    /// 与 ObjectPool 的语义对齐：Prewarm/Trim/Metrics/Capacity(=MaxIdle)/ExpireTime 同名同义，
    /// 诊断面板共用 <see cref="Core.ObjectPool.ObjectPoolMetrics"/>。
    ///
    /// 线程约束：全部 API 仅主线程。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/SpawnPool")]
    public sealed class SpawnPoolComponent : GameFrameworkComponent
    {
        [SerializeField]
        [Tooltip("空闲实例过期清扫的间隔秒数（unscaled）。")]
        private float m_SweepInterval = 1f;

        private readonly Dictionary<string, SpawnPoolEntry> m_Entries =
            new Dictionary<string, SpawnPoolEntry>(StringComparer.Ordinal);

        private IResourceManager m_ResourceManager;
        private Transform m_PoolRoot;
        private float m_SweepTimer;

        protected override void Awake()
        {
            base.Awake();

            // 资源管理器可选：未注册时只有 RegisterPrefab 路径可用，按路径加载会明确报错。
            m_ResourceManager = Framework.HasModule<IResourceManager>() ? Framework.GetModule<IResourceManager>() : null;

            var rootGo = new GameObject("__SpawnPool");
            rootGo.transform.SetParent(transform, false);
            rootGo.SetActive(false);
            m_PoolRoot = rootGo.transform;
        }

        private void Update()
        {
            m_SweepTimer += Time.unscaledDeltaTime;
            if (m_SweepTimer < m_SweepInterval)
            {
                return;
            }

            m_SweepTimer = 0f;
            float now = Time.unscaledTime;
            foreach (KeyValuePair<string, SpawnPoolEntry> kv in m_Entries)
            {
                kv.Value.Sweep(now);
            }
        }

        protected override void OnDestroy()
        {
            try
            {
                foreach (KeyValuePair<string, SpawnPoolEntry> kv in m_Entries)
                {
                    try { kv.Value.Shutdown(); }
                    catch (Exception ex) { Log.Error("SpawnPool '{0}'：Shutdown 抛出异常：{1}", kv.Key, ex); }
                }

                m_Entries.Clear();
            }
            finally
            {
                base.OnDestroy();
            }
        }

        // ================================================================
        //  条目
        // ================================================================

        /// <summary>过期清扫间隔（秒，unscaled）。</summary>
        public float SweepInterval
        {
            get { return m_SweepInterval; }
            set { m_SweepInterval = value < 0f ? 0f : value; }
        }

        /// <summary>条目数量。</summary>
        public int EntryCount
        {
            get { return m_Entries.Count; }
        }

        /// <summary>取条目；不存在返回 null。</summary>
        public SpawnPoolEntry GetEntry(string key)
        {
            SpawnPoolEntry entry;
            return !string.IsNullOrEmpty(key) && m_Entries.TryGetValue(key, out entry) ? entry : null;
        }

        /// <summary>取或创建条目（创建不触发加载）。业务侧缓存返回值用于热路径。</summary>
        public SpawnPoolEntry GetOrCreateEntry(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new FrameworkException("SpawnPool：key 不能为空。");
            }

            SpawnPoolEntry entry;
            if (!m_Entries.TryGetValue(key, out entry))
            {
                entry = new SpawnPoolEntry(this, key);
                m_Entries.Add(key, entry);
            }

            return entry;
        }

        /// <summary>获取全部条目（非分配版本：写入调用方列表，列表先被清空）。</summary>
        public void GetAllEntries(List<SpawnPoolEntry> results)
        {
            if (results == null)
            {
                throw new FrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<string, SpawnPoolEntry> kv in m_Entries)
            {
                results.Add(kv.Value);
            }
        }

        /// <summary>直接登记 prefab（不经 ResourceManager）。适用于业务已持有引用（ScriptableObject/Inspector）的情形与测试。</summary>
        public SpawnPoolEntry RegisterPrefab(string key, GameObject prefab)
        {
            SpawnPoolEntry entry = GetOrCreateEntry(key);
            entry.RegisterPrefab(prefab);
            return entry;
        }

        // ================================================================
        //  取出 / 归还（热路径）
        // ================================================================

        /// <summary>
        /// 同步取出：复用空闲实例，否则按已就绪的 prefab 实例化；prefab 未就绪返回 null 并告警
        /// （请先 Warmup / RegisterPrefab，或用异步重载触发首次加载）。
        /// </summary>
        public GameObject Spawn(SpawnPoolEntry entry, Transform parent = null)
        {
            if (entry == null)
            {
                throw new FrameworkException("SpawnPool：entry 不能为 null。");
            }

            GameObject go = entry.Spawn(parent);
            if (go == null)
            {
                Log.Warning("SpawnPool '{0}'：prefab 未就绪，Spawn 返回 null。请先 Warmup/RegisterPrefab，或改用异步 Spawn 重载触发首次加载。", entry.Key);
            }

            return go;
        }

        /// <summary>同步取出（字符串便利入口，多一次字典查找）。键不存在时视为未就绪。</summary>
        public GameObject Spawn(string assetName, Transform parent = null)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("SpawnPoolComponent.Spawn: assetName is invalid.");
                return null;
            }

            return Spawn(GetOrCreateEntry(assetName), parent);
        }

        /// <summary>
        /// 异步取出：prefab 就绪时同步取出并回调；否则发起首次加载，完成后实例化并回调；失败/取消回调 null。
        /// 仅首次加载路径会为回调分配闭包。
        /// </summary>
        public void Spawn(string assetName, Action<GameObject> onSpawned, Transform parent = null)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("SpawnPoolComponent.Spawn(async): assetName is invalid.");
                if (onSpawned != null) onSpawned(null);
                return;
            }

            SpawnPoolEntry entry = GetOrCreateEntry(assetName);
            if (entry.IsReady || entry.IdleCount > 0)
            {
                GameObject go = entry.Spawn(parent);
                if (onSpawned != null) onSpawned(go);
                return;
            }

            entry.EnsureLoaded(prefab =>
            {
                GameObject go = prefab != null ? entry.Spawn(parent) : null;
                if (onSpawned != null) onSpawned(go);
            });
        }

        /// <summary>
        /// 归还实例：经实例上的 <see cref="SpawnPoolInstance"/> 定位条目（无字典查找）。
        /// 非池化实例告警并直接销毁（容错，避免错误归还污染池）。
        /// </summary>
        public void Despawn(GameObject go)
        {
            if (go == null)
            {
                Log.Warning("SpawnPoolComponent.Despawn: GameObject is null.");
                return;
            }

            SpawnPoolInstance inst = go.GetComponent<SpawnPoolInstance>();
            if (inst == null || inst.Entry == null)
            {
                Log.Warning("SpawnPoolComponent.Despawn: '{0}' 不是本池的实例，直接销毁。", go.name);
                UnityEngine.Object.Destroy(go);
                return;
            }

            inst.Entry.Despawn(inst);
        }

        // ================================================================
        //  预热 / 收缩 / 清空
        // ================================================================

        /// <summary>同步预热（prefab 必须已就绪）：实例化到至少 count 个。返回本次创建数。</summary>
        public int Prewarm(SpawnPoolEntry entry, int count)
        {
            if (entry == null)
            {
                throw new FrameworkException("SpawnPool：entry 不能为 null。");
            }

            return entry.Prewarm(count);
        }

        /// <summary>异步预热：加载 prefab（如需要）后实例化 count 个空闲实例。完成回调成功失败均触发。</summary>
        public void Warmup(SpawnPoolEntry entry, int count, Action onComplete = null)
        {
            if (entry == null)
            {
                throw new FrameworkException("SpawnPool：entry 不能为 null。");
            }

            entry.EnsureLoaded(prefab =>
            {
                if (prefab != null)
                {
                    try { entry.Prewarm(count); }
                    catch (Exception ex) { Log.Error("SpawnPool '{0}'：预热失败：{1}", entry.Key, ex); }
                }

                if (onComplete != null) onComplete();
            });
        }

        /// <summary>异步预热（字符串便利入口）。</summary>
        public void Warmup(string assetName, int count, Action onComplete = null)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                Log.Error("SpawnPoolComponent.Warmup: assetName is invalid.");
                if (onComplete != null) onComplete();
                return;
            }

            Warmup(GetOrCreateEntry(assetName), count, onComplete);
        }

        /// <summary>收缩单个条目：销毁最久空闲的实例直到空闲数不超过 keepIdle。返回销毁数。</summary>
        public int Trim(SpawnPoolEntry entry, int keepIdle)
        {
            if (entry == null)
            {
                throw new FrameworkException("SpawnPool：entry 不能为 null。");
            }

            return entry.Trim(keepIdle);
        }

        /// <summary>收缩所有条目（关卡切换、内存告警时机）。返回销毁总数。</summary>
        public int TrimAll(int keepIdlePerEntry)
        {
            int total = 0;
            foreach (KeyValuePair<string, SpawnPoolEntry> kv in m_Entries)
            {
                total += kv.Value.Trim(keepIdlePerEntry);
            }

            return total;
        }

        /// <summary>
        /// 清空指定键：销毁该键所有空闲实例并释放 prefab/资产。条目保留（业务缓存的引用仍有效），
        /// 在外实例归还时将被销毁而非入池；再次 Warmup/RegisterPrefab 后恢复复用。
        /// </summary>
        public void Clear(string assetName)
        {
            SpawnPoolEntry entry = GetEntry(assetName);
            if (entry != null)
            {
                entry.Clear();
            }
        }

        /// <summary>清空所有键（语义同 <see cref="Clear"/>）。</summary>
        public void ClearAll()
        {
            foreach (KeyValuePair<string, SpawnPoolEntry> kv in m_Entries)
            {
                kv.Value.Clear();
            }
        }

        // ================================================================
        //  内部（供 SpawnPoolEntry 使用）
        // ================================================================

        internal Transform PoolRoot
        {
            get { return m_PoolRoot; }
        }

        internal IResourceManager ResourceManager
        {
            get
            {
                if (m_ResourceManager == null && Framework.HasModule<IResourceManager>())
                {
                    m_ResourceManager = Framework.GetModule<IResourceManager>();
                }

                return m_ResourceManager;
            }
        }
    }
}
