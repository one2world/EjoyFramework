//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Save
{
    /// <summary>
    /// 链式存档迁移器。
    ///
    /// 用法：
    /// <code>
    /// var mig = new SaveMigrator();
    /// mig.Register(fromVersion: 1, toVersion: 2, json =>
    /// {
    ///     // 解析 v1 json → 创建 v2 json
    ///     var v1 = JsonUtility.FromJson&lt;V1Save&gt;(json);
    ///     var v2 = new V2Save { Name = v1.PlayerName, NewField = "default" };
    ///     return JsonUtility.ToJson(v2);
    /// });
    /// mig.Register(2, 3, ...);
    /// </code>
    ///
    /// 加载流程：若存档 version=1，currentVersion=3，则自动走 1→2→3 两步。
    /// </summary>
    public sealed class SaveMigrator
    {
        private readonly Dictionary<int, MigrationStep> m_Steps = new Dictionary<int, MigrationStep>();
        private readonly Dictionary<int, BinaryMigrationStep> m_BinarySteps = new Dictionary<int, BinaryMigrationStep>();

        /// <summary>
        /// 注册从 fromVersion → fromVersion+1 的迁移步。
        /// </summary>
        public void Register(int fromVersion, Func<string, string> migrate)
        {
            if (migrate == null) throw new FrameworkException("Migrate delegate is null.");
            if (m_Steps.ContainsKey(fromVersion))
                throw new FrameworkException(string.Format("Migration from version {0} already registered.", fromVersion));
            m_Steps[fromVersion] = new MigrationStep { FromVersion = fromVersion, Migrate = migrate };
        }

        /// <summary>
        /// 注册二进制载荷从 fromVersion → fromVersion+1 的迁移步。
        /// 输入和输出均为解码 Base64 后的原始业务数据字节，不包含 SaveEnvelope。
        /// </summary>
        public void RegisterBinary(int fromVersion, Func<byte[], byte[]> migrate)
        {
            RegisterPayload(fromVersion, migrate);
        }

        /// <summary>
        /// Registers a format-neutral ESV2 payload migration step from fromVersion to fromVersion + 1.
        /// The callback receives exactly the bytes produced by the slot's codec.
        /// </summary>
        public void RegisterPayload(int fromVersion, Func<byte[], byte[]> migrate)
        {
            if (migrate == null) throw new FrameworkException("Binary migrate delegate is null.");
            if (m_BinarySteps.ContainsKey(fromVersion))
                throw new FrameworkException(string.Format("Binary migration from version {0} already registered.", fromVersion));
            m_BinarySteps[fromVersion] = new BinaryMigrationStep { FromVersion = fromVersion, Migrate = migrate };
        }

        /// <summary>
        /// 把 dataJson 从 fromVersion 迁移到 toVersion。失败抛 FrameworkException。
        /// </summary>
        public string Migrate(string dataJson, int fromVersion, int toVersion)
        {
            if (fromVersion == toVersion) return dataJson;
            if (fromVersion > toVersion)
                throw new FrameworkException(string.Format(
                    "Save is from newer version (v{0}) than current schema (v{1}); downgrade not supported.",
                    fromVersion, toVersion));

            string current = dataJson;
            int version = fromVersion;
            while (version < toVersion)
            {
                if (!m_Steps.TryGetValue(version, out var step))
                    throw new FrameworkException(string.Format(
                        "No migration step registered for version {0} → {1}. Add SaveMigrator.Register({0}, ...).",
                        version, version + 1));
                try
                {
                    current = step.Migrate(current);
                }
                catch (Exception ex)
                {
                    throw new FrameworkException(string.Format(
                        "Migration step {0} → {1} threw: {2}", version, version + 1, ex.Message), ex);
                }
                version++;
            }
            return current;
        }

        /// <summary>
        /// 把二进制业务载荷从 fromVersion 迁移到 toVersion。失败抛 FrameworkException。
        /// </summary>
        public byte[] MigrateBinary(byte[] data, int fromVersion, int toVersion)
        {
            return MigratePayload(data, fromVersion, toVersion);
        }

        /// <summary>Migrates format-neutral ESV2 payload bytes between schema versions.</summary>
        public byte[] MigratePayload(byte[] data, int fromVersion, int toVersion)
        {
            if (data == null) throw new FrameworkException("Binary save data is null.");
            if (fromVersion == toVersion) return data;
            if (fromVersion > toVersion)
                throw new FrameworkException(string.Format(
                    "Save is from newer version (v{0}) than current schema (v{1}); downgrade not supported.",
                    fromVersion, toVersion));

            byte[] current = data;
            int version = fromVersion;
            while (version < toVersion)
            {
                if (!m_BinarySteps.TryGetValue(version, out var step))
                    throw new FrameworkException(string.Format(
                        "No binary migration step registered for version {0} → {1}. Add SaveMigrator.RegisterBinary({0}, ...).",
                        version, version + 1));
                try
                {
                    byte[] migrated = step.Migrate(current);
                    if (migrated == null)
                        throw new FrameworkException(string.Format(
                            "Binary migration step {0} → {1} returned null.", version, version + 1));
                    current = migrated;
                }
                catch (Exception ex)
                {
                    throw new FrameworkException(string.Format(
                        "Binary migration step {0} → {1} threw: {2}", version, version + 1, ex.Message), ex);
                }
                version++;
            }
            return current;
        }

        private sealed class MigrationStep
        {
            public int FromVersion;
            public Func<string, string> Migrate;
        }

        private sealed class BinaryMigrationStep
        {
            public int FromVersion;
            public Func<byte[], byte[]> Migrate;
        }
    }
}
