//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Netcode.Messages
{
    /// <summary>
    /// 消息类型 id（ushort）与「工厂方法」的双向注册表，并负责消息的成帧/解帧。
    ///
    /// 帧格式：<c>[ushort typeId（小端）][payload...]</c>。
    /// - <see cref="Pack"/> 在负载前写入 2 字节类型 id。
    /// - <see cref="Unpack"/> 先读出 id，再用对应工厂构造实例并调用其 <see cref="INetMessage.Deserialize"/>。
    ///
    /// 注册校验：同一 typeId 或同一 <see cref="Type"/> 不可重复注册（防止稳定 id 被意外覆盖）。
    /// 单线程使用，非线程安全。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// </summary>
    public sealed class NetMessageRegistry
    {
        private readonly Dictionary<ushort, Func<INetMessage>> m_FactoryById =
            new Dictionary<ushort, Func<INetMessage>>();
        private readonly Dictionary<Type, ushort> m_IdByType = new Dictionary<Type, ushort>();

        /// <summary>
        /// 注册一种消息类型及其工厂。
        /// </summary>
        /// <typeparam name="T">消息类型，须实现 <see cref="INetMessage"/>。</typeparam>
        /// <param name="typeId">稳定的消息类型 id（应在版本间保持不变）。</param>
        /// <param name="factory">无参工厂，用于在解帧时构造空实例。</param>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> 为 null。</exception>
        /// <exception cref="ArgumentException">typeId 或类型 <typeparamref name="T"/> 已被注册。</exception>
        public void Register<T>(ushort typeId, Func<T> factory) where T : INetMessage
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            Type type = typeof(T);
            if (m_FactoryById.ContainsKey(typeId))
            {
                throw new ArgumentException("消息类型 id 已被注册：" + typeId, nameof(typeId));
            }

            if (m_IdByType.ContainsKey(type))
            {
                throw new ArgumentException("消息类型已被注册：" + type.FullName, nameof(T));
            }

            // 装箱为 Func<INetMessage>：T 受约束实现 INetMessage，故返回值可安全协变赋值。
            m_FactoryById.Add(typeId, () => factory());
            m_IdByType.Add(type, typeId);
        }

        /// <summary>
        /// 查询某消息类型对应的稳定类型 id。
        /// </summary>
        /// <typeparam name="T">已注册的消息类型。</typeparam>
        /// <returns>对应的类型 id。</returns>
        /// <exception cref="KeyNotFoundException">类型 <typeparamref name="T"/> 未注册。</exception>
        public ushort GetTypeId<T>() where T : INetMessage
        {
            Type type = typeof(T);
            ushort typeId;
            if (!m_IdByType.TryGetValue(type, out typeId))
            {
                throw new KeyNotFoundException("消息类型未注册：" + type.FullName);
            }

            return typeId;
        }

        /// <summary>
        /// 将消息成帧为「<c>[ushort typeId][payload]</c>」字节数组。
        /// </summary>
        /// <param name="message">待打包消息，不可为 null。</param>
        /// <returns>新分配的成帧字节数组。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> 为 null。</exception>
        /// <exception cref="KeyNotFoundException">消息的运行时类型未注册。</exception>
        public byte[] Pack(INetMessage message)
        {
            NetWriter writer = new NetWriter();
            ArraySegment<byte> seg = PackInto(message, writer);
            byte[] result = new byte[seg.Count];
            Buffer.BlockCopy(seg.Array, seg.Offset, result, 0, seg.Count);
            return result;
        }

        /// <summary>
        /// 将消息打包进调用方提供的（可复用）<see cref="NetWriter"/>，返回指向其内部缓冲的零拷贝区间。
        /// 用于高频下发路径复用同一个 writer，省去每次 <see cref="Pack"/> 的 NetWriter 分配 + ToArray 拷贝。
        /// 调用方必须在该 writer 再次写入 / <see cref="NetWriter.Reset"/> 之前消费完返回的区间
        /// （同步发送场景安全：本框架的两个传输实现都会在 Send 内同步拷贝该区间）。
        /// </summary>
        /// <param name="message">待打包的消息，不可为 null。</param>
        /// <param name="writer">复用的写入器，不可为 null；本方法会先 <see cref="NetWriter.Reset"/> 再写入。</param>
        /// <returns>指向 <paramref name="writer"/> 内部缓冲的「<c>[ushort typeId][payload]</c>」区间。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> 或 <paramref name="writer"/> 为 null。</exception>
        /// <exception cref="KeyNotFoundException">消息的运行时类型未注册。</exception>
        public ArraySegment<byte> PackInto(INetMessage message, NetWriter writer)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            Type type = message.GetType();
            ushort typeId;
            if (!m_IdByType.TryGetValue(type, out typeId))
            {
                throw new KeyNotFoundException("消息类型未注册，无法打包：" + type.FullName);
            }

            writer.Reset();
            writer.WriteUShort(typeId);
            message.Serialize(writer);
            return writer.AsSegment();
        }

        /// <summary>
        /// 从成帧字节区间中读出类型 id、构造实例并完成反序列化。
        /// </summary>
        /// <param name="framed">「<c>[ushort typeId][payload]</c>」区间。</param>
        /// <param name="typeId">输出参数：解析出的消息类型 id。</param>
        /// <returns>反序列化后的消息实例。</returns>
        /// <exception cref="KeyNotFoundException">类型 id 未注册。</exception>
        public INetMessage Unpack(ArraySegment<byte> framed, out ushort typeId)
        {
            NetReader reader = new NetReader(framed);
            typeId = reader.ReadUShort();

            Func<INetMessage> factory;
            if (!m_FactoryById.TryGetValue(typeId, out factory))
            {
                throw new KeyNotFoundException("消息类型 id 未注册，无法解包：" + typeId);
            }

            INetMessage message = factory();
            message.Deserialize(reader);
            return message;
        }

        /// <summary>
        /// 尝试解析帧头中的类型 id，并查表确认其是否已注册。
        /// 供 <see cref="MessageDispatcher"/> 静默忽略未知/截断帧使用，不抛异常。
        /// </summary>
        /// <param name="framed">成帧字节区间。</param>
        /// <param name="typeId">输出参数：解析出的类型 id（解析失败时为 0）。</param>
        /// <returns>当帧至少含 2 字节且 id 已注册时返回 true，否则 false。</returns>
        public bool TryPeekTypeId(ArraySegment<byte> framed, out ushort typeId)
        {
            typeId = 0;
            if (framed.Array == null || framed.Count < 2)
            {
                return false;
            }

            NetReader reader = new NetReader(framed);
            typeId = reader.ReadUShort();
            return m_FactoryById.ContainsKey(typeId);
        }
    }
}
