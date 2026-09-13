//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EjoyFramework.Core.Serialization;

namespace EjoyFramework.Core.Save
{
    /// <summary>
    /// Format-neutral save manager.
    /// New writes configured through <see cref="SetCodec"/> use the ESV2 binary envelope.
    /// The ESV1 JSON envelope remains readable and writable through the legacy serializer APIs.
    /// </summary>
    internal sealed class SaveManager : FrameworkModule, ISaveManager
    {
        private static readonly byte[] s_LegacyMagic = { (byte)'E', (byte)'S', (byte)'V', (byte)'1' };
        private static readonly byte[] s_CurrentMagic = { (byte)'E', (byte)'S', (byte)'V', (byte)'2' };
        private const byte LegacyFrameVersion = 1;
        private const byte CurrentFrameVersion = 2;
        private const byte BinaryEnvelopeVersion = 1;
        private const int HmacSize = 32;

        private readonly Dictionary<string, ISaveCodec> m_Codecs =
            new Dictionary<string, ISaveCodec>(StringComparer.Ordinal);

        private ISaveStorageHelper m_Storage;
        private ISaveCryptoHelper m_Crypto;
        private ISaveSerializer m_LegacySerializer;
        private ISaveBinarySerializer m_LegacyBinarySerializer;
        private SaveMigrator m_Migrator;
        private byte[] m_IntegritySecret;
        private string m_DefaultCodecId;
        private int m_CurrentVersion = 1;

        public event Action<int, string> SaveFailed;
        public event Action<int, string> SaveCorrupted;

