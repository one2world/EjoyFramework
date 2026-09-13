//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Save;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 存档组件。新存档默认使用支持集合与 Dictionary 的 Newtonsoft codec；
    /// JsonUtility serializer 仅保留用于读取 ESV1 旧存档。
    /// 业务可在 Inspector 启用 AES 加密；passphrase 必须通过环境变量
    /// EJOY_SAVE_PASSPHRASE 或代码 <see cref="SetPassphrase"/> 在运行时注入，
    /// 绝不在资源中烘焙明文默认值。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Save")]
    public sealed class SaveComponent : GameFrameworkComponent
    {
        /// <summary>读取 passphrase 的环境变量名（CI / 启动脚本注入）。</summary>
        public const string PassphraseEnvVar = "EJOY_SAVE_PASSPHRASE";

        [SerializeField] private int m_CurrentSchemaVersion = 1;
        [SerializeField] private bool m_EnableEncryption = false;
        [SerializeField]
        [Tooltip("不要在此填写明文 passphrase！启用加密时请通过环境变量 EJOY_SAVE_PASSPHRASE 或代码 SetPassphrase() 注入。此字段保持为空，仅作占位。")]
        private string m_Passphrase = string.Empty;

        private ISaveManager m_SaveManager;

        public ISaveManager Manager => m_SaveManager;

        /// <summary>
        /// 运行时注入加密 passphrase（推荐：在 Awake 之后、Start 之前调用，例如启动流程里）。
        /// 优先级高于环境变量。
        /// </summary>
        public void SetPassphrase(string passphrase)
        {
            m_Passphrase = passphrase ?? string.Empty;
        }

        protected override void Awake()
        {
            base.Awake();
            m_SaveManager = Framework.GetModule<ISaveManager>();
            if (m_SaveManager == null) { Log.Fatal("Save manager is invalid."); return; }

            // Storage and codecs must be ready before BaseComponent.Start performs hard module validation.
            m_SaveManager.SetStorageHelper(new FileSaveStorageHelper());
            m_SaveManager.SetSerializer(new JsonUtilitySaveSerializer());
            m_SaveManager.SetCodec(new NewtonsoftJsonSaveCodec());
            m_SaveManager.SetCurrentVersion(m_CurrentSchemaVersion);
        }

        private void Start()
        {
            if (m_SaveManager == null) return;
            if (m_EnableEncryption)
            {
                string passphrase = ResolvePassphrase();
                if (string.IsNullOrEmpty(passphrase))
                {
                    throw new FrameworkException(
                        "SaveComponent: encryption enabled but no passphrase provided. " +
                        "Set env var '" + PassphraseEnvVar + "' or call SetPassphrase() before Start.");
                }
                byte[] key = AesSaveCryptoHelper.DeriveKey(passphrase);
                m_SaveManager.SetCryptoHelper(new AesSaveCryptoHelper(key));
                // 同一 passphrase 派生独立 salt 的 HMAC 密钥，作为防篡改门。
                m_SaveManager.SetIntegritySecret(AesSaveCryptoHelper.DeriveKey(passphrase, IntegritySalt));
            }
            else
            {
                // 无密钥：完整性退化为 best-effort CRC 损坏检测，不防篡改。
                m_SaveManager.SetIntegritySecret(null);
            }
        }

        // HMAC 密钥与 AES 密钥使用不同 salt，避免两者相等。
        private static readonly byte[] IntegritySalt =
            System.Text.Encoding.UTF8.GetBytes("EjoyFramework.Core.Save.Integrity.v1");

        private string ResolvePassphrase()
        {
            if (!string.IsNullOrEmpty(m_Passphrase)) return m_Passphrase;
            try { return Environment.GetEnvironmentVariable(PassphraseEnvVar); }
            catch (Exception ex)
            {
                Log.Warning("SaveComponent: failed to read env var {0}: {1}", PassphraseEnvVar, ex.Message);
                return null;
            }
        }

        // ===== 业务方便捷转发 =====

        public bool Write<T>(int slotId, T data, SaveSlotMetadata meta = null) where T : class
            => m_SaveManager.WriteSlot(slotId, data, meta ?? new SaveSlotMetadata());

        public T Load<T>(int slotId) where T : class
            => m_SaveManager.LoadSlot<T>(slotId);

        public SaveSlot[] AllSlots => m_SaveManager.GetAllSlots();
        public bool Has(int slotId) => m_SaveManager.HasSlot(slotId);
        public bool Delete(int slotId) => m_SaveManager.DeleteSlot(slotId);
        public void DeleteAll() => m_SaveManager.DeleteAllSlots();

        public void SetMigrator(SaveMigrator migrator) => m_SaveManager.SetMigrator(migrator);
    }
}
