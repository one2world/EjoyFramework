//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Save
{
    /// <summary>
    /// 存档管理器接口。
    ///
    /// 设计：
    ///   - 业务通过 SaveSlot.SlotId 选择存档（多 slot 支持）
    ///   - 序列化由 <see cref="ISaveStorageHelper"/> 提供（默认 JSON / 二进制 / 自定义）
    ///   - 可选加密由 <see cref="ISaveCryptoHelper"/> 提供（AES 等）
    ///   - 版本兼容由 <see cref="SaveMigrator"/> 处理：旧版本数据加载时按链式 migration 升级
    ///   - 写入失败时触发 SaveFailed 事件；数据校验失败触发 SaveCorrupted
    /// </summary>
    public interface ISaveManager
    {
        /// <summary>注入存储 backend（文件 / PlayerPrefs / Cloud）。</summary>
        void SetStorageHelper(ISaveStorageHelper helper);

        /// <summary>Registers a format-neutral payload codec and makes it the default for new ESV2 writes.</summary>
        void SetCodec(ISaveCodec codec);

        /// <summary>Registers an additional codec so slots written with its id remain readable.</summary>
        void RegisterCodec(ISaveCodec codec);

        /// <summary>Selects a previously registered codec for subsequent writes.</summary>
        void SetDefaultCodec(string codecId);

        /// <summary>注入序列化器（JSON 等）。SaveManager 默认无序列化器，业务必须注入。</summary>
        void SetSerializer(ISaveSerializer serializer);

        /// <summary>
        /// 注入可选的二进制序列化器（基于 <see cref="EjoyFramework.Core.Serialization.ByteBuffer"/>）。
        /// 注入后写入路径将业务对象序列化为二进制并以 Base64 存入信封（envelope.Format = 1）；
        /// 不注入（null）时保持默认 JSON 路径（envelope.Format = 0，向后兼容旧存档）。
        /// 注意：仅影响业务数据的序列化方式，信封外层仍为 JSON，CRC/HMAC/加密/存储链路不变。
        /// </summary>
        void SetBinarySerializer(ISaveBinarySerializer serializer);

        /// <summary>注入加密 helper（可选；null 表示不加密）。</summary>
        void SetCryptoHelper(ISaveCryptoHelper helper);

        /// <summary>
        /// 注入完整性密钥（HMAC-SHA256 的 key）。
        /// 提供后，存档防篡改由 HMAC（覆盖 Metadata + DataJson + Version + 时间戳）保证；
        /// 未提供（null/空）时仅退化为 CRC32 损坏检测（best-effort，不防篡改）。
        /// </summary>
        void SetIntegritySecret(byte[] secret);

        /// <summary>
        /// 注入迁移器；可选。JSON 存档使用 Register，二进制存档必须使用 RegisterBinary
        /// 注册相同版本链，二者不会互相解释载荷。
        /// </summary>
        void SetMigrator(SaveMigrator migrator);

        /// <summary>设置当前业务数据 schema 的版本号（用于写入与 migration 对比）。</summary>
        void SetCurrentVersion(int currentVersion);

        /// <summary>已知 slot 数量。</summary>
        int SlotCount { get; }

        /// <summary>列出所有 slot 摘要（不解密 / 不反序列化数据，仅 metadata）。</summary>
        SaveSlot[] GetAllSlots();

        /// <summary>检查 slot 是否存在。</summary>
        bool HasSlot(int slotId);

        /// <summary>
        /// 写入 slot。data 由 storage helper 序列化（typically JSON）。失败抛 FrameworkException 或触发 SaveFailed。
        /// </summary>
        bool WriteSlot<T>(int slotId, T data, SaveSlotMetadata metadata) where T : class;

        /// <summary>
        /// 读取 slot。若数据版本低于 currentVersion，自动走 migrator 链。
        /// 数据损坏时触发 SaveCorrupted，返回 null 或 default(T)。
        /// </summary>
        T LoadSlot<T>(int slotId) where T : class;

        /// <summary>删除 slot。</summary>
        bool DeleteSlot(int slotId);

        /// <summary>删除所有 slot（业务侧"清档"功能）。</summary>
        void DeleteAllSlots();

        /// <summary>写入失败事件（磁盘满 / 权限 / 加密失败等）。</summary>
        event Action<int, string> SaveFailed;

        /// <summary>数据损坏事件（CRC 错误 / 反序列化失败 / migration 失败）。</summary>
        event Action<int, string> SaveCorrupted;
    }

    /// <summary>
    /// 存储后端 helper：负责字节 ↔ 落地媒介（文件系统 / PlayerPrefs / Cloud）。
    /// 不关心序列化格式（由 ISaveManager 内部调 ISaveSerializer 完成 JSON ↔ object）。
    /// </summary>
    public interface ISaveStorageHelper
    {
        bool Exists(int slotId);
        byte[] Read(int slotId);

        /// <summary>
        /// 写入 slot。实现必须保证原子性（先完整写入并落盘临时数据，再原子替换）；写入异常后旧数据必须仍可读，
        /// 不满足该契约的存储实现可能造成不可恢复的用户数据损坏，不受 SaveManager 支持。
        /// 若同时实现 <see cref="ISaveBackupStorage"/>，
        /// 应在替换时保留上一份有效存档为备份，以便主存档损坏时由 SaveManager 回退。
        /// </summary>
        void Write(int slotId, byte[] payload);
        bool Delete(int slotId);
        int[] EnumerateSlotIds();
    }

    /// <summary>
    /// 可选的存档备份能力。存储 helper 实现此接口后，SaveManager 在主存档损坏/被篡改时
    /// 会尝试从 .bak 备份回退，仅当主备都失败时才触发 SaveCorrupted。
    /// 不实现此接口的 helper（如内存存储）行为不变。
    /// </summary>
    public interface ISaveBackupStorage
    {
        /// <summary>是否存在备份（.bak）副本。</summary>
        bool BackupExists(int slotId);

        /// <summary>读取备份（.bak）副本字节；不存在返回 null。</summary>
        byte[] ReadBackup(int slotId);
    }

    /// <summary>序列化器（JSON 等）。</summary>
    public interface ISaveSerializer
    {
        string Serialize(object obj);
        T Deserialize<T>(string text);
        object Deserialize(Type type, string text);
    }

    /// <summary>
    /// Format-neutral business payload codec. SaveManager stores the returned bytes directly in an ESV2 envelope;
    /// JSON, binary code generation, MessagePack and other formats are peers rather than SaveManager concerns.
    /// </summary>
    public interface ISaveCodec
    {
        /// <summary>Stable persisted identifier. Changing it creates a different on-disk format.</summary>
        string Id { get; }

        byte[] Encode(object value, Type type);
        object Decode(byte[] payload, Type type);
    }

    /// <summary>
    /// 可选的二进制序列化器（基于 <see cref="EjoyFramework.Core.Serialization.ByteBuffer"/>）。
    /// 与 <see cref="ISaveSerializer"/> 互补：业务可选择高性能、紧凑的二进制存档而非 JSON。
    /// 由 <see cref="ISaveManager.SetBinarySerializer"/> 注入；约定 <see cref="Serialize"/> 把对象逐字段写入
    /// buffer，<see cref="Deserialize"/> 以相同线序读回。SaveManager 负责把字节转 Base64 存入信封并还原。
    /// </summary>
    public interface ISaveBinarySerializer
    {
        /// <summary>把业务对象写入 buffer（追加到当前写游标）。</summary>
        void Serialize(object obj, EjoyFramework.Core.Serialization.ByteBuffer buffer);

        /// <summary>从 buffer 读回指定类型的业务对象（从当前读游标）。</summary>
        object Deserialize(System.Type type, EjoyFramework.Core.Serialization.ByteBuffer buffer);
    }

    /// <summary>可选加密层。</summary>
    public interface ISaveCryptoHelper
    {
        byte[] Encrypt(byte[] plain);
        byte[] Decrypt(byte[] cipher);
    }
}
