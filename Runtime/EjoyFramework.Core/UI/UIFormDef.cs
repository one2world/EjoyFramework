//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.UI
{
    /// <summary>
    /// UIForm 元数据。把 (Id, AssetPath, Group, Priority, PauseCovered, Modal, AllowMultiple, Reusable) 一次配齐。
    /// 业务调 OpenUIForm(UIFormId.MainMenu) 即可，无须分散到各 Procedure 里硬编码字符串。
    /// </summary>
    public sealed class UIFormDef
    {
        public int Id { get; }
        public string AssetPath { get; }
        public string GroupName { get; }
        public int Priority { get; }
        public bool PauseCoveredUIForm { get; }
        /// <summary>是否模态：opening 时压下面所有 form 的输入。</summary>
        public bool Modal { get; }
        /// <summary>是否允许多实例同时打开。false 时第二次 Open 直接 Refocus 已存在的实例。</summary>
        public bool AllowMultiple { get; }
        /// <summary>
        /// 关闭后是否保留实例池缓存。
        /// true（默认）：关闭后 Unspawn 回实例池，下次 Open 命中复用。
        /// false：关闭后立即释放实例（不回池缓存），适合一次性使用的 form。
        /// 由 UIController.OpenByDef 贯通至 UIManager，关闭路径据此决定 Unspawn 后是否立即 ReleaseAllUnused。
        /// </summary>
        public bool Reusable { get; }

        public UIFormDef(int id, string assetPath, string groupName,
            int priority = 0, bool pauseCoveredUIForm = false,
            bool modal = false, bool allowMultiple = false, bool reusable = true)
        {
            if (id == 0) throw new FrameworkException("UIFormDef id 0 is reserved for invalid.");
            if (string.IsNullOrEmpty(assetPath)) throw new FrameworkException("UIFormDef AssetPath is invalid.");
            if (string.IsNullOrEmpty(groupName)) throw new FrameworkException("UIFormDef GroupName is invalid.");
            Id = id;
            AssetPath = assetPath;
            GroupName = groupName;
            Priority = priority;
            PauseCoveredUIForm = pauseCoveredUIForm;
            Modal = modal;
            AllowMultiple = allowMultiple;
            Reusable = reusable;
        }
    }

    /// <summary>
    /// UIForm 注册表。启动期一次性 Register；运行时 Get/Open by Id。
    /// </summary>
    public sealed class UIFormRegistry
    {
        private readonly Dictionary<int, UIFormDef> m_ById = new Dictionary<int, UIFormDef>();
        private readonly Dictionary<string, UIFormDef> m_ByAssetPath = new Dictionary<string, UIFormDef>(StringComparer.Ordinal);

        public int Count { get { return m_ById.Count; } }

        public void Register(UIFormDef def)
        {
            if (def == null) throw new FrameworkException("UIFormDef is invalid.");
            if (m_ById.ContainsKey(def.Id))
                throw new FrameworkException(Utility.Text.Format("Duplicate UIFormDef id={0}.", def.Id));
            if (m_ByAssetPath.ContainsKey(def.AssetPath))
                throw new FrameworkException(Utility.Text.Format("Duplicate UIFormDef asset path '{0}'.", def.AssetPath));
            m_ById.Add(def.Id, def);
            m_ByAssetPath.Add(def.AssetPath, def);
        }

        public bool TryGet(int id, out UIFormDef def) { return m_ById.TryGetValue(id, out def); }
        public UIFormDef Get(int id)
        {
            UIFormDef d;
            if (!m_ById.TryGetValue(id, out d))
                throw new FrameworkException(Utility.Text.Format("No UIFormDef registered for id={0}.", id));
            return d;
        }
        public UIFormDef GetByAsset(string path)
        {
            UIFormDef d;
            return m_ByAssetPath.TryGetValue(path, out d) ? d : null;
        }

        public void Clear() { m_ById.Clear(); m_ByAssetPath.Clear(); }
    }
}
