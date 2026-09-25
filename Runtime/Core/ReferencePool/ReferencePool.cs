//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 引用池。
    /// 修复：
    ///   - EnableStrictCheck 改 volatile 字段。
    ///   - 全局 SetMaxCapacity API 一次配置默认上限。
    /// </summary>
    public static partial class ReferencePool
    {
        private static readonly Dictionary<Type, ReferenceCollection> s_ReferenceCollections = new Dictionary<Type, ReferenceCollection>();
        private static volatile bool s_EnableStrictCheck = false;
        private static int s_DefaultMaxCapacity = 0;

        public static bool EnableStrictCheck
        {
            get { return s_EnableStrictCheck; }
            set { s_EnableStrictCheck = value; }
        }

        public static int Count
        {
            get
            {
                lock (s_ReferenceCollections)
                {
                    return s_ReferenceCollections.Count;
                }
            }
        }

        /// <summary>
        /// 设置后续新创建 ReferenceCollection 的默认上限（0 = 无限）。已存在的不受影响。
        /// </summary>
        public static int DefaultMaxCapacity
        {
            get { return s_DefaultMaxCapacity; }
            set { s_DefaultMaxCapacity = value < 0 ? 0 : value; }
        }

        public static ReferencePoolInfo[] GetAllReferencePoolInfos()
        {
            lock (s_ReferenceCollections)
            {
                int idx = 0;
                ReferencePoolInfo[] results = new ReferencePoolInfo[s_ReferenceCollections.Count];
                foreach (KeyValuePair<Type, ReferenceCollection> kvp in s_ReferenceCollections)
                {
                    results[idx++] = new ReferencePoolInfo(
                        kvp.Key,
                        kvp.Value.UnusedReferenceCount,
                        kvp.Value.UsingReferenceCount,
                        kvp.Value.AcquireReferenceCount,
                        kvp.Value.ReleaseReferenceCount,
                        kvp.Value.AddReferenceCount,
                        kvp.Value.RemoveReferenceCount);
                }
                return results;
            }
        }

        public static void ClearAll()
        {
            ReferenceCollection[] all;
            lock (s_ReferenceCollections)
            {
                all = new ReferenceCollection[s_ReferenceCollections.Count];
                int idx = 0;
                foreach (KeyValuePair<Type, ReferenceCollection> kvp in s_ReferenceCollections)
                {
                    all[idx++] = kvp.Value;
                }
                s_ReferenceCollections.Clear();
            }

            for (int i = 0; i < all.Length; i++)
            {
                all[i].RemoveAll();
            }
        }

        public static T Acquire<T>() where T : class, IReference, new()
        {
            return GetReferenceCollection(typeof(T)).Acquire<T>();
        }

        public static IReference Acquire(Type referenceType)
        {
            InternalCheckReferenceType(referenceType);
            return GetReferenceCollection(referenceType).Acquire();
        }

        public static void Release(IReference reference)
        {
            if (reference == null)
            {
                throw new FrameworkException("Reference is invalid.");
            }

            Type referenceType = reference.GetType();
            InternalCheckReferenceType(referenceType);
            GetReferenceCollection(referenceType).Release(reference);
        }

        public static void Add<T>(int count) where T : class, IReference, new()
        {
            GetReferenceCollection(typeof(T)).Add<T>(count);
        }

        public static void Add(Type referenceType, int count)
        {
            InternalCheckReferenceType(referenceType);
            GetReferenceCollection(referenceType).Add(count);
        }

        public static void Remove<T>(int count) where T : class, IReference
        {
            GetReferenceCollection(typeof(T)).Remove(count);
        }

        public static void Remove(Type referenceType, int count)
        {
            InternalCheckReferenceType(referenceType);
            GetReferenceCollection(referenceType).Remove(count);
        }

        public static void RemoveAll<T>() where T : class, IReference
        {
            GetReferenceCollection(typeof(T)).RemoveAll();
        }

        public static void RemoveAll(Type referenceType)
        {
            InternalCheckReferenceType(referenceType);
            GetReferenceCollection(referenceType).RemoveAll();
        }

        /// <summary>
        /// 设置指定类型的池大小上限。
        /// </summary>
        public static void SetMaxCapacity<T>(int maxCapacity) where T : class, IReference
        {
            GetReferenceCollection(typeof(T)).MaxCapacity = maxCapacity;
        }

        public static void SetMaxCapacity(Type referenceType, int maxCapacity)
        {
            InternalCheckReferenceType(referenceType);
            GetReferenceCollection(referenceType).MaxCapacity = maxCapacity;
        }

        private static void InternalCheckReferenceType(Type referenceType)
        {
            if (!s_EnableStrictCheck) return;

            if (referenceType == null)
            {
                throw new FrameworkException("Reference type is invalid.");
            }

            if (!referenceType.IsClass || referenceType.IsAbstract)
            {
                throw new FrameworkException("Reference type is not a non-abstract class type.");
            }

            if (!typeof(IReference).IsAssignableFrom(referenceType))
            {
                throw new FrameworkException(string.Format("Reference type '{0}' is invalid.", referenceType.FullName));
            }
        }

        private static ReferenceCollection GetReferenceCollection(Type referenceType)
        {
            if (referenceType == null)
            {
                throw new FrameworkException("ReferenceType is invalid.");
            }

            ReferenceCollection collection;
            lock (s_ReferenceCollections)
            {
                if (!s_ReferenceCollections.TryGetValue(referenceType, out collection))
                {
                    collection = new ReferenceCollection(referenceType);
                    if (s_DefaultMaxCapacity > 0)
                    {
                        collection.MaxCapacity = s_DefaultMaxCapacity;
                    }
                    s_ReferenceCollections.Add(referenceType, collection);
                }
            }

            return collection;
        }
    }
}
