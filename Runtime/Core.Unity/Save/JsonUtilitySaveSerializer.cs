//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Save;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Unity JsonUtility 实现 ISaveSerializer。
    /// 业务可用 Newtonsoft.Json / System.Text.Json 等替换。
    ///
    /// 警告：Unity 的 <see cref="UnityEngine.JsonUtility"/> 限制较多，存档数据模型须避开以下类型，否则会被静默丢弃或抛错：
    /// <list type="bullet">
    ///   <item><description>不支持 Dictionary&lt;,&gt;（以及任何字典）——用并行 List 或自定义可序列化键值对替代；</description></item>
    ///   <item><description>不支持多态 / 接口 / 抽象基类字段——反序列化只会还原声明类型，派生信息丢失；</description></item>
    ///   <item><description>不支持可空值类型（int? 等）与顶层数组/原始值——请包一层 [Serializable] 类；</description></item>
    ///   <item><description>仅序列化 public 字段或带 [SerializeField] 的字段；属性不会被序列化。</description></item>
    /// </list>
    /// 若存档需要上述能力，请注入 Newtonsoft.Json 等更完备的 ISaveSerializer 实现。
    /// </summary>
    public sealed class JsonUtilitySaveSerializer : ISaveSerializer
    {
        public string Serialize(object obj) => obj == null ? string.Empty : JsonUtility.ToJson(obj);
        public T Deserialize<T>(string text) => string.IsNullOrEmpty(text) ? default(T) : JsonUtility.FromJson<T>(text);
        public object Deserialize(Type type, string text) => string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson(text, type);
    }
}
