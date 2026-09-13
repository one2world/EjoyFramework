//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Config;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 全局配置组件。完全转发给 ConfigManager；Awake 注入 ResourceManager + DefaultConfigHelper。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Config")]
    public sealed class ConfigComponent : GameFrameworkComponent
    {
        public event Action<string, float, object> LoadConfigSuccess;
        public event Action<string, string, object> LoadConfigFailure;

        private IConfigManager m_ConfigManager;
        private DefaultConfigHelper m_Helper;

        protected override void Awake()
        {
            base.Awake();
            m_ConfigManager = Framework.GetModule<IConfigManager>();
            if (m_ConfigManager == null) { Log.Fatal("Config manager is invalid."); return; }
            m_ConfigManager.LoadConfigSuccess += OnLoadSuccess;
            m_ConfigManager.LoadConfigFailure += OnLoadFailure;
            ConfigureManager();
        }

        private void ConfigureManager()
        {
            if (m_ConfigManager == null) return;
            IResourceManager rm = Framework.GetModule<IResourceManager>();
            if (rm == null) { Log.Fatal("Resource manager is invalid."); return; }
            // ConfigManager 自取 ResourceManager（构造注入用于测试，线上走 Framework.GetModule 懒拉取）
            m_Helper = new DefaultConfigHelper(rm);
            m_ConfigManager.SetConfigHelper(m_Helper);
        }

        protected override void OnDestroy()
        {
            if (m_ConfigManager != null)
            {
                m_ConfigManager.LoadConfigSuccess -= OnLoadSuccess;
                m_ConfigManager.LoadConfigFailure -= OnLoadFailure;
            }
            base.OnDestroy();
        }

        public int Count { get { return m_ConfigManager != null ? m_ConfigManager.Count : 0; } }
        public bool HasConfig(string n) { return m_ConfigManager != null && m_ConfigManager.HasConfig(n); }
        public bool GetBool(string n) { return m_ConfigManager != null && m_ConfigManager.GetBool(n); }
        public int GetInt(string n) { return m_ConfigManager != null ? m_ConfigManager.GetInt(n) : 0; }
        public float GetFloat(string n) { return m_ConfigManager != null ? m_ConfigManager.GetFloat(n) : 0f; }
        public string GetString(string n) { return m_ConfigManager != null ? m_ConfigManager.GetString(n) : null; }

        /// <summary>
        /// 尝试读取布尔型配置项；存在返回 true 并经由 <paramref name="value"/> 输出，缺失（或管理器无效）返回 false 且 <paramref name="value"/> 为默认值。
        /// </summary>
        public bool TryGetBool(string n, out bool value)
        {
            if (m_ConfigManager != null) { return m_ConfigManager.TryGetBool(n, out value); }
            value = false; return false;
        }

        /// <summary>
        /// 尝试读取整型配置项；存在返回 true 并经由 <paramref name="value"/> 输出，缺失（或管理器无效）返回 false 且 <paramref name="value"/> 为默认值。
        /// </summary>
        public bool TryGetInt(string n, out int value)
        {
            if (m_ConfigManager != null) { return m_ConfigManager.TryGetInt(n, out value); }
            value = 0; return false;
        }

        /// <summary>
        /// 尝试读取浮点型配置项；存在返回 true 并经由 <paramref name="value"/> 输出，缺失（或管理器无效）返回 false 且 <paramref name="value"/> 为默认值。
        /// </summary>
        public bool TryGetFloat(string n, out float value)
        {
            if (m_ConfigManager != null) { return m_ConfigManager.TryGetFloat(n, out value); }
            value = 0f; return false;
        }

        /// <summary>
        /// 尝试读取字符串型配置项；存在返回 true 并经由 <paramref name="value"/> 输出，缺失（或管理器无效）返回 false 且 <paramref name="value"/> 为 null。
        /// </summary>
        public bool TryGetString(string n, out string value)
        {
            if (m_ConfigManager != null) { return m_ConfigManager.TryGetString(n, out value); }
            value = null; return false;
        }

        public bool AddConfig(string name, string value, string boolStr, string intStr, string floatStr)
        {
            return m_ConfigManager != null && m_ConfigManager.AddConfig(name, value, boolStr, intStr, floatStr);
        }
        public bool RemoveConfig(string n) { return m_ConfigManager != null && m_ConfigManager.RemoveConfig(n); }
        public void RemoveAllConfigs() { m_ConfigManager?.RemoveAllConfigs(); }

        public void LoadConfig(string configAssetName, object userData = null) { LoadConfig(configAssetName, 0, userData); }

        public void LoadConfig(string configAssetName, int priority, object userData = null)
        {
            if (m_ConfigManager == null) { Log.Fatal("Config manager is invalid."); return; }
            if (string.IsNullOrEmpty(configAssetName)) { Log.Error("Config asset name is invalid."); return; }
            m_ConfigManager.LoadConfig(configAssetName, priority, userData);
        }

        public bool ParseConfig(string text, object userData = null)
        {
            return m_ConfigManager != null && m_ConfigManager.ParseConfig(text, userData);
        }

        private void OnLoadSuccess(object sender, LoadConfigSuccessEventArgs e)
        {
            try { LoadConfigSuccess?.Invoke(e.ConfigAssetName, e.Duration, e.UserData); }
            catch (Exception ex) { Log.Error("ConfigComponent.LoadConfigSuccess threw: {0}", ex); }
        }

        private void OnLoadFailure(object sender, LoadConfigFailureEventArgs e)
        {
            try { LoadConfigFailure?.Invoke(e.ConfigAssetName, e.ErrorMessage, e.UserData); }
            catch (Exception ex) { Log.Error("ConfigComponent.LoadConfigFailure threw: {0}", ex); }
        }
    }
}
