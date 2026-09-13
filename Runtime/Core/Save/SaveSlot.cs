//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Save
{
    /// <summary>
    /// 存档 slot 元信息（不含业务数据本身，仅 metadata）。
    /// </summary>
    [Serializable]
    public sealed class SaveSlot
    {
        public int SlotId;
        public SaveSlotMetadata Metadata;
        public int Version;       // 存档时的数据 schema 版本（用于 migration）
        public string CodecId;    // ESV2 payload codec；ESV1 返回 legacy-* 标识
        public long ByteSize;     // 存储大小（已含加密）
        public long LastWriteUnixSeconds;
    }

    /// <summary>业务侧自定义 metadata（slot 列表展示用）。</summary>
    [Serializable]
    public sealed class SaveSlotMetadata
    {
        public string PlayerName;
        public string SceneOrChapter;
        public int Level;
        public float PlayedSeconds;
        public string CustomTag;   // 业务自由扩展
    }

    /// <summary>Legacy ESV1 JSON envelope. New writes use SaveManager's binary ESV2 envelope.</summary>
    [Serializable]
    internal sealed class SaveEnvelope
    {
        public int Version;        // 业务 schema 版本
        public uint Crc32;         // 校验 metadata + dataJson
        public SaveSlotMetadata Metadata;
        public string DataJson;
        public long WriteUnixSeconds;

        /// <summary>
        /// 业务数据载荷格式：0 = JSON（默认，向后兼容旧存档——旧信封无此字段时 JsonUtility 反序列化为 0）；
        /// 1 = 二进制（DataJson 存的是 ByteBuffer 序列化后字节的 Base64 字符串）。
        /// 保持为普通可序列化字段（JsonUtility 兼容）。
        /// </summary>
        public int Format;
    }
}
