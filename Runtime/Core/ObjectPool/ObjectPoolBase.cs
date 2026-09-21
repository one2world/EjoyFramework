//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.ObjectPool
{
    /// <summary>
    /// 对象池基类。
    /// </summary>
    public abstract class ObjectPoolBase
    {
        private readonly string m_Name;

        /// <summary>
        /// 初始化对象池基类的新实例。
        /// </summary>
        public ObjectPoolBase(string name)
        {
            m_Name = name ?? string.Empty;
        }

        /// <summary>
        /// 获取对象池名称。
        /// </summary>
        public string Name
        {
            get { return m_Name; }
        }

        /// <summary>
        /// 获取对象池完整名称。
        /// </summary>
        public string FullName
        {
            get { return Utility.Text.GetFullName(ObjectType, m_Name); }
        }

        /// <summary>
        /// 获取对象池对象类型。
        /// </summary>
        public abstract Type ObjectType { get; }

        /// <summary>
        /// 获取对象池中对象的数量。
        /// </summary>
        public abstract int Count { get; }

        /// <summary>
        /// 获取对象池中能被释放的对象的数量。
        /// </summary>
        public abstract int CanReleaseCount { get; }

        /// <summary>
        /// 获取是否允许多次获取。
        /// </summary>
        public abstract bool AllowMultiSpawn { get; }

        /// <summary>
        /// 获取或设置对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        public abstract float AutoReleaseInterval { get; set; }

        /// <summary>
        /// 获取或设置对象池的容量。
        /// </summary>
        public abstract int Capacity { get; set; }

        /// <summary>
        /// 获取或设置对象池对象过期秒数。
        /// </summary>
        public abstract float ExpireTime { get; set; }

        /// <summary>
        /// 获取或设置对象池的优先级。
        /// </summary>
        public abstract int Priority { get; set; }

        /// <summary>
        /// 释放对象池中的可释放对象。
        /// </summary>
        public abstract void Release();

        /// <summary>
        /// 释放对象池中的所有未使用对象。
        /// </summary>
        public abstract void ReleaseAllUnused();

        /// <summary>
        /// 获取所有对象信息。
        /// </summary>
        /// <returns>所有对象信息。</returns>
        public abstract ObjectInfo[] GetAllObjectInfos();

        /// <summary>
        /// 获取所有对象信息（非分配版本：写入调用方提供的列表）。诊断面板每帧刷新时用这个。
        /// </summary>
        /// <param name="results">输出列表，会先被清空。</param>
        public abstract void GetAllObjectInfos(List<ObjectInfo> results);

        /// <summary>当前在用对象数（SpawnCount &gt; 0）。O(1)。</summary>
        public abstract int SpawnedCount { get; }

        /// <summary>历史最大对象总数。O(1)。</summary>
        public abstract int PeakCount { get; }

        /// <summary>
        /// 收缩：释放最久未用的空闲对象，直到总数不超过 <paramref name="keepCount"/>（受 Locked / CustomCanReleaseFlag 约束）。
        /// 用于关卡切换、内存告警等主动回收时机；与自动过期相比它是即时且确定的。
        /// </summary>
        /// <param name="keepCount">保留的对象总数上限。</param>
        /// <returns>实际释放的对象数。</returns>
        public abstract int Trim(int keepCount);

        /// <summary>零分配读取运行指标。</summary>
        public abstract void GetMetrics(out ObjectPoolMetrics metrics);

        /// <summary>
        /// 对象池轮询。
        /// </summary>
        internal abstract void Update(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 关闭并清理对象池。
        /// </summary>
        internal abstract void Shutdown();
    }
}
