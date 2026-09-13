//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EjoyFramework.Core.Performance
{
    /// <summary>
    /// C# / Mono 对象追踪诊断器（性能模块）。用<b>弱引用</b>登记指定对象，按类型统计存活 / 泄漏，可选捕获创建堆栈。
    /// 弱引用不会延长被追踪对象的生命周期。专用于「针对某些 bug（内存泄漏 / 悬垂引用）的检测分析」。
    ///
    /// 对象分两类：
    ///   • <b>普通对象</b>：弱引用是否回收 = 是否泄漏（仍被引用则未回收）。
    ///   • <b>Unity 对象</b>：除托管是否回收外，还区分原生是否被 Destroy —— 已 Destroy 却仍被托管持有
    ///     （<see cref="TrackedObjectLiveness.UnityNativeDestroyed"/>）是典型悬垂引用 bug。该判定经注入的
    ///     <see cref="IUnityObjectInspector"/> 完成（核心层不引用 UnityEngine）。
    ///
    /// 默认<b>关闭</b>（有开销，尤其堆栈捕获）；排查时设 <see cref="Enabled"/>=true，再在对象创建处调用 <see cref="Track"/>。
    /// 线程安全：可在任意线程 Track / Snapshot / Sweep。
    /// </summary>
    public static class ObjectTracker
    {
        private sealed class Entry
        {
            public WeakReference<object> Ref;
            public Type Type;
            public string Tag;
            public string CreationStack;
            public double TrackedAtSeconds;
        }

        private const int DefaultMaxEntries = 100000;

        private static readonly object s_Lock = new object();
        private static readonly List<Entry> s_Entries = new List<Entry>();
        private static IUnityObjectInspector s_UnityInspector;
        private static volatile bool s_Enabled;
        private static volatile bool s_CaptureStackByDefault;
        private static int s_MaxEntries = DefaultMaxEntries;
        private static bool s_CapWarned;

        /// <summary>总开关。默认 false —— 关闭时 <see cref="Track"/> 为空操作（接近零开销）。</summary>
        public static bool Enabled { get { return s_Enabled; } set { s_Enabled = value; } }

        /// <summary><see cref="Track"/> 未显式指定 captureStack 时是否默认捕获创建堆栈。默认 false（堆栈捕获较贵）。</summary>
        public static bool CaptureStackByDefault { get { return s_CaptureStackByDefault; } set { s_CaptureStackByDefault = value; } }

        /// <summary>追踪条目上限，防止开启后在热点循环中无界增长。默认 100000；&lt;=0 表示不限制。</summary>
        public static int MaxEntries
        {
            get { lock (s_Lock) { return s_MaxEntries; } }
            set { lock (s_Lock) { s_MaxEntries = value < 0 ? 0 : value; } }
        }

        /// <summary>当前追踪条目数（含尚未 Sweep 的已回收条目）。</summary>
        public static int TrackedCount { get { lock (s_Lock) { return s_Entries.Count; } } }

        /// <summary>注入 Unity 对象探针（由 Unity 层在启动时自动调用）。未注入时所有对象按普通对象处理。</summary>
        public static void SetUnityObjectInspector(IUnityObjectInspector inspector)
        {
            lock (s_Lock) { s_UnityInspector = inspector; }
        }

        /// <summary>
        /// 登记一个对象（弱引用，不延长其生命周期）。<paramref name="captureStack"/> 为 null 时取
        /// <see cref="CaptureStackByDefault"/>。关闭、obj 为 null、或达到 <see cref="MaxEntries"/> 时不登记。
        /// </summary>
        /// <param name="obj">被追踪对象。对 Unity 对象传入的是其<b>托管引用</b>（即便原生已 Destroy 也照常登记）。</param>
        /// <param name="tag">可选标签，便于在明细中区分来源。</param>
        /// <param name="captureStack">是否捕获创建处堆栈；null 取全局默认。</param>
        public static void Track(object obj, string tag = null, bool? captureStack = null)
        {
            if (!s_Enabled || obj == null) return;

            bool capture = captureStack ?? s_CaptureStackByDefault;
            Entry entry = new Entry
            {
                Ref = new WeakReference<object>(obj),
                Type = obj.GetType(),
                Tag = tag,
                CreationStack = capture ? CaptureStack() : null,
                TrackedAtSeconds = Utility.Timestamp.Seconds,
            };

            lock (s_Lock)
            {
                if (s_MaxEntries > 0 && s_Entries.Count >= s_MaxEntries)
                {
                    if (!s_CapWarned)
                    {
                        s_CapWarned = true;
                        FrameworkLog.Warning("ObjectTracker reached MaxEntries={0}; further Track calls ignored until Sweep/Clear.", s_MaxEntries);
                    }
                    return;
                }
                s_Entries.Add(entry);
            }
        }

        /// <summary>移除所有已被 GC 回收的弱引用条目，返回移除数量。</summary>
        public static int Sweep()
        {
            lock (s_Lock)
            {
                int removed = 0;
                for (int i = s_Entries.Count - 1; i >= 0; i--)
                {
                    if (!s_Entries[i].Ref.TryGetTarget(out _))
                    {
                        s_Entries.RemoveAt(i);
                        removed++;
                    }
                }
                if (removed > 0) s_CapWarned = false;
                return removed;
            }
        }

        /// <summary>清空全部追踪条目。</summary>
        public static void Clear()
        {
            lock (s_Lock)
            {
                s_Entries.Clear();
                s_CapWarned = false;
            }
        }

        /// <summary>按类型聚合统计快照（顺带清理已回收条目）。结果按类型名排序。</summary>
        public static ObjectTrackerStats[] Snapshot()
        {
            lock (s_Lock)
            {
                IUnityObjectInspector insp = s_UnityInspector;
                var byType = new Dictionary<Type, ObjectTrackerStats>();

                for (int i = s_Entries.Count - 1; i >= 0; i--)
                {
                    Entry e = s_Entries[i];
                    if (!e.Ref.TryGetTarget(out object obj))
                    {
                        s_Entries.RemoveAt(i); // 顺带清理已回收
                        continue;
                    }

                    bool isUnity = insp != null && insp.IsUnityObjectType(e.Type);
                    if (!byType.TryGetValue(e.Type, out ObjectTrackerStats s))
                    {
                        s = new ObjectTrackerStats { TypeName = e.Type.FullName, IsUnityObject = isUnity };
                    }
                    s.Alive++;
                    if (isUnity)
                    {
                        if (insp.IsNativeAlive(obj)) s.UnityNativeAlive++;
                        else s.UnityNativeDestroyed++;
                    }
                    byType[e.Type] = s;
                }

                var result = new ObjectTrackerStats[byType.Count];
                byType.Values.CopyTo(result, 0);
                Array.Sort(result, (a, b) => string.CompareOrdinal(a.TypeName, b.TypeName));
                return result;
            }
        }

        /// <summary>
        /// 取逐实例明细（含存活状态与创建堆栈），用于钻取泄漏对象。
        /// <paramref name="typeFullName"/> 为 null 取全部，否则仅取该类型。
        /// </summary>
        public static ObjectTrackerRecord[] Inspect(string typeFullName = null)
        {
            lock (s_Lock)
            {
                IUnityObjectInspector insp = s_UnityInspector;
                double now = Utility.Timestamp.Seconds;
                var list = new List<ObjectTrackerRecord>();

                for (int i = 0; i < s_Entries.Count; i++)
                {
                    Entry e = s_Entries[i];
                    if (typeFullName != null && e.Type.FullName != typeFullName) continue;

                    list.Add(new ObjectTrackerRecord
                    {
                        TypeName = e.Type.FullName,
                        Tag = e.Tag,
                        Liveness = LivenessOf(e, insp),
                        AgeSeconds = (float)(now - e.TrackedAtSeconds),
                        CreationStack = e.CreationStack,
                    });
                }
                return list.ToArray();
            }
        }

        private static TrackedObjectLiveness LivenessOf(Entry e, IUnityObjectInspector insp)
        {
            if (!e.Ref.TryGetTarget(out object obj)) return TrackedObjectLiveness.Collected;
            if (insp != null && insp.IsUnityObjectType(e.Type))
            {
                return insp.IsNativeAlive(obj)
                    ? TrackedObjectLiveness.UnityNativeAlive
                    : TrackedObjectLiveness.UnityNativeDestroyed;
            }
            return TrackedObjectLiveness.Alive;
        }

        // 跳过 CaptureStack + Track 两帧，从调用方起算；带文件/行（Editor / Development 构建下可用）。
        private static string CaptureStack()
        {
            return new StackTrace(2, true).ToString();
        }
    }
}
