//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 标记某 partial class/struct 由 SerializerGenerator 自动生成
    /// <c>Serialize(ByteBuffer)</c> / <c>Deserialize(ByteBuffer)</c> 二进制读写方法
    /// （实现 <see cref="EjoyFramework.Core.Serialization.IBinarySerializable"/>）。
    ///
    /// 相比 JsonUtility 的反射递归，生成代码逐字段直读直写、零反射零装箱。
    /// 业务侧只需把类型声明为 <c>partial</c> 并标记本属性，运行
    /// Menu <c>EjoyFramework/Core/CodeGen/Generate All</c>（或 Code Generation Window）。
    ///
    /// 支持的成员类型：基元数值 / bool / char / string / enum / 嵌套 [GenerateSerializer] 类型 /
    /// <c>IBinarySerializable</c> 类型 / 上述类型的一维数组、<c>List&lt;T&gt;</c>、
    /// <c>Queue&lt;T&gt;</c>、<c>Stack&lt;T&gt;</c>、确定性 <c>HashSet&lt;T&gt;</c> 与
    /// 确定性 key 的 <c>Dictionary&lt;TKey,TValue&gt;</c>。不支持的成员会被跳过并在生成报告中告警。
    ///
    /// <code>
    /// [GenerateSerializer]
    /// public partial class PlayerState
    /// {
    ///     public int Level;
    ///     public string Name;
    ///     public System.Collections.Generic.List&lt;int&gt; Inventory;
    /// }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class GenerateSerializerAttribute : Attribute { }

    /// <summary>
    /// 标记某字段/属性在代码生成（序列化、深拷贝）中被忽略。
    /// 用于排除运行期缓存、不需要持久化/拷贝的成员。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class SerializeIgnoreAttribute : Attribute { }

    /// <summary>
    /// 显式指定成员的序列化顺序（升序）。未标记的成员排在已标记之后，
    /// 组内按声明顺序（MetadataToken）。
    ///
    /// 二进制线序由顺序唯一决定——一旦数据已落地/已上线，<b>不要</b>调整既有字段的 Order，
    /// 新增字段请追加更大的 Order，以保持向后兼容。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class SerializeOrderAttribute : Attribute
    {
        public SerializeOrderAttribute(int order)
        {
            Order = order;
        }

        /// <summary>排序键（升序）。</summary>
        public int Order { get; private set; }
    }
}
