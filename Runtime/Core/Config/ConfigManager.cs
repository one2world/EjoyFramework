//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core.Config
{
    /// <summary>
    /// 全局配置管理器实现。
    /// 加载链：LoadConfig → ResourceManager.LoadAsset → ConfigHelper.ReadData → 入库 → 触发 LoadConfigSuccess
    /// </summary>
    internal sealed class ConfigManager : FrameworkModule, IConfigManager
    {
        private struct Entry
        {
            public string Raw;
            public bool Bool;
            public int Int;
            public float Float;
        }

        private readonly Dictionary<string, Entry> m_Configs = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private IResourceManager m_ResourceManager;
        private IConfigHelper m_Helper;
        private AssetLoadCoordinator m_LoadCoordinator;

        public event EventHandler<LoadConfigSuccessEventArgs> LoadConfigSuccess;
        public event EventHandler<LoadConfigFailureEventArgs> LoadConfigFailure;

        public ConfigManager()
        {
        }

        /// <summary>测试用构造：直接注入 IResourceManager，无须依赖 Framework.GetModule。</summary>
        internal ConfigManager(IResourceManager resourceManager)
        {
            if (resourceManager == null) throw new FrameworkException("Resource manager is invalid.");
            m_ResourceManager = resourceManager;
        }

        // Priority 0：业务数据模块。
        public override int Priority { get { return 0; } }

        // 必需配置自检：依赖 ConfigHelper 解析配置资产。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Helper != null; } }
        public override string ConfigurationHint { get { return "Call SetConfigHelper(...) before use."; } }

        public override void Update(float a, float b) { }
        public override void Shutdown()
        {
            m_Configs.Clear();
            m_LoadCoordinator?.Clear();
        }

        public int Count { get { return m_Configs.Count; } }

        /// <summary>
        /// 懒拉取 IResourceManager：线上路径走 Framework.GetModule，测试路径走构造注入的实例。
        /// </summary>
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
                    "ConfigHelper.ReadData threw: ", "ConfigHelper.ReadData returned false."));
            }
        }

        public void SetConfigHelper(IConfigHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetConfigHelper));
            if (helper == null) throw new FrameworkException("Config helper is invalid.");
            m_Helper = helper;
        }

        public void LoadConfig(string configAssetName, int priority, object userData)
        {
            Framework.EnsureMainThread(nameof(LoadConfig));
            if (m_Helper == null) throw new FrameworkException("Config helper is not set.");
            if (string.IsNullOrEmpty(configAssetName)) throw new FrameworkException("Config asset name is invalid.");

            var handle = Resource.LoadAssetWithHandle(configAssetName, priority, configAssetName);
            LoadCoordinator.BeginLoad(configAssetName, handle, userData, ParseLoadedConfig);
        }

        private bool ParseLoadedConfig(string assetName, object asset, object userData)
        {
            return m_Helper.ReadData(this, assetName, asset, userData);
        }

        public bool ParseConfig(string text, object userData)
        {
            if (m_Helper == null) throw new FrameworkException("Config helper is not set.");
            return m_Helper.ParseData(this, text, userData);
        }

        public bool HasConfig(string n) { return n != null && m_Configs.ContainsKey(n); }

        public bool GetBool(string n) { Entry e; return n != null && m_Configs.TryGetValue(n, out e) && e.Bool; }
        public int GetInt(string n) { Entry e; return n != null && m_Configs.TryGetValue(n, out e) ? e.Int : 0; }
        public float GetFloat(string n) { Entry e; return n != null && m_Configs.TryGetValue(n, out e) ? e.Float : 0f; }
        public string GetString(string n) { Entry e; return n != null && m_Configs.TryGetValue(n, out e) ? e.Raw : null; }

        public bool TryGetBool(string n, out bool value)
        {
            Entry e;
            if (n != null && m_Configs.TryGetValue(n, out e)) { value = e.Bool; return true; }
            value = false; return false;
        }

        public bool TryGetInt(string n, out int value)
        {
            Entry e;
            if (n != null && m_Configs.TryGetValue(n, out e)) { value = e.Int; return true; }
            value = 0; return false;
        }

        public bool TryGetFloat(string n, out float value)
        {
            Entry e;
            if (n != null && m_Configs.TryGetValue(n, out e)) { value = e.Float; return true; }
            value = 0f; return false;
        }

        public bool TryGetString(string n, out string value)
        {
            Entry e;
            if (n != null && m_Configs.TryGetValue(n, out e)) { value = e.Raw; return true; }
            value = null; return false;
        }

        public bool AddConfig(string name, string value, string boolStr, string intStr, string floatStr)
        {
            Framework.EnsureMainThread(nameof(AddConfig));
            if (string.IsNullOrEmpty(name) || m_Configs.ContainsKey(name)) return false;
            Entry e = new Entry { Raw = value };
            e.Bool = ParseBool(boolStr);
            int.TryParse(intStr, out e.Int);
            float.TryParse(floatStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out e.Float);
            m_Configs.Add(name, e);
            return true;
        }

        /// <summary>
        /// 宽松布尔解析：接受 1/0、true/false、yes/no（大小写不敏感，自动 Trim）。
        /// 修复了 bool.TryParse 只认 "true"/"false" 导致配置里的 1/0 被静默解析为 false 的问题。
        /// </summary>
        private static bool ParseBool(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                    return true;
                default:
                    return false;
            }
        }

        public bool RemoveConfig(string n)
        {
            Framework.EnsureMainThread(nameof(RemoveConfig));
            return n != null && m_Configs.Remove(n);
        }

        public void RemoveAllConfigs()
        {
            Framework.EnsureMainThread(nameof(RemoveAllConfigs));
            m_Configs.Clear();
        }

        private void FireSuccess(string assetName, float duration, object userData)
        {
            var h = LoadConfigSuccess;
            if (h != null)
            {
                var args = LoadConfigSuccessEventArgs.Create(assetName, duration, userData);
                try { h(this, args); } catch (Exception ex) { FrameworkLog.Error("LoadConfigSuccess threw: {0}", ex); }
                ReferencePool.Release(args);
            }
        }

        private void FireFailure(string assetName, string error, object userData)
        {
            FrameworkLog.Error("LoadConfig failed: '{0}' err={1}", assetName, error);
            var h = LoadConfigFailure;
            if (h != null)
            {
                var args = LoadConfigFailureEventArgs.Create(assetName, error, userData);
                try { h(this, args); } catch (Exception ex) { FrameworkLog.Error("LoadConfigFailure threw: {0}", ex); }
                ReferencePool.Release(args);
            }
        }

        private void ReleaseAsset(object asset)
        {
            if (m_Helper != null)
            {
                try { m_Helper.ReleaseDataAsset(asset); } catch (System.Exception ex) { FrameworkLog.Warning("Config ReleaseDataAsset threw: {0}", ex); }
            }
        }
    }
}
