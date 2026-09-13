//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.Core.AI
{
    /// <summary>
    /// 行为树黑板：节点共享数据 key-value 表。
    /// 类型安全（generic by TValue）；业务 Set/Get 时编译期检查类型。
    /// </summary>
    public sealed class Blackboard<TContext>
    {
        private readonly Dictionary<string, object> m_Data = new Dictionary<string, object>();

        public void Set<T>(string key, T value) => m_Data[key] = value;

        /// <summary>
        /// 按 key 取值。
        /// 注意：当 key 不存在，或存储的值与请求类型 T 不匹配时，均静默返回 defaultValue。
        /// 类型不匹配的情况被掩盖（不抛异常、不记日志），以避免破坏依赖此宽松行为的调用方；
        /// 调试类型问题时请留意此处可能吞掉 mismatch。
        /// </summary>
        public T Get<T>(string key, T defaultValue = default)
        {
            return m_Data.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        }

        public bool Has(string key) => m_Data.ContainsKey(key);

        public bool Remove(string key) => m_Data.Remove(key);

        public void Clear() => m_Data.Clear();

        public int Count => m_Data.Count;
    }
}
