//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

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
