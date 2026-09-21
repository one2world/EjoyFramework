//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.ObjectPool
{
    /// <summary>
    /// 对象池接口。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    public interface IObjectPool<T> where T : ObjectBase
    {
        /// <summary>
        /// 获取对象池名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取对象池完整名称。
        /// </summary>
        string FullName { get; }

        /// <summary>
        /// 获取对象池对象类型。
        /// </summary>
        Type ObjectType { get; }

        /// <summary>
        /// 获取对象池中对象的数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 获取对象池中能被释放的对象的数量。
        /// </summary>
        int CanReleaseCount { get; }

        /// <summary>
        /// 获取是否允许多次获取。
        /// </summary>
        bool AllowMultiSpawn { get; }

        /// <summary>
        /// 获取或设置对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        float AutoReleaseInterval { get; set; }

        /// <summary>
        /// 获取或设置对象池的容量。
        /// </summary>
        int Capacity { get; set; }

        /// <summary>
        /// 获取或设置对象池对象过期秒数。
        /// </summary>
        float ExpireTime { get; set; }

        /// <summary>
        /// 获取或设置对象池的优先级。
        /// </summary>
        int Priority { get; set; }

        /// <summary>
        /// 注册对象。
        /// </summary>
        void Register(T obj, bool spawned);

        /// <summary>
        /// 预热：用 <paramref name="factory"/> 创建对象并以空闲态注册，直到池内对象总数不少于 <paramref name="count"/>。
        /// 在加载界面/关卡开始时调用，把首次 Spawn 的创建开销从战斗帧挪走。
        /// <paramref name="count"/> 大于 Capacity 时抛出（否则超出部分会被容量回收，预热等于白做）。
        /// </summary>
        /// <param name="count">目标对象总数（含在用）。</param>
        /// <param name="factory">对象工厂，返回已 Initialize、尚未注册的对象。</param>
        /// <returns>本次实际创建并注册的对象数。</returns>
        int Prewarm(int count, Func<T> factory);

        /// <summary>当前在用对象数（SpawnCount &gt; 0）。O(1)。</summary>
        int SpawnedCount { get; }

        /// <summary>历史最大对象总数。O(1)。</summary>
        int PeakCount { get; }

        /// <summary>
        /// 收缩：释放最久未用的空闲对象直到总数不超过 <paramref name="keepCount"/>（受 Locked / CustomCanReleaseFlag 约束）。
        /// </summary>
        /// <returns>实际释放的对象数。</returns>
        int Trim(int keepCount);

        /// <summary>零分配读取运行指标。</summary>
        void GetMetrics(out ObjectPoolMetrics metrics);

        /// <summary>获取所有对象信息（非分配版本：写入调用方提供的列表，列表先被清空）。</summary>
        void GetAllObjectInfos(List<ObjectInfo> results);

        /// <summary>
        /// 检查对象。
        /// </summary>
        bool CanSpawn();

        /// <summary>
        /// 检查对象。
        /// </summary>
        bool CanSpawn(string name);

        /// <summary>
        /// 获取对象。
        /// </summary>
        T Spawn();

        /// <summary>
        /// 获取对象。
        /// </summary>
        T Spawn(string name);

        /// <summary>
        /// 回收对象。
        /// </summary>
        void Unspawn(object target);

        /// <summary>
        /// 释放对象池中的可释放对象。
        /// </summary>
        void Release();

        /// <summary>
        /// 释放对象池中的所有未使用对象。
        /// </summary>
        void ReleaseAllUnused();
    }
}
