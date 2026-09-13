//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Localization;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 本地化组件。完全转发给 LocalizationManager；Awake 注入 ResourceManager + DefaultLocalizationHelper。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Localization")]
    public sealed class LocalizationComponent : GameFrameworkComponent
    {
        public event Action<string, float, object> LoadDictionarySuccess;
        public event Action<string, string, object> LoadDictionaryFailure;

        private ILocalizationManager m_LocalizationManager;
        private DefaultLocalizationHelper m_Helper;

        protected override void Awake()
        {
            base.Awake();
            m_LocalizationManager = Framework.GetModule<ILocalizationManager>();
            if (m_LocalizationManager == null) { Log.Fatal("Localization manager is invalid."); return; }
            m_LocalizationManager.LoadDictionarySuccess += OnLoadSuccess;
            m_LocalizationManager.LoadDictionaryFailure += OnLoadFailure;
            ConfigureManager();
        }

        private void ConfigureManager()
        {
            if (m_LocalizationManager == null) return;
            IResourceManager rm = Framework.GetModule<IResourceManager>();
            if (rm == null) { Log.Fatal("Resource manager is invalid."); return; }
            // LocalizationManager 自取 ResourceManager（懒拉取）
            m_Helper = new DefaultLocalizationHelper(rm);
            m_LocalizationManager.SetLocalizationHelper(m_Helper);
        }

        protected override void OnDestroy()
        {
            if (m_LocalizationManager != null)
            {
                m_LocalizationManager.LoadDictionarySuccess -= OnLoadSuccess;
                m_LocalizationManager.LoadDictionaryFailure -= OnLoadFailure;
            }
            base.OnDestroy();
        }

        public Language Language
        {
            get { return m_LocalizationManager != null ? m_LocalizationManager.Language : Language.Unspecified; }
            set { if (m_LocalizationManager != null) m_LocalizationManager.Language = value; }
        }

        public int DictionaryCount { get { return m_LocalizationManager != null ? m_LocalizationManager.DictionaryCount : 0; } }

        public string GetString(string key) { return m_LocalizationManager != null ? m_LocalizationManager.GetString(key) : key; }
        public string GetString(string key, params object[] args) { return m_LocalizationManager != null ? m_LocalizationManager.GetString(key, args) : key; }
        public bool HasRawString(string key) { return m_LocalizationManager != null && m_LocalizationManager.HasRawString(key); }
        public bool AddRawString(string key, string value) { return m_LocalizationManager != null && m_LocalizationManager.AddRawString(key, value); }
        public bool RemoveRawString(string key) { return m_LocalizationManager != null && m_LocalizationManager.RemoveRawString(key); }
        public void RemoveAllRawStrings() { m_LocalizationManager?.RemoveAllRawStrings(); }

        public void LoadDictionary(string assetName, object userData = null) { LoadDictionary(assetName, 0, userData); }

        public void LoadDictionary(string assetName, int priority, object userData = null)
        {
            if (m_LocalizationManager == null) { Log.Fatal("Localization manager is invalid."); return; }
            if (string.IsNullOrEmpty(assetName)) { Log.Error("Dictionary asset name is invalid."); return; }
            m_LocalizationManager.LoadDictionary(assetName, priority, userData);
        }

        public bool ParseDictionary(string text, object userData = null)
        {
            return m_LocalizationManager != null && m_LocalizationManager.ParseDictionary(text, userData);
        }

        private void OnLoadSuccess(object sender, LoadDictionarySuccessEventArgs e)
        {
            try { LoadDictionarySuccess?.Invoke(e.DictionaryAssetName, e.Duration, e.UserData); }
            catch (Exception ex) { Log.Error("LocalizationComponent.LoadDictionarySuccess threw: {0}", ex); }
        }

        private void OnLoadFailure(object sender, LoadDictionaryFailureEventArgs e)
        {
            try { LoadDictionaryFailure?.Invoke(e.DictionaryAssetName, e.ErrorMessage, e.UserData); }
            catch (Exception ex) { Log.Error("LocalizationComponent.LoadDictionaryFailure threw: {0}", ex); }
        }
    }
}
