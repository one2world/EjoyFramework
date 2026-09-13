//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Setting
{
    /// <summary>
    /// 游戏配置管理器。引擎无关，通过 ISettingHelper 注入持久化后端。
    /// </summary>
    public sealed class SettingManager : FrameworkModule, ISettingManager
    {
        private readonly Dictionary<string, string> m_Cache = new Dictionary<string, string>();
        private ISettingHelper m_Helper;

        /// <summary>
        /// 持久化保存失败事件（业务可监听以提示用户磁盘满 / 权限错误）。
        /// </summary>
        public event Action<string> SaveFailed;

        // Priority 0：玩家设置模块。
        public override int Priority { get { return 0; } }

        // 必需配置自检：依赖外部注入的 ISettingHelper 提供持久化后端（无 helper 时仅内存缓存，重启即丢失）。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Helper != null; } }
        public override string ConfigurationHint { get { return "SettingManager needs ISettingHelper — call GameEntry.Setting.SetHelper(...) (or the framework's helper-injection) before use."; } }

        public int Count
        {
            get { return m_Helper != null ? m_Helper.Count : m_Cache.Count; }
        }

        public void SetHelper(ISettingHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetHelper));
            m_Helper = helper;
        }

        public override void Update(float a, float b) { }

        public override void Shutdown()
        {
            // Shutdown 阶段 Save 失败不应阻塞清理，但要记录避免静默
            try { Save(); }
            catch (Exception ex) { FrameworkLog.Error("SettingManager.Shutdown: Save threw: {0}", ex); }
            m_Cache.Clear();
        }

        public bool HasSetting(string settingName)
        {
            if (string.IsNullOrEmpty(settingName)) return false;
            if (m_Helper != null) return m_Helper.Has(settingName);
            return m_Cache.ContainsKey(settingName);
        }

        private string GetRaw(string name, string defaultValue)
        {
            if (m_Helper != null)
            {
                string v;
                return m_Helper.TryGet(name, out v) ? v : defaultValue;
            }
            string c;
            return m_Cache.TryGetValue(name, out c) ? c : defaultValue;
        }

        private void SetRaw(string name, string value)
        {
            Framework.EnsureMainThread("SettingManager.Set");
            if (m_Helper != null) m_Helper.Set(name, value);
            else m_Cache[name] = value;
        }

        public bool GetBool(string n) { return GetBool(n, false); }
        public bool GetBool(string n, bool def) { string s = GetRaw(n, null); return s != null ? s == "1" : def; }
        public void SetBool(string n, bool v) { SetRaw(n, v ? "1" : "0"); }

        public int GetInt(string n) { return GetInt(n, 0); }
        public int GetInt(string n, int def) { int v; return int.TryParse(GetRaw(n, null), out v) ? v : def; }
        public void SetInt(string n, int v) { SetRaw(n, v.ToString(System.Globalization.CultureInfo.InvariantCulture)); }

        public float GetFloat(string n) { return GetFloat(n, 0f); }
        public float GetFloat(string n, float def) { float v; return float.TryParse(GetRaw(n, null), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def; }
        public void SetFloat(string n, float v) { SetRaw(n, v.ToString(System.Globalization.CultureInfo.InvariantCulture)); }

        public string GetString(string n) { return GetRaw(n, string.Empty); }
        public string GetString(string n, string def) { return GetRaw(n, def); }
        public void SetString(string n, string v) { SetRaw(n, v ?? string.Empty); }

        public T GetObject<T>(string n)
        {
            string s = GetRaw(n, null);
            if (string.IsNullOrEmpty(s)) return default(T);
            try
            {
                return Utility.Json.ToObject<T>(s);
            }
            catch (Exception ex)
            {
                // 持久化数据损坏不应从 getter 抛出，记录后返回默认值。
                FrameworkLog.Error("SettingManager.GetObject failed to deserialize '{0}': {1}", n, ex);
                return default(T);
            }
        }

        public void SetObject<T>(string n, T obj)
        {
            SetRaw(n, Utility.Json.ToJson(obj));
        }

        public bool RemoveSetting(string n)
        {
            Framework.EnsureMainThread(nameof(RemoveSetting));
            if (m_Helper != null) return m_Helper.Remove(n);
            return m_Cache.Remove(n);
        }

        public void RemoveAllSettings()
        {
            Framework.EnsureMainThread(nameof(RemoveAllSettings));
            if (m_Helper != null) m_Helper.RemoveAll();
            else m_Cache.Clear();
        }

        /// <summary>
        /// 持久化保存。失败时记录日志并触发 SaveFailed 事件，业务可监听以提示用户。
        /// </summary>
        public bool Save()
        {
            Framework.EnsureMainThread(nameof(Save));
            if (m_Helper == null) return true;
            try
            {
                m_Helper.Save();
                return true;
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("SettingManager.Save failed: {0}", ex);
                var h = SaveFailed;
                if (h != null)
                {
                    try { h(ex.Message); }
                    catch (Exception inner) { FrameworkLog.Error("SaveFailed handler threw: {0}", inner); }
                }
                return false;
            }
        }
    }
}
