//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Serialization
{
    /// <summary>
    /// 二进制可序列化契约。由 SerializerGenerator 为标记了
    /// <see cref="GenerateSerializerAttribute"/> 的类型自动生成实现（partial 方法）。
    ///
    /// 约定：<see cref="Serialize"/> 把自身字段按固定线序写入 buffer；
    /// <see cref="Deserialize"/> 以相同顺序读回。生成代码保证读写顺序一致。
    /// 嵌套可序列化类型直接调用其 <see cref="Serialize"/>/<see cref="Deserialize"/>，
    /// 形成非反射的递归——这是相对 JsonUtility 反射递归的核心性能优势。
    /// </summary>
    public interface IBinarySerializable
    {
        /// <summary>把自身写入 buffer（追加到当前写游标）。</summary>
        void Serialize(ByteBuffer buffer);

        /// <summary>从 buffer 读回自身（从当前读游标）。</summary>
        void Deserialize(ByteBuffer buffer);
    }
}
