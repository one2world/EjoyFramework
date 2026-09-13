//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;

namespace EjoyFramework.Core.RemoteConfig
{
    /// <summary>
    /// 远程配置管理器实现。
    ///
    /// 取值流程：先查 Provider（命中即用其原始字符串），未命中再查内存本地默认值；
    /// 均未命中则返回调用方 def。数值/布尔解析统一用 InvariantCulture，解析失败回退到 def
    /// （避免不同区域设置下小数点/千分位差异导致的解析歧义）。
    ///
    /// 核心层引擎无关：本类仅使用 System.* ，不引用 UnityEngine。
    /// </summary>
    internal sealed class RemoteConfigManager : FrameworkModule, IRemoteConfigManager
    {
        private readonly Dictionary<string, string> m_LocalDefaults = new Dictionary<string, string>(StringComparer.Ordinal);
        private IRemoteConfigProvider m_Provider;

        public RemoteConfigManager()
        {
            m_Provider = null;
        }

        // Priority 0：基础数据模块，无依赖。
        public override int Priority { get { return 0; } }

        public void SetProvider(IRemoteConfigProvider provider)
        {
            Framework.EnsureMainThread(nameof(SetProvider));
            m_Provider = provider;
        }

        /// <summary>
        /// 设置本地默认值（供测试/启动期 bootstrap 填充）。raw 为原始字符串，按需在 Get* 时解析。
        /// </summary>
        public void SetLocalDefault(string key, string raw)
        {
            if (string.IsNullOrEmpty(key))
            {
                FrameworkLog.Warning("RemoteConfig.SetLocalDefault: key is invalid.");
                return;
            }
            m_LocalDefaults[key] = raw;
        }

        public bool HasKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string raw;
            return TryGetRaw(key, out raw);
        }

        public string GetString(string key, string def = "")
        {
            string raw;
            return TryGetRaw(key, out raw) ? raw : def;
        }

        public int GetInt(string key, int def = 0)
        {
            string raw;
            if (!TryGetRaw(key, out raw)) return def;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;
        }

        public bool GetBool(string key, bool def = false)
        {
            string raw;
            if (!TryGetRaw(key, out raw)) return def;
            // 支持 "true"/"false"（忽略大小写）与 "1"/"0"。
            if (bool.TryParse(raw, out bool b)) return b;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i != 0;
            return def;
        }

        public float GetFloat(string key, float def = 0f)
        {
            string raw;
            if (!TryGetRaw(key, out raw)) return def;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
        }

        // 取原始字符串：Provider 优先，本地默认值兜底。
        private bool TryGetRaw(string key, out string raw)
        {
            raw = null;
            if (string.IsNullOrEmpty(key)) return false;

            IRemoteConfigProvider provider = m_Provider;
            if (provider != null)
            {
                try
                {
                    if (provider.TryGet(key, out string fromProvider))
                    {
                        raw = fromProvider;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("RemoteConfig provider TryGet('{0}') threw: {1}", key, ex);
                }
            }

            return m_LocalDefaults.TryGetValue(key, out raw);
        }

        public override void Update(float elapseSeconds, float realElapseSeconds) { }

        public override void Shutdown()
        {
            m_Provider = null;
            m_LocalDefaults.Clear();
        }
    }
}
