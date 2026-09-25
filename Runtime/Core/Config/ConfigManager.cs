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

        // uniqueHashes：拒绝同哈希的不同名字，保证 StringHash 查找唯一确定。
        private readonly StringMap<Entry> m_Configs = new StringMap<Entry>(0, true);
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

        public bool HasConfig(StringHash n) { return m_Configs.ContainsKey(n); }
        public bool GetBool(StringHash n) { Entry e; return m_Configs.TryGetValue(n, out e) && e.Bool; }
        public int GetInt(StringHash n) { Entry e; return m_Configs.TryGetValue(n, out e) ? e.Int : 0; }
        public float GetFloat(StringHash n) { Entry e; return m_Configs.TryGetValue(n, out e) ? e.Float : 0f; }
        public string GetString(StringHash n) { Entry e; return m_Configs.TryGetValue(n, out e) ? e.Raw : null; }

        public bool TryGetBool(StringHash n, out bool value)
        {
            Entry e;
            if (m_Configs.TryGetValue(n, out e)) { value = e.Bool; return true; }
            value = false; return false;
        }

        public bool TryGetInt(StringHash n, out int value)
        {
            Entry e;
            if (m_Configs.TryGetValue(n, out e)) { value = e.Int; return true; }
            value = 0; return false;
        }

        public bool TryGetFloat(StringHash n, out float value)
        {
            Entry e;
            if (m_Configs.TryGetValue(n, out e)) { value = e.Float; return true; }
            value = 0f; return false;
        }

        public bool TryGetString(StringHash n, out string value)
        {
            Entry e;
            if (m_Configs.TryGetValue(n, out e)) { value = e.Raw; return true; }
            value = null; return false;
        }

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

        /// <summary>
        /// 五元组字符串入口。布尔宽松解析：接受 1/0、true/false、yes/no（大小写不敏感，自动 Trim），无法识别为 false
        /// （修复了 bool.TryParse 只认 "true"/"false" 导致配置里的 1/0 被静默解析为 false 的问题）；整数 / 浮点失败为 0。
        /// </summary>
        public bool AddConfig(string name, string value, string boolStr, string intStr, string floatStr)
        {
            bool boolValue;
            int intValue;
            float floatValue;
            SpanParse.TryParseBoolean(boolStr.AsSpan(), out boolValue);
            SpanParse.TryParseInt32(intStr.AsSpan(), out intValue);
            SpanParse.TryParseSingle(floatStr.AsSpan(), out floatValue);
            return AddConfig(name, value, boolValue, intValue, floatValue);
        }

        public bool AddConfig(string name, string value, bool boolValue, int intValue, float floatValue)
        {
            Framework.EnsureMainThread(nameof(AddConfig));
            if (string.IsNullOrEmpty(name) || m_Configs.ContainsKey(name)) return false;
            Entry e = new Entry { Raw = value, Bool = boolValue, Int = intValue, Float = floatValue };
            if (!m_Configs.TryAdd(name, e))
            {
                string existing;
                m_Configs.TryGetKey(new StringHash(StringHash.Compute(name)), out existing);
                FrameworkLog.Error("Config '{0}' has the same StringHash as existing config '{1}'; hash lookups could not tell them apart. Rename one of them.",
                    name, existing);
                return false;
            }

            return true;
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
