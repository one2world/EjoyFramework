//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 池化实例标记组件：由 <see cref="SpawnPoolComponent"/> 在实例化时挂到每个实例根上。
    ///
    /// 作用：
    ///   • 让 Despawn 从"字典查实例"变成一次 <c>GetComponent</c>（无托管分配、无字符串哈希）；
    ///   • 缓存实例上的 <see cref="ISpawnCallback"/> 数组（创建时 <c>GetComponentsInChildren</c> 一次，
    ///     之后每次取出/归还都不再分配——这是旧实现最大的每帧 GC 来源）；
    ///   • 记录在用/空闲状态与空闲起始时间，为重复归还诊断与空闲过期提供依据；
    ///   • <c>OnDestroy</c> 通知所属条目：被外部直接 Destroy 的实例会从计数与空闲列表中摘除，
    ///     取代旧实现每次 Despawn 全表扫描"伪 null"的做法。
    ///
    /// 业务代码不应手动添加或移除本组件。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class SpawnPoolInstance : MonoBehaviour
    {
        internal static readonly ISpawnCallback[] EmptyCallbacks = new ISpawnCallback[0];

        internal SpawnPoolEntry Entry;
        internal ISpawnCallback[] Callbacks = EmptyCallbacks;
        internal bool IsSpawned;
        internal float IdleSince;

        /// <summary>所属池键；不属于任何池时为 null。</summary>
        public string PoolKey
        {
            get { return Entry != null ? Entry.Key : null; }
        }

        /// <summary>当前是否处于"已取出"状态。</summary>
        public bool IsInUse
        {
            get { return IsSpawned; }
        }

        private void OnDestroy()
        {
            SpawnPoolEntry entry = Entry;
            Entry = null;
            if (entry != null)
            {
                entry.OnInstanceDestroyed(this);
            }
        }
    }
}
