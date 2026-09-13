//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.CloudSave
{
    /// <summary>
    /// 一条云存档数据。纯数据、不可变值对象，与引擎无关。
    /// 由 <see cref="Key"/> 唯一定位；<see cref="Version"/> 与 <see cref="TimestampMs"/>
    /// 共同用于冲突解决（详见 <see cref="CloudSaveManager.ResolveConflict"/>）。
    /// </summary>
    public sealed class CloudSaveData
    {
        private readonly string m_Key;
        private readonly byte[] m_Data;
        private readonly long m_Version;
        private readonly long m_TimestampMs;

        /// <summary>
        /// 构造一条云存档数据。
        /// </summary>
        /// <param name="key">存档键，唯一标识一份存档。</param>
        /// <param name="data">存档字节内容；调用方应视为只读，本类不会复制也不会修改它。</param>
        /// <param name="version">单调递增的存档版本号（每次写入自增）。</param>
        /// <param name="timestampMs">存档发生时的 Unix 纪元毫秒时间戳。</param>
        public CloudSaveData(string key, byte[] data, long version, long timestampMs)
        {
            m_Key = key;
            m_Data = data;
            m_Version = version;
            m_TimestampMs = timestampMs;
        }

        /// <summary>
        /// 存档键，唯一标识一份存档。
        /// </summary>
        public string Key
        {
            get { return m_Key; }
        }

        /// <summary>
        /// 存档字节内容。调用方应视为只读，请勿原地修改。
        /// </summary>
        public byte[] Data
        {
            get { return m_Data; }
        }

        /// <summary>
        /// 单调递增的存档版本号，每次写入自增。版本越高代表写入越晚。
        /// </summary>
        public long Version
        {
            get { return m_Version; }
        }

        /// <summary>
        /// 存档发生时的 Unix 纪元毫秒时间戳。
        /// </summary>
        public long TimestampMs
        {
            get { return m_TimestampMs; }
        }
    }
}
