//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Netcode.Messages;

namespace EjoyFramework.GamePlay.Netcode.Session
{
    /// <summary>
    /// 输入指令消息（客户端→服务器）。
    /// <para>
    /// 客户端每次产生本地输入时发送，携带单调递增的序号、模拟步长以及输入向量。
    /// 服务器据 <see cref="Sequence"/> 回填该客户端的「已处理输入序号」，供客户端和解（reconcile）。
    /// </para>
    /// </summary>
    public sealed class InputMessage : INetMessage
    {
        /// <summary>
        /// 单条输入向量允许的最大浮点数个数。反序列化时据此拒绝畸形 / 恶意的超大计数，
        /// 防止「先按声明计数预分配大数组」的放大型 DoS。正常输入向量极小（如 [vx, vy]）。
        /// </summary>
        public const int MaxInputFloats = 64;

        /// <summary>
        /// 单调递增的输入序号，由客户端预测器分配，用于与服务器 ack 对齐。
        /// </summary>
        public uint Sequence { get; set; }

        /// <summary>
        /// 该输入对应的模拟时间步长（秒）。
        /// </summary>
        public float DeltaTime { get; set; }

        /// <summary>
        /// 输入向量（如 [vx, vy]）。约定非 null；空输入请传长度为 0 的数组。
        /// </summary>
        public float[] Input { get; set; }

        /// <inheritdoc />
        public void Serialize(NetWriter writer)
        {
            writer.WriteUInt(Sequence);
            writer.WriteFloat(DeltaTime);

            float[] input = Input ?? Array.Empty<float>();
            writer.WriteUShort((ushort)input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                writer.WriteFloat(input[i]);
            }
        }

        /// <inheritdoc />
        public void Deserialize(NetReader reader)
        {
            Sequence = reader.ReadUInt();
            DeltaTime = reader.ReadFloat();

            int count = reader.ReadUShort();
            // 在分配前先校验计数，拒绝放大型 DoS（声明海量计数迫使预分配大数组）。
            if (count > MaxInputFloats)
            {
                throw new System.IO.InvalidDataException(
                    "InputMessage 输入计数超过上限：" + count + " > " + MaxInputFloats);
            }

            float[] input = new float[count];
            for (int i = 0; i < count; i++)
            {
                input[i] = reader.ReadFloat();
            }

            Input = input;
        }
    }
}
