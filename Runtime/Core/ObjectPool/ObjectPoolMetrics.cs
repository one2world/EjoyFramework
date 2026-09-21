//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.ObjectPool
{
    /// <summary>
    /// 对象池运行指标快照（零分配读取：<see cref="IObjectPool{T}.GetMetrics"/> 直接填充 out 参数）。
    ///
    /// 用途：容量调参与泄漏排查。经验规则——
    ///   • <see cref="SpawnMissCount"/> 持续上涨 = 池子太小或预热不足（每次 miss 都意味着业务侧 new 一个新对象）；
    ///   • <see cref="PeakCount"/> 远高于 <see cref="Count"/> 且 <see cref="TotalReleaseCount"/> 高 = 峰值后频繁释放重建，考虑调大 ExpireTime；
    ///   • <see cref="SpawnedCount"/> 只升不降 = 业务漏了 Unspawn。
    /// </summary>
    public readonly struct ObjectPoolMetrics
    {
        /// <summary>当前池内对象总数（在用 + 空闲）。</summary>
        public readonly int Count;

        /// <summary>当前在用对象数（SpawnCount &gt; 0 的对象）。</summary>
        public readonly int SpawnedCount;

        /// <summary>当前空闲对象数。</summary>
        public readonly int IdleCount;

        /// <summary>历史最大对象总数。</summary>
        public readonly int PeakCount;

        /// <summary>累计 Spawn 成功次数。</summary>
        public readonly long TotalSpawnCount;

        /// <summary>累计 Unspawn 次数。</summary>
        public readonly long TotalUnspawnCount;

        /// <summary>累计对象释放（真正销毁）次数。</summary>
        public readonly long TotalReleaseCount;

        /// <summary>累计 Spawn 未命中（返回 null，业务需自行创建）次数。</summary>
        public readonly long SpawnMissCount;

        /// <summary>容量上限。</summary>
        public readonly int Capacity;

        /// <summary>对象过期秒数。</summary>
        public readonly float ExpireTime;

        public ObjectPoolMetrics(
            int count, int spawnedCount, int peakCount,
            long totalSpawnCount, long totalUnspawnCount, long totalReleaseCount, long spawnMissCount,
            int capacity, float expireTime)
        {
            Count = count;
            SpawnedCount = spawnedCount;
            IdleCount = count - spawnedCount;
            PeakCount = peakCount;
            TotalSpawnCount = totalSpawnCount;
            TotalUnspawnCount = totalUnspawnCount;
            TotalReleaseCount = totalReleaseCount;
            SpawnMissCount = spawnMissCount;
            Capacity = capacity;
            ExpireTime = expireTime;
        }

        /// <summary>Spawn 命中率（0~1）；从未 Spawn 过时为 1。</summary>
        public float HitRate
        {
            get
            {
                long total = TotalSpawnCount + SpawnMissCount;
                return total <= 0 ? 1f : (float)((double)TotalSpawnCount / total);
            }
        }
    }
}
