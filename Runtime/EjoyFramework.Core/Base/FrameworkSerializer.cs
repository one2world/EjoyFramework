//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 游戏框架序列化器基类。
    /// 注：注册回调通常发生在启动期单线程，运行时不应再修改。若有热更新场景需自行加锁。
    /// </summary>
    public abstract class FrameworkSerializer<T>
    {
        private readonly Dictionary<byte, SerializeCallback> m_SerializeCallbacks;
        private readonly Dictionary<byte, DeserializeCallback> m_DeserializeCallbacks;
        private readonly Dictionary<byte, TryGetValueCallback> m_TryGetValueCallbacks;
        private byte m_LatestSerializeCallbackVersion;

        public FrameworkSerializer()
        {
            m_SerializeCallbacks = new Dictionary<byte, SerializeCallback>();
            m_DeserializeCallbacks = new Dictionary<byte, DeserializeCallback>();
            m_TryGetValueCallbacks = new Dictionary<byte, TryGetValueCallback>();
            m_LatestSerializeCallbackVersion = 0;
        }

        public delegate bool SerializeCallback(Stream stream, T data);
        public delegate T DeserializeCallback(Stream stream);
        public delegate bool TryGetValueCallback(Stream stream, string key, out object value);

        public void RegisterSerializeCallback(byte version, SerializeCallback callback)
        {
            if (callback == null)
            {
                throw new FrameworkException("Serialize callback is invalid.");
            }

            m_SerializeCallbacks[version] = callback;
            if (version > m_LatestSerializeCallbackVersion)
            {
                m_LatestSerializeCallbackVersion = version;
            }
        }

        public void RegisterDeserializeCallback(byte version, DeserializeCallback callback)
        {
            if (callback == null)
            {
                throw new FrameworkException("Deserialize callback is invalid.");
            }

            m_DeserializeCallbacks[version] = callback;
        }

        public void RegisterTryGetValueCallback(byte version, TryGetValueCallback callback)
        {
            if (callback == null)
            {
                throw new FrameworkException("Try get value callback is invalid.");
            }

            m_TryGetValueCallbacks[version] = callback;
        }

        public bool Serialize(Stream stream, T data)
        {
            if (m_SerializeCallbacks.Count <= 0)
            {
                throw new FrameworkException("No serialize callback registered.");
            }

            return Serialize(stream, data, m_LatestSerializeCallbackVersion);
        }

        public bool Serialize(Stream stream, T data, byte version)
        {
            byte[] header = GetHeader();
            stream.WriteByte(header[0]);
            stream.WriteByte(header[1]);
            stream.WriteByte(header[2]);
            stream.WriteByte(version);
            SerializeCallback callback;
            if (!m_SerializeCallbacks.TryGetValue(version, out callback))
            {
                throw new FrameworkException(Utility.Text.Format("Serialize callback '{0}' is not exist.", version));
            }

            return callback(stream, data);
        }

        public T Deserialize(Stream stream)
        {
            byte[] header = GetHeader();
            byte header0 = (byte)stream.ReadByte();
            byte header1 = (byte)stream.ReadByte();
            byte header2 = (byte)stream.ReadByte();
            if (header0 != header[0] || header1 != header[1] || header2 != header[2])
            {
                throw new FrameworkException(Utility.Text.Format("Header is invalid, need '{0}{1}{2}', current '{3}{4}{5}'.", (char)header[0], (char)header[1], (char)header[2], (char)header0, (char)header1, (char)header2));
            }

            byte version = (byte)stream.ReadByte();
            DeserializeCallback callback;
            if (!m_DeserializeCallbacks.TryGetValue(version, out callback))
            {
                throw new FrameworkException(Utility.Text.Format("Deserialize callback '{0}' is not exist.", version));
            }

            return callback(stream);
        }

        public bool TryGetValue(Stream stream, string key, out object value)
        {
            value = null;
            byte[] header = GetHeader();
            byte header0 = (byte)stream.ReadByte();
            byte header1 = (byte)stream.ReadByte();
            byte header2 = (byte)stream.ReadByte();
            if (header0 != header[0] || header1 != header[1] || header2 != header[2])
            {
                return false;
            }

            byte version = (byte)stream.ReadByte();
            TryGetValueCallback callback;
            if (!m_TryGetValueCallbacks.TryGetValue(version, out callback))
            {
                return false;
            }

            return callback(stream, key, out value);
        }

        protected abstract byte[] GetHeader();
    }
}
