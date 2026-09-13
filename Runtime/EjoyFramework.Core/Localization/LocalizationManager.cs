//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core.Localization
{
    /// <summary>
    /// 本地化管理器实现。
    /// 加载链：LoadDictionary → ResourceManager.LoadAsset → Helper.ReadData → 入字典 → LoadDictionarySuccess
    /// </summary>
    internal sealed class LocalizationManager : FrameworkModule, ILocalizationManager
    {
        private readonly Dictionary<string, string> m_Dict = new Dictionary<string, string>(StringComparer.Ordinal);
        // 已警告过的缺失 key，去重避免逐帧刷屏。
        private readonly HashSet<string> m_WarnedMissingKeys = new HashSet<string>(StringComparer.Ordinal);
        private Language m_Language = Language.Unspecified;
        private IResourceManager m_ResourceManager;
        private ILocalizationHelper m_Helper;
        private AssetLoadCoordinator m_LoadCoordinator;

        public event EventHandler<LoadDictionarySuccessEventArgs> LoadDictionarySuccess;
        public event EventHandler<LoadDictionaryFailureEventArgs> LoadDictionaryFailure;

        public LocalizationManager()
        {
        }

        /// <summary>测试用构造：直接注入 IResourceManager。</summary>
        internal LocalizationManager(IResourceManager resourceManager)
        {
            if (resourceManager == null) throw new FrameworkException("Resource manager is invalid.");
            m_ResourceManager = resourceManager;
        }

        // Priority 0：业务数据模块。
        public override int Priority { get { return 0; } }

        // 必需配置自检：依赖 LocalizationHelper 解析本地化资产。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Helper != null; } }
        public override string ConfigurationHint { get { return "Call SetLocalizationHelper(...) before use."; } }

        public override void Update(float a, float b) { }
        public override void Shutdown() { m_Dict.Clear(); m_LoadCoordinator?.Clear(); m_WarnedMissingKeys.Clear(); }

        public Language Language { get { return m_Language; } set { m_Language = value; } }
        public int DictionaryCount { get { return m_Dict.Count; } }

        private IResourceManager Resource
        {
            get { return m_ResourceManager ?? (m_ResourceManager = Framework.GetModule<IResourceManager>()); }
        }

        // 懒构造加载协调器：注入本模块的成功/失败/释放回调与错误文案，封装在飞加载跟踪与同步完成时序。
        private AssetLoadCoordinator LoadCoordinator
        {
            get
            {
                return m_LoadCoordinator ?? (m_LoadCoordinator = new AssetLoadCoordinator(
                    FireSuccess, FireFailure, ReleaseAsset,
                    "LocalizationHelper.ReadData threw: ", "LocalizationHelper.ReadData returned false."));
            }
        }

        public void SetLocalizationHelper(ILocalizationHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetLocalizationHelper));
            if (helper == null) throw new FrameworkException("Localization helper is invalid.");
            m_Helper = helper;
        }

        public void LoadDictionary(string dictionaryAssetName, int priority, object userData)
        {
            Framework.EnsureMainThread(nameof(LoadDictionary));
            if (m_Helper == null) throw new FrameworkException("Localization helper is not set.");
            if (string.IsNullOrEmpty(dictionaryAssetName)) throw new FrameworkException("Dictionary asset name is invalid.");

            var handle = Resource.LoadAssetWithHandle(dictionaryAssetName, priority, dictionaryAssetName);
            LoadCoordinator.BeginLoad(dictionaryAssetName, handle, userData, ParseLoadedDictionary);
        }

        private bool ParseLoadedDictionary(string assetName, object asset, object userData)
        {
            return m_Helper.ReadData(this, assetName, asset, userData);
        }

        public bool ParseDictionary(string text, object userData)
        {
            if (m_Helper == null) throw new FrameworkException("Localization helper is not set.");
            return m_Helper.ParseData(this, text, userData);
        }

        public string GetString(string key)
        {
            if (string.IsNullOrEmpty(key)) return "<EmptyKey>";
            string v;
            if (m_Dict.TryGetValue(key, out v)) return v;
            // 缺失 key：保留 fallback（不抛异常，调用方/测试可能依赖非抛行为），
            // 但每个 key 只告警一次，避免逐帧刷屏。
            if (m_WarnedMissingKeys.Add(key))
            {
                FrameworkLog.Warning("Localization key not found: '{0}'.", key);
            }
            return Utility.Text.Format("<NoKey>{0}", key);
        }

        public string GetString(string key, params object[] args)
        {
            return Utility.Text.Format(GetString(key), args);
        }

        public bool HasRawString(string key) { return key != null && m_Dict.ContainsKey(key); }
        public string GetRawString(string key) { string v; return m_Dict.TryGetValue(key, out v) ? v : null; }

        public bool AddRawString(string key, string value)
        {
            // 默认行为保持不变：重复 key 返回 false（不覆盖）。
            return AddRawString(key, value, false);
        }

        /// <summary>
        /// 添加本地化原始字符串。<paramref name="overwrite"/> 为 true 时允许覆盖已存在的 key
        /// （用于 base + DLC 叠加等 last-wins 场景）；为 false 时遇到重复 key 返回 false。
        /// </summary>
        public bool AddRawString(string key, string value, bool overwrite)
        {
            Framework.EnsureMainThread(nameof(AddRawString));
            if (string.IsNullOrEmpty(key)) return false;
            if (m_Dict.ContainsKey(key))
            {
                if (!overwrite) return false;
                FrameworkLog.Warning("Localization key overwritten: '{0}'.", key);
            }
            m_Dict[key] = value ?? string.Empty;
            m_WarnedMissingKeys.Remove(key);
            return true;
        }

        public bool RemoveRawString(string key)
        {
            Framework.EnsureMainThread(nameof(RemoveRawString));
            return key != null && m_Dict.Remove(key);
        }

        public void RemoveAllRawStrings()
        {
            Framework.EnsureMainThread(nameof(RemoveAllRawStrings));
            m_Dict.Clear();
        }

        private void FireSuccess(string assetName, float duration, object userData)
        {
            var h = LoadDictionarySuccess;
            if (h != null)
            {
                var args = LoadDictionarySuccessEventArgs.Create(assetName, duration, userData);
                try { h(this, args); } catch (Exception ex) { FrameworkLog.Error("LoadDictionarySuccess threw: {0}", ex); }
                ReferencePool.Release(args);
            }
        }

        private void FireFailure(string assetName, string error, object userData)
        {
            FrameworkLog.Error("LoadDictionary failed: '{0}' err={1}", assetName, error);
            var h = LoadDictionaryFailure;
            if (h != null)
            {
                var args = LoadDictionaryFailureEventArgs.Create(assetName, error, userData);
                try { h(this, args); } catch (Exception ex) { FrameworkLog.Error("LoadDictionaryFailure threw: {0}", ex); }
                ReferencePool.Release(args);
            }
        }

        private void ReleaseAsset(object asset)
        {
            if (m_Helper != null) { try { m_Helper.ReleaseDataAsset(asset); } catch (System.Exception ex) { FrameworkLog.Warning("Localization ReleaseDataAsset threw: {0}", ex); } }
        }
    }
}
