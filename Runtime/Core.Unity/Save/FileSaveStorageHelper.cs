//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using EjoyFramework.Core.Save;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 基于文件系统的 ISaveStorageHelper：存档放在 Application.persistentDataPath/saves/slot_N.sav
    /// </summary>
    public sealed class FileSaveStorageHelper : ISaveStorageHelper, ISaveBackupStorage
    {
        private const string DirectoryName = "saves";
        private const string FilePrefix = "slot_";
        private const string FileExt = ".sav";
        private const string TmpExt = ".tmp";
        private const string BakExt = ".bak";

        private readonly string m_Root;

        public FileSaveStorageHelper(string rootOverride = null)
        {
            m_Root = string.IsNullOrEmpty(rootOverride)
                ? Path.Combine(Application.persistentDataPath, DirectoryName)
                : rootOverride;
            if (!Directory.Exists(m_Root)) Directory.CreateDirectory(m_Root);
        }

        public bool Exists(int slotId) => File.Exists(GetPath(slotId));
        public byte[] Read(int slotId) => File.ReadAllBytes(GetPath(slotId));

        /// <summary>
        /// 原子写入：先写 &lt;path&gt;.tmp 并 flush 到磁盘，再原子替换正式文件。
        /// 目标已存在时用 File.Replace（同时把旧的有效文件保留为 &lt;path&gt;.bak）；
        /// 不存在时 File.Move。任何失败都清理临时文件，保证正式存档永不被半截写入破坏。
        /// </summary>
        public void Write(int slotId, byte[] payload)
        {
            string path = GetPath(slotId);
            string tmp = path + TmpExt;
            string bak = path + BakExt;
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(payload, 0, payload.Length);
                    fs.Flush(true);
                }

                if (File.Exists(path))
                {
                    // 原子替换；上一份有效存档保留为 .bak 供 LoadSlot 回退
                    File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            catch
            {
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { /* 清理失败不掩盖原始异常 */ }
                }
                throw;
            }
        }

        /// <summary>
        /// 删除指定槽位的正式存档及其 .bak / .tmp 残留。
        /// 三个删除各自独立 try/catch：任一失败（如文件被占用）仅 FrameworkLog.Warning 并继续，
        /// 不会因主文件删除抛出而跳过后续 .bak / .tmp 清理。
        /// 返回值表示删除前主文件是否存在（即便其删除失败也照实返回，保持 existed 契约）。
        /// </summary>
        public bool Delete(int slotId)
        {
            string path = GetPath(slotId);
            bool existed = File.Exists(path);
            if (existed) { try { File.Delete(path); } catch (System.Exception ex) { FrameworkLog.Warning("Delete save '{0}' threw: {1}", path, ex); } }
            string bak = path + BakExt;
            if (File.Exists(bak)) { try { File.Delete(bak); } catch (System.Exception ex) { FrameworkLog.Warning("Delete backup '{0}' threw: {1}", bak, ex); } }
            string tmp = path + TmpExt;
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch (System.Exception ex) { FrameworkLog.Warning("Delete tmp '{0}' threw: {1}", tmp, ex); } }
            return existed;
        }

        /// <summary>读取 .bak 备份字节；不存在返回 null（供 SaveManager 在主存档损坏时回退）。</summary>
        public byte[] ReadBackup(int slotId)
        {
            string bak = GetPath(slotId) + BakExt;
            return File.Exists(bak) ? File.ReadAllBytes(bak) : null;
        }

        /// <summary>是否存在 .bak 备份。</summary>
        public bool BackupExists(int slotId) => File.Exists(GetPath(slotId) + BakExt);

        public int[] EnumerateSlotIds()
        {
            if (!Directory.Exists(m_Root)) return Array.Empty<int>();
            var ids = new List<int>();
            foreach (var f in Directory.GetFiles(m_Root, FilePrefix + "*" + FileExt))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.StartsWith(FilePrefix) && int.TryParse(name.Substring(FilePrefix.Length), out int id))
                {
                    ids.Add(id);
                }
            }
            return ids.ToArray();
        }

        private string GetPath(int slotId) => Path.Combine(m_Root, FilePrefix + slotId + FileExt);
    }
}