        public override int Priority { get { return 0; } }
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Storage != null; } }
        public override string ConfigurationHint
        {
            get { return "SaveManager needs ISaveStorageHelper. Configure SaveComponent or call SetStorageHelper before use."; }
        }

        public override void Update(float a, float b) { }
        public override void Shutdown() { }

        public void SetStorageHelper(ISaveStorageHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetStorageHelper));
            if (helper == null) throw new FrameworkException("Storage helper is invalid.");
            m_Storage = helper;
        }

        public void SetCodec(ISaveCodec codec)
        {
            Framework.EnsureMainThread(nameof(SetCodec));
            RegisterCodecInternal(codec);
            m_DefaultCodecId = codec.Id;
        }

        public void RegisterCodec(ISaveCodec codec)
        {
            Framework.EnsureMainThread(nameof(RegisterCodec));
            RegisterCodecInternal(codec);
        }

        public void SetDefaultCodec(string codecId)
        {
            Framework.EnsureMainThread(nameof(SetDefaultCodec));
            if (string.IsNullOrWhiteSpace(codecId)) throw new FrameworkException("Codec id is invalid.");
            if (!m_Codecs.ContainsKey(codecId))
                throw new FrameworkException(string.Format("Save codec '{0}' is not registered.", codecId));
            m_DefaultCodecId = codecId;
        }

        /// <summary>Legacy ESV1 text serializer. New code should use <see cref="SetCodec"/>.</summary>
        public void SetSerializer(ISaveSerializer serializer)
        {
            Framework.EnsureMainThread(nameof(SetSerializer));
            if (serializer == null) throw new FrameworkException("Serializer is invalid.");
            m_LegacySerializer = serializer;
        }

        /// <summary>Legacy ESV1 ByteBuffer serializer. New code should use <see cref="SetCodec"/>.</summary>
        public void SetBinarySerializer(ISaveBinarySerializer serializer)
        {
            Framework.EnsureMainThread(nameof(SetBinarySerializer));
            m_LegacyBinarySerializer = serializer;
        }

        public void SetCryptoHelper(ISaveCryptoHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetCryptoHelper));
            m_Crypto = helper;
        }

        public void SetIntegritySecret(byte[] secret)
        {
            Framework.EnsureMainThread(nameof(SetIntegritySecret));
            m_IntegritySecret = secret != null && secret.Length > 0 ? (byte[])secret.Clone() : null;
        }

        public void SetMigrator(SaveMigrator migrator)
        {
            Framework.EnsureMainThread(nameof(SetMigrator));
            m_Migrator = migrator;
        }

        public void SetCurrentVersion(int currentVersion)
        {
            if (currentVersion < 1) throw new FrameworkException("currentVersion must be >= 1.");
            m_CurrentVersion = currentVersion;
        }

        public int SlotCount
        {
            get { return m_Storage != null ? m_Storage.EnumerateSlotIds().Length : 0; }
        }

        public SaveSlot[] GetAllSlots()
        {
            EnsureReadyForRead();
            int[] ids = m_Storage.EnumerateSlotIds();
            var slots = new List<SaveSlot>(ids.Length);
            foreach (int id in ids)
            {
                SaveSlot slot = TryReadEnvelopeMetadata(id);
                if (slot != null) slots.Add(slot);
            }
            return slots.ToArray();
        }

        public bool HasSlot(int slotId)
        {
            return m_Storage != null && m_Storage.Exists(slotId);
        }

        public bool WriteSlot<T>(int slotId, T data, SaveSlotMetadata metadata) where T : class
        {
            Framework.EnsureMainThread(nameof(WriteSlot));
            EnsureReadyForWrite();
            try
            {
                byte[] plain = m_DefaultCodecId != null
                    ? EncodeCurrentSlot(data, typeof(T), metadata)
                    : EncodeLegacySlot(data, metadata);
                byte[] stored = m_Crypto != null ? m_Crypto.Encrypt(plain) : plain;
                m_Storage.Write(slotId, stored);
                return true;
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("SaveManager.WriteSlot({0}) failed: {1}", slotId, ex);
                Action<int, string> handler = SaveFailed;
                if (handler != null)
                {
                    try { handler(slotId, ex.Message); }
                    catch (Exception handlerError) { FrameworkLog.Error("SaveFailed handler threw: {0}", handlerError); }
                }
                return false;
            }
        }

        public T LoadSlot<T>(int slotId) where T : class
        {
            Framework.EnsureMainThread(nameof(LoadSlot));
            EnsureReadyForRead();
            if (!m_Storage.Exists(slotId)) return default(T);

            string primaryError;
            T primary = TryDecodeSlot<T>(m_Storage.Read(slotId), out primaryError);
            if (primaryError == null) return primary;

            var backupStore = m_Storage as ISaveBackupStorage;
            if (backupStore != null && backupStore.BackupExists(slotId))
            {
                byte[] backupBytes = backupStore.ReadBackup(slotId);
                if (backupBytes != null)
                {
                    string backupError;
                    T backup = TryDecodeSlot<T>(backupBytes, out backupError);
                    if (backupError == null)
                    {
                        FrameworkLog.Warning("Save slot {0} primary failed ({1}); recovered from backup.", slotId, primaryError);
                        return backup;
                    }
                    FireCorrupted(slotId, string.Format(
                        "Primary failed ({0}); backup also failed ({1}).", primaryError, backupError));
                    return default(T);
                }
            }

            FireCorrupted(slotId, primaryError);
            return default(T);
        }

        public bool DeleteSlot(int slotId)
        {
            Framework.EnsureMainThread(nameof(DeleteSlot));
            EnsureStorage();
            return m_Storage.Delete(slotId);
        }

        public void DeleteAllSlots()
        {
            Framework.EnsureMainThread(nameof(DeleteAllSlots));
            EnsureStorage();
            foreach (int id in m_Storage.EnumerateSlotIds())
            {
                try { m_Storage.Delete(id); }
                catch (Exception ex) { FrameworkLog.Error("DeleteAllSlots: delete {0} threw: {1}", id, ex); }
            }
        }

        private byte[] EncodeCurrentSlot(object data, Type dataType, SaveSlotMetadata metadata)
        {
            ISaveCodec codec = m_Codecs[m_DefaultCodecId];
            byte[] businessPayload = codec.Encode(data, dataType);
            if (businessPayload == null)
                throw new FrameworkException(string.Format("Save codec '{0}' returned null payload.", codec.Id));

            byte[] envelope = EncodeBinaryEnvelope(new BinaryEnvelope
            {
                Version = m_CurrentVersion,
                CodecId = codec.Id,
                Metadata = metadata,
                Payload = businessPayload,
                WriteUnixSeconds = UnixNow(),
            });
            return FrameWithHmac(envelope, s_CurrentMagic, CurrentFrameVersion);
        }

        private byte[] EncodeLegacySlot(object data, SaveSlotMetadata metadata)
        {
            if (m_LegacySerializer == null)
                throw new FrameworkException("SaveManager: no default codec configured. Call SetCodec; SetSerializer is supported only for ESV1 compatibility.");

            string dataPayload;
            int format;
            if (m_LegacyBinarySerializer != null)
            {
                ByteBuffer buffer = ByteBuffer.Acquire();
                try
                {
                    m_LegacyBinarySerializer.Serialize(data, buffer);
                    dataPayload = Convert.ToBase64String(buffer.ToArray());
                }
                finally
                {
                    buffer.Release();
                }
                format = 1;
            }
            else
            {
                dataPayload = m_LegacySerializer.Serialize(data);
                format = 0;
            }

            var envelope = new SaveEnvelope
            {
                Version = m_CurrentVersion,
                Metadata = metadata,
                DataJson = dataPayload ?? string.Empty,
                WriteUnixSeconds = UnixNow(),
                Format = format,
            };
            envelope.Crc32 = ComputeLegacyCrc(envelope);
            byte[] bytes = Encoding.UTF8.GetBytes(m_LegacySerializer.Serialize(envelope));
            return FrameWithHmac(bytes, s_LegacyMagic, LegacyFrameVersion);
        }

        private T TryDecodeSlot<T>(byte[] storedPayload, out string error) where T : class
        {
            try
            {
                if (storedPayload == null) { error = "Payload is null."; return default(T); }
                byte[] plain = m_Crypto != null ? m_Crypto.Decrypt(storedPayload) : storedPayload;

                if (HasMagic(plain, s_CurrentMagic))
                    return DecodeCurrentSlot<T>(plain, out error);
                if (HasMagic(plain, s_LegacyMagic))
                    return DecodeLegacySlot<T>(plain, out error);

                error = "Unknown save format magic.";
                return default(T);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return default(T);
            }
        }

        private T DecodeCurrentSlot<T>(byte[] plain, out string error) where T : class
        {
            if (!TryUnframeHmac(plain, s_CurrentMagic, CurrentFrameVersion, out byte[] envelopeBytes, out error))
                return default(T);

            BinaryEnvelope envelope = DecodeBinaryEnvelope(envelopeBytes);
            if (!m_Codecs.TryGetValue(envelope.CodecId, out ISaveCodec codec))
            {
                error = string.Format(
                    "Save codec '{0}' is not registered. Register it before loading this slot.", envelope.CodecId);
                return default(T);
            }

            byte[] payload = envelope.Payload;
            if (envelope.Version != m_CurrentVersion)
            {
                if (m_Migrator == null)
                {
                    error = string.Format(
                        "Save v{0} requires migration to current v{1}, but no SaveMigrator is configured.",
                        envelope.Version, m_CurrentVersion);
                    return default(T);
                }
                payload = m_Migrator.MigratePayload(payload, envelope.Version, m_CurrentVersion);
            }

            object value = codec.Decode(payload, typeof(T));
            error = null;
            return value as T;
        }

        private T DecodeLegacySlot<T>(byte[] plain, out string error) where T : class
        {
            if (m_LegacySerializer == null)
            {
                error = "ESV1 save requires a legacy ISaveSerializer. Call SetSerializer before loading.";
                return default(T);
            }
            if (!TryUnframeHmac(plain, s_LegacyMagic, LegacyFrameVersion, out byte[] envelopeBytes, out error))
                return default(T);

            string envelopeJson = Encoding.UTF8.GetString(envelopeBytes);
            SaveEnvelope envelope = m_LegacySerializer.Deserialize<SaveEnvelope>(envelopeJson);
            if (envelope == null) { error = "Legacy envelope deserialization returned null."; return default(T); }

            uint expected = ComputeLegacyCrc(envelope);
            if (envelope.Crc32 != expected)
            {
                error = string.Format("CRC mismatch: expected {0:X8}, got {1:X8}.", expected, envelope.Crc32);
                return default(T);
            }

            if (envelope.Format == 1)
            {
                if (m_LegacyBinarySerializer == null)
                {
                    error = "Legacy binary save requires ISaveBinarySerializer.";
                    return default(T);
                }
                byte[] bytes = Convert.FromBase64String(envelope.DataJson ?? string.Empty);
                if (envelope.Version != m_CurrentVersion)
                {
                    if (m_Migrator == null) { error = "Legacy binary save requires a migration chain."; return default(T); }
                    bytes = m_Migrator.MigrateBinary(bytes, envelope.Version, m_CurrentVersion);
                }
                ByteBuffer buffer = ByteBuffer.Acquire(bytes);
                try
                {
                    error = null;
                    return m_LegacyBinarySerializer.Deserialize(typeof(T), buffer) as T;
                }
                finally
                {
                    buffer.Release();
                }
            }

            string dataJson = envelope.DataJson;
            if (envelope.Version != m_CurrentVersion)
            {
                if (m_Migrator == null) { error = "Legacy text save requires a migration chain."; return default(T); }
                dataJson = m_Migrator.Migrate(dataJson, envelope.Version, m_CurrentVersion);
            }
            error = null;
            return m_LegacySerializer.Deserialize<T>(dataJson);
        }

        private SaveSlot TryReadEnvelopeMetadata(int slotId)
        {
            try
            {
                byte[] stored = m_Storage.Read(slotId);
                if (stored == null) return null;
                byte[] plain = m_Crypto != null ? m_Crypto.Decrypt(stored) : stored;

                if (HasMagic(plain, s_CurrentMagic))
                {
                    if (!TryUnframeHmac(plain, s_CurrentMagic, CurrentFrameVersion, out byte[] bytes, out string frameError))
                        throw new FrameworkException(frameError);
                    BinaryEnvelope envelope = DecodeBinaryEnvelope(bytes);
                    return ToSaveSlot(slotId, stored.LongLength, envelope);
                }

                if (HasMagic(plain, s_LegacyMagic) && m_LegacySerializer != null)
                {
                    if (!TryUnframeHmac(plain, s_LegacyMagic, LegacyFrameVersion, out byte[] bytes, out string frameError))
                        throw new FrameworkException(frameError);
                    SaveEnvelope envelope = m_LegacySerializer.Deserialize<SaveEnvelope>(Encoding.UTF8.GetString(bytes));
                    if (envelope == null || envelope.Crc32 != ComputeLegacyCrc(envelope)) return null;
                    return new SaveSlot
                    {
                        SlotId = slotId,
                        Metadata = envelope.Metadata,
                        Version = envelope.Version,
                        CodecId = envelope.Format == 1 ? "legacy-bytebuffer-v1" : "legacy-json-v1",
                        ByteSize = stored.LongLength,
                        LastWriteUnixSeconds = envelope.WriteUnixSeconds,
                    };
                }
            }
            catch (Exception ex)
            {
                FrameworkLog.Warning("Failed to read slot {0} metadata: {1}", slotId, ex.Message);
            }
            return null;
        }

        private static SaveSlot ToSaveSlot(int slotId, long byteSize, BinaryEnvelope envelope)
        {
            return new SaveSlot
            {
                SlotId = slotId,
                Metadata = envelope.Metadata,
                Version = envelope.Version,
                CodecId = envelope.CodecId,
                ByteSize = byteSize,
                LastWriteUnixSeconds = envelope.WriteUnixSeconds,
            };
        }

        private static byte[] EncodeBinaryEnvelope(BinaryEnvelope envelope)
        {
            byte[] content;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(BinaryEnvelopeVersion);
                writer.Write(envelope.Version);
                writer.Write(envelope.WriteUnixSeconds);
                WriteNullableString(writer, envelope.CodecId);
                WriteMetadata(writer, envelope.Metadata);
                writer.Write(envelope.Payload.Length);
                writer.Write(envelope.Payload);
                writer.Flush();
                content = stream.ToArray();
            }

            using (var stream = new MemoryStream(content.Length + sizeof(uint)))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Crc32(content));
                writer.Write(content);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static BinaryEnvelope DecodeBinaryEnvelope(byte[] bytes)
        {
            if (bytes == null || bytes.Length < sizeof(uint) + 1)
                throw new FrameworkException("ESV2 envelope is truncated.");

            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                uint storedCrc = reader.ReadUInt32();
                byte[] content = reader.ReadBytes((int)(stream.Length - stream.Position));
                uint actualCrc = Crc32(content);
                if (storedCrc != actualCrc)
                    throw new FrameworkException(string.Format(
                        "CRC mismatch: expected {0:X8}, got {1:X8}.", storedCrc, actualCrc));

                using (var contentStream = new MemoryStream(content, false))
                using (var contentReader = new BinaryReader(contentStream, Encoding.UTF8))
                {
                    byte envelopeVersion = contentReader.ReadByte();
                    if (envelopeVersion != BinaryEnvelopeVersion)
                        throw new FrameworkException(string.Format("Unsupported ESV2 envelope version {0}.", envelopeVersion));

                    var envelope = new BinaryEnvelope
                    {
                        Version = contentReader.ReadInt32(),
                        WriteUnixSeconds = contentReader.ReadInt64(),
                        CodecId = ReadNullableString(contentReader),
                        Metadata = ReadMetadata(contentReader),
                    };
                    if (string.IsNullOrWhiteSpace(envelope.CodecId))
                        throw new FrameworkException("ESV2 envelope codec id is missing.");

                    int payloadLength = contentReader.ReadInt32();
                    long remaining = contentStream.Length - contentStream.Position;
                    if (payloadLength < 0 || payloadLength > remaining)
                        throw new FrameworkException("ESV2 envelope payload length is invalid.");
                    envelope.Payload = contentReader.ReadBytes(payloadLength);
                    return envelope;
                }
            }
        }

        private byte[] FrameWithHmac(byte[] envelopeBytes, byte[] magic, byte frameVersion)
        {
            byte[] mac = m_IntegritySecret != null ? ComputeHmac(envelopeBytes) : Array.Empty<byte>();
            int headerLength = magic.Length + 2;
            byte[] framed = new byte[headerLength + mac.Length + envelopeBytes.Length];
            Buffer.BlockCopy(magic, 0, framed, 0, magic.Length);
            framed[magic.Length] = frameVersion;
            framed[magic.Length + 1] = (byte)mac.Length;
            if (mac.Length > 0) Buffer.BlockCopy(mac, 0, framed, headerLength, mac.Length);
            Buffer.BlockCopy(envelopeBytes, 0, framed, headerLength + mac.Length, envelopeBytes.Length);
            return framed;
        }

        private bool TryUnframeHmac(
            byte[] framed,
            byte[] expectedMagic,
            byte expectedFrameVersion,
            out byte[] envelopeBytes,
            out string error)
        {
            envelopeBytes = null;
            int headerLength = expectedMagic.Length + 2;
            if (framed == null || framed.Length < headerLength || !HasMagic(framed, expectedMagic))
            {
                error = "Save frame magic is invalid.";
                return false;
            }
            if (framed[expectedMagic.Length] != expectedFrameVersion)
            {
                error = string.Format("Unsupported save frame version {0}.", framed[expectedMagic.Length]);
                return false;
            }

            int macLength = framed[expectedMagic.Length + 1];
            if (framed.Length < headerLength + macLength)
            {
                error = "Save frame is shorter than its declared HMAC length.";
                return false;
            }
            int dataOffset = headerLength + macLength;
            int dataLength = framed.Length - dataOffset;
            var data = new byte[dataLength];
            Buffer.BlockCopy(framed, dataOffset, data, 0, dataLength);

            if (m_IntegritySecret != null)
            {
                if (macLength != HmacSize)
                {
                    error = "HMAC is required but missing or invalid.";
                    return false;
                }
                byte[] expected = ComputeHmac(data);
                var actual = new byte[macLength];
                Buffer.BlockCopy(framed, headerLength, actual, 0, macLength);
                if (!FixedTimeEquals(expected, actual))
                {
                    error = "HMAC mismatch: save was modified or the integrity secret is wrong.";
                    return false;
                }
            }

            envelopeBytes = data;
            error = null;
            return true;
        }

        private void RegisterCodecInternal(ISaveCodec codec)
        {
            if (codec == null) throw new FrameworkException("Save codec is invalid.");
            if (string.IsNullOrWhiteSpace(codec.Id)) throw new FrameworkException("Save codec id is invalid.");
            m_Codecs[codec.Id] = codec;
        }

        private void EnsureReadyForWrite()
        {
            EnsureStorage();
            if (m_DefaultCodecId == null && m_LegacySerializer == null)
                throw new FrameworkException("SaveManager: no codec configured. Call SetCodec before writing.");
        }

        private void EnsureReadyForRead()
        {
            EnsureStorage();
        }

        private void EnsureStorage()
        {
            if (m_Storage == null) throw new FrameworkException("SaveManager: storage helper not set.");
        }

        private void FireCorrupted(int slotId, string error)
        {
            FrameworkLog.Error("Save slot {0} corrupted: {1}", slotId, error);
            Action<int, string> handler = SaveCorrupted;
            if (handler != null)
            {
                try { handler(slotId, error); }
                catch (Exception handlerError) { FrameworkLog.Error("SaveCorrupted handler threw: {0}", handlerError); }
            }
        }

        private byte[] ComputeHmac(byte[] data)
        {
            using (var hmac = new HMACSHA256(m_IntegritySecret))
            {
                return hmac.ComputeHash(data);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static bool HasMagic(byte[] bytes, byte[] magic)
        {
            if (bytes == null || bytes.Length < magic.Length) return false;
            for (int i = 0; i < magic.Length; i++)
            {
                if (bytes[i] != magic[i]) return false;
            }
            return true;
        }

        private static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static void WriteMetadata(BinaryWriter writer, SaveSlotMetadata metadata)
        {
            writer.Write(metadata != null);
            if (metadata == null) return;
            WriteNullableString(writer, metadata.PlayerName);
            WriteNullableString(writer, metadata.SceneOrChapter);
            writer.Write(metadata.Level);
            writer.Write(metadata.PlayedSeconds);
            WriteNullableString(writer, metadata.CustomTag);
        }

        private static SaveSlotMetadata ReadMetadata(BinaryReader reader)
        {
            if (!reader.ReadBoolean()) return null;
            return new SaveSlotMetadata
            {
                PlayerName = ReadNullableString(reader),
                SceneOrChapter = ReadNullableString(reader),
                Level = reader.ReadInt32(),
                PlayedSeconds = reader.ReadSingle(),
                CustomTag = ReadNullableString(reader),
            };
        }

        private static void WriteNullableString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null) writer.Write(value);
        }

        private static string ReadNullableString(BinaryReader reader)
        {
            return reader.ReadBoolean() ? reader.ReadString() : null;
        }

        private static uint ComputeLegacyCrc(SaveEnvelope envelope)
        {
            string canonical = envelope.Version + "|" + envelope.WriteUnixSeconds + "|"
                + MetadataCanonical(envelope.Metadata) + "|" + (envelope.DataJson ?? string.Empty);
            return Crc32(Encoding.UTF8.GetBytes(canonical));
        }

        private static string MetadataCanonical(SaveSlotMetadata metadata)
        {
            if (metadata == null) return "<null>";
            const string separator = "\u001f";
            return (metadata.PlayerName ?? string.Empty) + separator
                 + (metadata.SceneOrChapter ?? string.Empty) + separator
                 + metadata.Level + separator
                 + metadata.PlayedSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + separator
                 + (metadata.CustomTag ?? string.Empty);
        }

        private static readonly uint[] s_CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            const uint polynomial = 0xEDB88320u;
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1u) != 0 ? (value >> 1) ^ polynomial : value >> 1;
                table[i] = value;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                crc = (crc >> 8) ^ s_CrcTable[(crc ^ data[i]) & 0xFF];
            return crc ^ 0xFFFFFFFFu;
        }

        private sealed class BinaryEnvelope
        {
            public int Version;
            public string CodecId;
            public SaveSlotMetadata Metadata;
            public byte[] Payload;
            public long WriteUnixSeconds;
        }
    }
}
