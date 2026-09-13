//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 标记某 <see cref="EjoyFramework.Core.Network.Packet"/> 子类参与网络包编解码代码生成。
    ///
    /// 被标记的 Packet 必须同时是可二进制序列化的（即也标记 <see cref="GenerateSerializerAttribute"/>，
    /// 从而实现 <see cref="EjoyFramework.Core.Serialization.IBinarySerializable"/>）。
    /// PacketCodecGenerator 会扫描所有标记类型，默认在声明程序集的 Generated/Network 下生成
    /// <c>GeneratedPacketCodec</c> 静态类。
    /// 协议 Id 必须写在 attribute 上，例如 <c>[GeneratePacketCodec(1001)]</c>；生成器不会在
    /// Editor 中构造 Packet 读取 Id。接入 <c>GeneratedPacketCodec.GetPacketId(Packet)</c> 后，
    /// 发送帧头与接收解码使用同一个 attribute 协议 Id 来源。
    ///
    /// 生成内容：
    ///   • <c>byte[] SerializeBody(Packet)</c> —— 调用 Packet 的二进制 Serialize；
    ///   • <c>int GetPacketId(Packet)</c> —— 按 attribute 协议 Id 返回发送帧头 Id；
    ///   • <c>Packet CreatePacket(int id, ByteBuffer)</c> —— 按 id 创建实例并 Deserialize。
    ///
    /// 业务在自定义的 <c>TcpNetworkChannelHelper</c> 子类里转调它，即可消除手写包体编解码。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class GeneratePacketCodecAttribute : Attribute
    {
        public GeneratePacketCodecAttribute()
        {
        }

        public GeneratePacketCodecAttribute(int packetId)
        {
            PacketId = packetId;
            HasPacketId = true;
        }

        public int PacketId { get; }

        public bool HasPacketId { get; }
    }
}
