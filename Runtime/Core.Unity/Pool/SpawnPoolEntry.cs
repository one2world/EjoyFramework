//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.ObjectPool;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 一个池键（prefab）对应的池条目。业务侧热路径应缓存本对象，
    /// 用 <see cref="SpawnPoolComponent.Spawn(SpawnPoolEntry, Transform)"/> 取实例——不再有字符串哈希。
    ///
    /// 语义与 <see cref="IObjectPool{T}"/> 对齐：预热 / 容量（<see cref="MaxIdle"/>）/ 过期（<see cref="ExpireTime"/>）
    /// / 收缩 / 指标（复用 <see cref="ObjectPoolMetrics"/>，诊断面板统一格式）。
    ///
    /// 空闲列表按 LIFO 复用：最近归还的最先被取出（缓存更热），最久空闲的沉在列表头部，
    /// 过期与收缩都从头部开始，无需排序。
    ///
    /// 线程约束：仅主线程。
    /// </summary>
    public sealed class SpawnPoolEntry
    {
        private readonly SpawnPoolComponent m_Owner;
        private readonly string m_Key;
        private readonly List<SpawnPoolInstance> m_Idle = new List<SpawnPoolInstance>();
        private List<Action<GameObject>> m_Waiters = new List<Action<GameObject>>();
        private List<Action<GameObject>> m_WaitersSwap = new List<Action<GameObject>>();
        private readonly Action<IAssetLoadHandle> m_OnLoaded;

        private GameObject m_Prefab;
        private object m_Asset;
        private bool m_Loading;
        private bool m_Destroying;
        private int m_MaxIdle = int.MaxValue;
        private float m_ExpireTime = float.MaxValue;

        private int m_SpawnedCount;
        private int m_PeakCount;
        private long m_TotalSpawn;
        private long m_TotalDespawn;
        private long m_TotalDestroyed;
        private long m_TotalInstantiated;
        private long m_SpawnMiss;

        internal SpawnPoolEntry(SpawnPoolComponent owner, string key)
        {
            m_Owner = owner;
            m_Key = key;
            // 实例方法转委托每次都分配；缓存一次，每次首加载零分配。
            m_OnLoaded = OnPrefabLoaded;
        }

        // ================================================================
        //  公开只读面
        // ================================================================

        /// <summary>池键（资源路径或 RegisterPrefab 指定的名字）。</summary>
        public string Key
        {
            get { return m_Key; }
        }

        /// <summary>已缓存的 prefab；未加载/已清空时为 null。</summary>
        public GameObject Prefab
        {
            get { return m_Prefab; }
        }

        /// <summary>prefab 是否就绪（可同步 Spawn）。</summary>
        public bool IsReady
        {
            get { return m_Prefab != null; }
        }

        /// <summary>是否有进行中的首次加载。</summary>
        public bool IsLoading
        {
            get { return m_Loading; }
        }

        /// <summary>在用 + 空闲实例总数。</summary>
        public int Count
        {
            get { return m_SpawnedCount + m_Idle.Count; }
        }

        /// <summary>在用实例数。</summary>
        public int SpawnedCount
        {
            get { return m_SpawnedCount; }
        }

        /// <summary>空闲实例数。</summary>
        public int IdleCount
        {
            get { return m_Idle.Count; }
        }

        /// <summary>历史最大实例总数。</summary>
        public int PeakCount
        {
            get { return m_PeakCount; }
        }

        /// <summary>
        /// 空闲实例上限：归还时空闲数已达上限则直接销毁而不入池。默认无上限。
        /// 调小时立即收缩到新上限。
        /// </summary>
        public int MaxIdle
        {
            get { return m_MaxIdle; }
            set
            {
                if (value < 0)
                {
                    throw new FrameworkException("MaxIdle is invalid.");
                }

                m_MaxIdle = value;
                Trim(value);
            }
        }

        /// <summary>空闲实例保留秒数（unscaled time）；超过则在周期清扫中销毁。默认永不过期。</summary>
        public float ExpireTime
        {
            get { return m_ExpireTime; }
            set
            {
                if (value < 0f)
                {
                    throw new FrameworkException("ExpireTime is invalid.");
                }

                m_ExpireTime = value;
            }
        }

        /// <summary>零分配读取运行指标（与 ObjectPool 同一结构：Capacity = MaxIdle，TotalRelease = 销毁数，TotalUnspawn = 归还数）。</summary>
        public void GetMetrics(out ObjectPoolMetrics metrics)
        {
            metrics = new ObjectPoolMetrics(
                Count, m_SpawnedCount, m_PeakCount,
                m_TotalSpawn, m_TotalDespawn, m_TotalDestroyed, m_SpawnMiss,
                m_MaxIdle, m_ExpireTime);
        }

        /// <summary>累计 Instantiate 次数（= 真正付出实例化开销的次数）。</summary>
        public long TotalInstantiatedCount
        {
            get { return m_TotalInstantiated; }
        }

        // ================================================================
        //  取出 / 归还
        // ================================================================

        /// <summary>
        /// 取出一个实例：优先复用空闲实例，否则按 prefab 实例化；prefab 未就绪返回 null（计一次 miss）。
        /// </summary>
        internal GameObject Spawn(Transform parent)
        {
            SpawnPoolInstance inst = TakeIdle();
            if (inst == null)
            {
                if (m_Prefab == null)
                {
                    m_SpawnMiss++;
                    return null;
                }

                inst = Instantiate();
            }

            Transform t = inst.transform;
            t.SetParent(parent, false);
            inst.IsSpawned = true;
            m_SpawnedCount++;
            m_TotalSpawn++;
            if (Count > m_PeakCount)
            {
                m_PeakCount = Count;
            }

            inst.gameObject.SetActive(true);
            InvokeCallbacks(inst, true);
            return inst.gameObject;
        }

        /// <summary>
        /// 归还实例。重复归还只告警不动状态（业务 bug，不能用"销毁"放大它）；
        /// prefab 已被 Clear 或空闲已达上限时直接销毁。
        /// </summary>
        internal void Despawn(SpawnPoolInstance inst)
        {
            if (!inst.IsSpawned)
            {
                Log.Warning("SpawnPool '{0}'：实例 '{1}' 被重复 Despawn（当前已在空闲队列）。每次 Spawn 只能配对一次 Despawn。", m_Key, inst.name);
                return;
            }

            InvokeCallbacks(inst, false);
            inst.IsSpawned = false;
            m_SpawnedCount--;
            m_TotalDespawn++;

            if (m_Prefab == null || m_Idle.Count >= m_MaxIdle)
            {
                DestroyInstance(inst);
                return;
            }

            GameObject go = inst.gameObject;
            go.SetActive(false);
            go.transform.SetParent(m_Owner.PoolRoot, false);
            inst.IdleSince = Time.unscaledTime;
            m_Idle.Add(inst);
        }

        // ================================================================
        //  预热 / 收缩 / 清扫
        // ================================================================

        /// <summary>预热到至少 <paramref name="count"/> 个实例（含在用）。prefab 必须已就绪。</summary>
        internal int Prewarm(int count)
        {
            if (count < 0)
            {
                throw new FrameworkException("Prewarm count is invalid.");
            }

            if (m_Prefab == null)
            {
                throw new FrameworkException(Utility.Text.Format("SpawnPool '{0}'：prefab 未就绪，不能同步预热。请先 RegisterPrefab 或用 Warmup(assetName, count) 异步预热。", m_Key));
            }

            int idleTarget = count - m_SpawnedCount;
            if (idleTarget > m_MaxIdle)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "SpawnPool '{0}'：预热需要 {1} 个空闲实例，超过 MaxIdle {2}——超出部分会在归还时被销毁，预热等于白做。请先调大 MaxIdle。",
                    m_Key, idleTarget, m_MaxIdle));
            }

            int created = 0;
            while (Count < count)
            {
                SpawnPoolInstance inst = Instantiate();
                inst.gameObject.SetActive(false);
                inst.transform.SetParent(m_Owner.PoolRoot, false);
                inst.IdleSince = Time.unscaledTime;
                m_Idle.Add(inst);
                created++;
            }

            if (Count > m_PeakCount)
            {
                m_PeakCount = Count;
            }

            return created;
        }

        /// <summary>销毁最久空闲的实例，直到空闲数不超过 <paramref name="keepIdle"/>。返回销毁数。</summary>
        internal int Trim(int keepIdle)
        {
            if (keepIdle < 0)
            {
                throw new FrameworkException("Keep count is invalid.");
            }

            int toDestroy = m_Idle.Count - keepIdle;
            if (toDestroy <= 0)
            {
                return 0;
            }

            DestroyIdlePrefix(toDestroy);
            return toDestroy;
        }

        /// <summary>周期清扫：销毁空闲超过 ExpireTime 的实例（空闲列表头部即最久空闲）。</summary>
        internal void Sweep(float now)
        {
            if (m_ExpireTime >= float.MaxValue || m_Idle.Count == 0)
            {
                return;
            }

            int expired = 0;
            while (expired < m_Idle.Count && now - m_Idle[expired].IdleSince > m_ExpireTime)
            {
                expired++;
            }

            if (expired > 0)
            {
                DestroyIdlePrefix(expired);
            }
        }

        /// <summary>清空：销毁全部空闲实例，释放 prefab 与底层资产；在外实例归还时将被销毁而非入池。</summary>
        internal void Clear()
        {
            DestroyIdlePrefix(m_Idle.Count);
            ReleasePrefab();
        }

        /// <summary>组件销毁：清空后标记，之后实例 OnDestroy 的回调不再改动状态。</summary>
        internal void Shutdown()
        {
            m_Destroying = true;
            Clear();
            m_Waiters.Clear();
            m_WaitersSwap.Clear();
        }

        // ================================================================
        //  prefab 来源
        // ================================================================

        /// <summary>直接登记 prefab（不经 ResourceManager，Clear 时也不会去卸载资产）。</summary>
        internal void RegisterPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new FrameworkException(Utility.Text.Format("SpawnPool '{0}'：RegisterPrefab 的 prefab 不能为 null。", m_Key));
            }

            ReleasePrefab();
            m_Prefab = prefab;
            m_Asset = null;
        }

        /// <summary>
        /// 确保 prefab 已加载：就绪立即回调；加载中合并等待；否则经 ResourceManager 发起首次加载。
        /// 失败/取消回调 null。
        /// </summary>
        internal void EnsureLoaded(Action<GameObject> onLoaded)
        {
            if (m_Prefab != null)
            {
                onLoaded(m_Prefab);
                return;
            }

            m_Waiters.Add(onLoaded);
            if (m_Loading)
            {
                return;
            }

            IResourceManager rm = m_Owner.ResourceManager;
            if (rm == null)
            {
                Log.Error("SpawnPool '{0}'：ResourceManager 未注册，无法按资源路径加载；请改用 RegisterPrefab。", m_Key);
                FireWaiters(null);
                return;
            }

            m_Loading = true;
            IAssetLoadHandle handle = rm.LoadAssetWithHandle(m_Key, 0, null);
            handle.Completed += m_OnLoaded;
        }

        private void OnPrefabLoaded(IAssetLoadHandle handle)
        {
            m_Loading = false;
            if (m_Destroying)
            {
                if (handle.Status == LoadAssetStatus.Done && handle.Asset != null && m_Owner.ResourceManager != null)
                {
                    m_Owner.ResourceManager.UnloadAsset(handle.Asset);
                }

                return;
            }

            GameObject prefab = null;
            if (handle.Status == LoadAssetStatus.Done)
            {
                prefab = handle.Asset as GameObject;
                if (prefab == null)
                {
                    Log.Error("SpawnPool '{0}'：资源已加载但不是 GameObject prefab。", m_Key);
                    if (handle.Asset != null && m_Owner.ResourceManager != null)
                    {
                        m_Owner.ResourceManager.UnloadAsset(handle.Asset);
                    }
                }
                else if (m_Prefab != null)
                {
                    // 加载期间已被 RegisterPrefab：本次到达的资产引用立即交还，避免泄漏。
                    if (m_Owner.ResourceManager != null)
                    {
                        m_Owner.ResourceManager.UnloadAsset(handle.Asset);
                    }

                    prefab = m_Prefab;
                }
                else
                {
                    m_Prefab = prefab;
                    m_Asset = handle.Asset;
                }
            }
            else if (handle.Status == LoadAssetStatus.Failed)
            {
                Log.Error("SpawnPool '{0}'：加载失败：{1} {2}", m_Key, handle.FailureStatus, handle.ErrorMessage);
            }

            FireWaiters(prefab);
        }

        private void FireWaiters(GameObject prefab)
        {
            // 交换双列表再回调：等待者里可能再次触发加载（失败重试）并往新的 m_Waiters 追加；
            // 列表按条目持有，跨条目的同步完成嵌套互不干扰。
            List<Action<GameObject>> pending = m_Waiters;
            m_Waiters = m_WaitersSwap;
            m_WaitersSwap = pending;
            for (int i = 0; i < pending.Count; i++)
            {
                try { pending[i](prefab); }
                catch (Exception ex) { Log.Error("SpawnPool '{0}'：加载回调抛出异常：{1}", m_Key, ex); }
            }

            pending.Clear();
        }

        private void ReleasePrefab()
        {
            m_Prefab = null;
            object asset = m_Asset;
            m_Asset = null;
            if (asset != null && m_Owner.ResourceManager != null)
            {
                try { m_Owner.ResourceManager.UnloadAsset(asset); }
                catch (Exception ex) { Log.Error("SpawnPool '{0}'：UnloadAsset 抛出异常：{1}", m_Key, ex); }
            }
        }

        // ================================================================
        //  实例生命周期
        // ================================================================

        private SpawnPoolInstance TakeIdle()
        {
            int last = m_Idle.Count - 1;
            if (last < 0)
            {
                return null;
            }

            SpawnPoolInstance inst = m_Idle[last];
            m_Idle.RemoveAt(last);
            return inst;
        }

        private SpawnPoolInstance Instantiate()
        {
            GameObject go = UnityEngine.Object.Instantiate(m_Prefab, m_Owner.PoolRoot);
            SpawnPoolInstance inst = go.GetComponent<SpawnPoolInstance>();
            if (inst == null)
            {
                inst = go.AddComponent<SpawnPoolInstance>();
            }

            inst.Entry = this;
            inst.IsSpawned = false;
            ISpawnCallback[] callbacks = go.GetComponentsInChildren<ISpawnCallback>(true);
            inst.Callbacks = callbacks.Length > 0 ? callbacks : SpawnPoolInstance.EmptyCallbacks;
            m_TotalInstantiated++;
            return inst;
        }

        private void DestroyIdlePrefix(int count)
        {
            if (count <= 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                SpawnPoolInstance inst = m_Idle[i];
                if (inst != null)
                {
                    inst.Entry = null;   // 先解绑，Destroy 触发的 OnDestroy 不再回调本条目
                    m_TotalDestroyed++;
                    UnityEngine.Object.Destroy(inst.gameObject);
                }
            }

            m_Idle.RemoveRange(0, count);
        }

        private void DestroyInstance(SpawnPoolInstance inst)
        {
            inst.Entry = null;
            m_TotalDestroyed++;
            UnityEngine.Object.Destroy(inst.gameObject);
        }

        /// <summary>实例被外部直接 Destroy：从计数/空闲列表摘除，保持指标真实。</summary>
        internal void OnInstanceDestroyed(SpawnPoolInstance inst)
        {
            if (m_Destroying)
            {
                return;
            }

            m_TotalDestroyed++;
            if (inst.IsSpawned)
            {
                m_SpawnedCount--;
                return;
            }

            int index = m_Idle.IndexOf(inst);
            if (index >= 0)
            {
                m_Idle.RemoveAt(index);
            }
        }

        private void InvokeCallbacks(SpawnPoolInstance inst, bool spawn)
        {
            ISpawnCallback[] callbacks = inst.Callbacks;
            for (int i = 0; i < callbacks.Length; i++)
            {
                try
                {
                    if (spawn) callbacks[i].OnSpawn();
                    else callbacks[i].OnDespawn();
                }
                catch (Exception ex)
                {
                    Log.Error("SpawnPool '{0}'：ISpawnCallback.{1} 抛出异常：{2}", m_Key, spawn ? "OnSpawn" : "OnDespawn", ex);
                }
            }
        }
    }
}
