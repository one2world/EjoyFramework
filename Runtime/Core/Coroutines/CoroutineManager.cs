//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

namespace EjoyFramework.Core.Coroutines
{
    /// <summary>
    /// 协程管理器实现。维护 Id→Entry 注册表；通过 Helper 真正驱动；
    /// 协程结束时自动从注册表移除（用 try/finally 包裹用户 IEnumerator）。
    /// </summary>
    internal sealed class CoroutineManager : FrameworkModule, ICoroutineManager
    {
        private sealed class Entry
        {
            public int Id;
            public string Tag;
            public WeakReference OwnerRef;
            public string OwnerName;
            public float StartTime;
            public object HelperCookie;
        }

        private readonly Dictionary<int, Entry> m_Running = new Dictionary<int, Entry>();
        private ICoroutineHelper m_Helper;
        private int m_NextId;

        // Priority 100：协程调度必须最先 tick；其他模块的 helper（DefaultSceneHelper 等）可能依赖它。
        public override int Priority { get { return 100; } }

        // 必需配置自检：依赖外部注入的 ICoroutineHelper 才能驱动任何协程（Resource/Scene/Entity 异步加载均经此）。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return HasHelper; } }
        public override string ConfigurationHint { get { return "CoroutineManager needs ICoroutineHelper — call GameEntry.Coroutine.SetHelper(...) (or the framework's helper-injection) before use."; } }

        public override void Update(float a, float b) { }

        public override void Shutdown()
        {
            StopAll();
            m_Helper = null;
        }

        public int RunningCount { get { return m_Running.Count; } }

        public bool HasHelper
        {
            get
            {
                if (m_Helper == null) return false;
                ICoroutineHelperStatus status = m_Helper as ICoroutineHelperStatus;
                return status == null || status.IsAvailable;
            }
        }

        public void SetHelper(ICoroutineHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetHelper));
            if (helper == null) throw new FrameworkException("Coroutine helper is invalid.");
            if (ReferenceEquals(m_Helper, helper)) return;

            // Coroutine cookies belong to the helper that created them. They cannot be migrated
            // to a replacement host, so clear the old registry while the old helper is still set.
            if (m_Helper != null) StopAll();
            m_Helper = helper;
        }

        public CoroutineHandle Run(IEnumerator routine, string tag, object owner = null)
        {
            Framework.EnsureMainThread(nameof(Run));
            if (!HasHelper)
            {
                // Actionable: the most common failure mode is a scene whose GameEntry-equivalent
                // GameObject was authored before CoroutineComponent existed. Without a helper,
                // every async module (Resource / Scene / Entity bundle loads) fails at first use.
                throw new FrameworkException(
                    "Coroutine helper is not set. " +
                    "Add the 'EjoyFramework/Core/Coroutine' component to the scene GameObject that hosts the other framework Components, " +
                    "or instantiate the EjoyFramework.Core.prefab (it already includes CoroutineComponent). " +
                    "CoroutineComponent.Awake injects UnityCoroutineHelper into CoroutineManager.");
            }
            if (routine == null) throw new FrameworkException("Routine is invalid.");

            int id = ++m_NextId;
            var entry = new Entry
            {
                Id = id,
                Tag = tag ?? string.Empty,
                OwnerRef = owner != null ? new WeakReference(owner) : null,
                OwnerName = owner != null ? owner.GetType().Name : null,
                StartTime = NowSeconds(),
            };
            m_Running[id] = entry;

            try
            {
                entry.HelperCookie = m_Helper.StartCoroutine(Wrap(routine, id));
            }
            catch (Exception ex)
            {
                m_Running.Remove(id);
                FrameworkLog.Error("CoroutineHelper.StartCoroutine threw for tag '{0}': {1}", entry.Tag, ex);
                return default;
            }
            return new CoroutineHandle(id);
        }

        public bool Stop(CoroutineHandle handle)
        {
            Framework.EnsureMainThread(nameof(Stop));
            if (!handle.IsValid) return false;
            return StopById(handle.Id);
        }

        public int StopByOwner(object owner)
        {
            Framework.EnsureMainThread(nameof(StopByOwner));
            if (owner == null) return 0;
            // 收集 id 后再 stop（避免在迭代中修改字典）
            List<int> targets = null;
            foreach (var kv in m_Running)
            {
                var oRef = kv.Value.OwnerRef;
                if (oRef == null) continue;
                // 一次性读取 Target，避免 IsAlive→Target 之间被 GC 回收的 TOCTOU。
                var target = oRef.Target;
                if (target != null && ReferenceEquals(target, owner))
                {
                    (targets ??= new List<int>()).Add(kv.Key);
                }
            }
            if (targets == null) return 0;
            int n = 0;
            foreach (var id in targets) if (StopById(id)) n++;
            return n;
        }

        public int StopByTag(string tag)
        {
            Framework.EnsureMainThread(nameof(StopByTag));
            if (tag == null) return 0;
            List<int> targets = null;
            foreach (var kv in m_Running)
            {
                if (kv.Value.Tag == tag)
                {
                    (targets ??= new List<int>()).Add(kv.Key);
                }
            }
            if (targets == null) return 0;
            int n = 0;
            foreach (var id in targets) if (StopById(id)) n++;
            return n;
        }

        public int StopAll()
        {
            Framework.EnsureMainThread(nameof(StopAll));
            if (m_Running.Count == 0) return 0;
            var snapshot = new int[m_Running.Count];
            int i = 0;
            foreach (var kv in m_Running) snapshot[i++] = kv.Key;
            int n = 0;
            foreach (var id in snapshot) if (StopById(id)) n++;
            return n;
        }

        public CoroutineSnapshot[] GetAllRunning()
        {
            var arr = new CoroutineSnapshot[m_Running.Count];
            int i = 0;
            float now = NowSeconds();
            foreach (var kv in m_Running)
            {
                arr[i++] = new CoroutineSnapshot
                {
                    Id = kv.Value.Id,
                    Tag = kv.Value.Tag,
                    OwnerName = kv.Value.OwnerName,
                    ElapsedSeconds = now - kv.Value.StartTime,
                };
            }
            return arr;
        }

        // ===== 内部 =====

        private bool StopById(int id)
        {
            if (!m_Running.TryGetValue(id, out var entry)) return false;
            m_Running.Remove(id);
            if (entry.HelperCookie != null && m_Helper != null)
            {
                try { m_Helper.StopCoroutine(entry.HelperCookie); }
                catch (Exception ex) { FrameworkLog.Error("CoroutineHelper.StopCoroutine threw: {0}", ex); }
            }
            return true;
        }

        /// <summary>把用户 IEnumerator 用 try/finally 包起来，结束时自动从注册表移除。</summary>
        private IEnumerator Wrap(IEnumerator inner, int id)
        {
            // 注意：try/yield 限制 → finally 内做清理。
            try
            {
                while (true)
                {
                    object current;
                    try
                    {
                        if (!inner.MoveNext()) yield break;
                        current = inner.Current;
                    }
                    catch (Exception ex)
                    {
                        FrameworkLog.Error("Coroutine '{0}' threw: {1}", id, ex);
                        yield break;
                    }
                    yield return current;
                }
            }
            finally
            {
                // Helper 端 Stop 也会进 finally；二者都走移除路径。
                // 单次 Remove 即可（返回 bool），无需先 ContainsKey 再 Remove 的双查找。
                m_Running.Remove(id);
            }
        }

        private static float NowSeconds()
        {
            // 单调时钟，避免系统时钟跳变导致的负数/巨大耗时，且子秒精度充足。
            return Utility.Timestamp.SecondsF;
        }
    }
}
