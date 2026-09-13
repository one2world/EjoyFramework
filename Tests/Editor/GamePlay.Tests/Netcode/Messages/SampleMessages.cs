//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Netcode.Messages;

namespace EjoyFramework.GamePlay.Tests.Netcode.Messages
{
    /// <summary>
    /// 测试用样例消息：模拟玩家移动指令（多基础类型字段），验证 <see cref="INetMessage"/> 往返。
    /// </summary>
    public sealed class MoveMessage : INetMessage
    {
        public int EntityId;
        public float X;
        public float Y;
        public bool Sprinting;

        /// <summary>
        /// 序列化字段（顺序须与 <see cref="Deserialize"/> 完全一致）。
        /// </summary>
        public void Serialize(NetWriter writer)
        {
            writer.WriteInt(EntityId);
            writer.WriteFloat(X);
            writer.WriteFloat(Y);
            writer.WriteBool(Sprinting);
        }

        /// <summary>
        /// 反序列化字段。
        /// </summary>
        public void Deserialize(NetReader reader)
        {
            EntityId = reader.ReadInt();
            X = reader.ReadFloat();
            Y = reader.ReadFloat();
            Sprinting = reader.ReadBool();
        }
    }

    /// <summary>
    /// 测试用样例消息：模拟聊天文本（含字符串字段），用于验证多类型独立分派。
    /// </summary>
    public sealed class ChatTextMessage : INetMessage
    {
        public string Sender;
        public string Text;

        /// <summary>
        /// 序列化字段。
        /// </summary>
        public void Serialize(NetWriter writer)
        {
            writer.WriteString(Sender);
            writer.WriteString(Text);
        }

        /// <summary>
        /// 反序列化字段。
        /// </summary>
        public void Deserialize(NetReader reader)
        {
            Sender = reader.ReadString();
            Text = reader.ReadString();
        }
    }

    /// <summary>
    /// 测试用样例消息：仅含单字节负载，便于构造紧凑帧。
    /// </summary>
    public sealed class PingMessage : INetMessage
    {
        public byte Sequence;

        /// <summary>
        /// 序列化字段。
        /// </summary>
        public void Serialize(NetWriter writer)
        {
            writer.WriteByte(Sequence);
        }

        /// <summary>
        /// 反序列化字段。
        /// </summary>
        public void Deserialize(NetReader reader)
        {
            Sequence = reader.ReadByte();
        }
    }
}
