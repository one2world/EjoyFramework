//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Setting;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 游戏配置组件。
    /// 桥接 Layer 1 SettingManager + 注入 PlayerPrefs 后端 helper（Unity 特有持久化）。
    /// 业务代码可继续走 GameEntry.Setting.GetXxx，也可走 Framework.GetModule&lt;ISettingManager&gt;()。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Setting")]
    public sealed class SettingComponent : GameFrameworkComponent
    {
        private ISettingManager m_SettingManager;

        protected override void Awake()
        {
            base.Awake();
            m_SettingManager = Framework.GetModule<ISettingManager>();
            if (m_SettingManager == null)
            {
                Log.Fatal("Setting manager is invalid.");
                return;
            }

            // 通过反射调用 SetHelper（SettingManager 是 internal sealed，但 SetHelper 是 public）
            var concrete = m_SettingManager as SettingManager;
            if (concrete != null)
            {
                concrete.SetHelper(new PlayerPrefsSettingHelper());
            }
        }

        public bool HasSetting(string n) { return m_SettingManager.HasSetting(n); }

        public bool GetBool(string n, bool def = false) { return m_SettingManager.GetBool(n, def); }
        public void SetBool(string n, bool v) { m_SettingManager.SetBool(n, v); }

        public int GetInt(string n, int def = 0) { return m_SettingManager.GetInt(n, def); }
        public void SetInt(string n, int v) { m_SettingManager.SetInt(n, v); }

        public float GetFloat(string n, float def = 0f) { return m_SettingManager.GetFloat(n, def); }
        public void SetFloat(string n, float v) { m_SettingManager.SetFloat(n, v); }

        public string GetString(string n, string def = "") { return m_SettingManager.GetString(n, def); }
        public void SetString(string n, string v) { m_SettingManager.SetString(n, v); }

        public bool RemoveSetting(string n) { return m_SettingManager.RemoveSetting(n); }
        public void RemoveAllSettings() { m_SettingManager.RemoveAllSettings(); }
        /// <summary>保存配置；失败返回 false。业务可监听 m_SettingManager.SaveFailed 事件诊断细节。</summary>
        public bool Save() { return m_SettingManager.Save(); }

        /// <summary>
        /// 基于 Unity PlayerPrefs 的持久化 helper。
        /// PlayerPrefs 无法枚举 key，因此把已知 key 列表持久化在保留键
        /// <see cref="IndexKey"/> 下，并在 Set/Remove/RemoveAll 时同步，
        /// 这样 Count/Keys 在全新启动时也能反映真实持久化内容（而非内存里偶然 Get 过的子集）。
        /// </summary>
        private sealed class PlayerPrefsSettingHelper : ISettingHelper
        {
            // 存放 key 索引的保留键；用换行分隔（PlayerPrefs 值是字符串）。
            private const string IndexKey = "__ejoy_setting_keys__";
            private static readonly char[] IndexSeparator = { '\n' };

            private readonly HashSet<string> m_Keys = new HashSet<string>();

            public PlayerPrefsSettingHelper()
            {
                LoadIndex();
            }

            public int Count { get { return m_Keys.Count; } }

            public bool Has(string name) { return PlayerPrefs.HasKey(name); }

            public bool TryGet(string name, out string value)
            {
                if (!PlayerPrefs.HasKey(name)) { value = null; return false; }
                value = PlayerPrefs.GetString(name);
                if (m_Keys.Add(name)) SaveIndex();
                return true;
            }

            public void Set(string name, string value)
            {
                // key 索引以换行分隔持久化（见 SaveIndex/LoadIndex）。key 内若含 '\n'/'\r' 会污染索引、
                // 导致 Count/Keys 在重启后错乱。在写入边界直接拒绝，给出明确异常而非静默损坏。
                if (name != null && (name.IndexOf('\n') >= 0 || name.IndexOf('\r') >= 0))
                {
                    throw new FrameworkException(Utility.Text.Format(
                        "Setting key must not contain newline characters ('\\n' or '\\r'): '{0}'.", name));
                }

                PlayerPrefs.SetString(name, value ?? string.Empty);
                if (m_Keys.Add(name)) SaveIndex();
            }

            public bool Remove(string name)
            {
                if (!PlayerPrefs.HasKey(name)) return false;
                PlayerPrefs.DeleteKey(name);
                if (m_Keys.Remove(name)) SaveIndex();
                return true;
            }

            public void RemoveAll()
            {
                PlayerPrefs.DeleteAll();
                m_Keys.Clear();
                // DeleteAll 已清掉索引键，无需再写。
            }

            public void Save() { PlayerPrefs.Save(); }

            public IEnumerable<string> Keys() { return m_Keys; }

            private void LoadIndex()
            {
                if (!PlayerPrefs.HasKey(IndexKey)) return;
                string raw = PlayerPrefs.GetString(IndexKey);
                if (string.IsNullOrEmpty(raw)) return;
                foreach (var k in raw.Split(IndexSeparator, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    // 只保留仍真实存在的 key，剔除外部删除留下的陈旧条目。
                    if (PlayerPrefs.HasKey(k)) m_Keys.Add(k);
                }
            }

            private void SaveIndex()
            {
                PlayerPrefs.SetString(IndexKey, string.Join("\n", m_Keys));
            }
        }
    }
}
