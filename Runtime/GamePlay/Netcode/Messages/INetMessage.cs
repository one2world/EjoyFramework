//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Netcode.Messages
{
    /// <summary>
    /// 可自序列化的网络消息。实现者负责把自身字段写入 <see cref="NetWriter"/>，
    /// 并从 <see cref="NetReader"/> 以「完全对称、相同字段顺序」的方式读回，保证精确往返。
    ///
    /// 约定：<see cref="Serialize"/> 与 <see cref="Deserialize"/> 只处理「负载（payload）」本身，
    /// 不写入/读取消息类型 id —— 类型 id 的成帧由 <see cref="NetMessageRegistry"/> 统一负责。
    /// </summary>
    public interface INetMessage
    {
        /// <summary>
        /// 将自身负载写入 <paramref name="writer"/>。
        /// </summary>
        void Serialize(NetWriter writer);

        /// <summary>
        /// 从 <paramref name="reader"/> 读回自身负载（字段顺序须与 <see cref="Serialize"/> 完全一致）。
        /// </summary>
        void Deserialize(NetReader reader);
    }
}
