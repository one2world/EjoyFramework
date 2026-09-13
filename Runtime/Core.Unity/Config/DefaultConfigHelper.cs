//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Config;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认配置 helper。
    /// 文本格式：每非空非注释（#开头）行五列 TAB 分隔：
    ///   Name \t StringValue \t BoolValue \t IntValue \t FloatValue
    /// 兼容 LF/CRLF；支持空白行和 # 注释行。
    /// </summary>
    public sealed class DefaultConfigHelper : IConfigHelper
    {
        private readonly IResourceManager m_ResourceManager;

        public DefaultConfigHelper(IResourceManager resourceManager)
        {
            m_ResourceManager = resourceManager;
        }

        public bool ReadData(IConfigManager manager, string assetName, object asset, object userData)
        {
            if (asset == null) return false;
            string text = AsText(asset);
            if (text == null)
            {
                Log.Error("DefaultConfigHelper: asset '{0}' is not a TextAsset (got {1}).", assetName, asset.GetType().FullName);
                return false;
            }
            return ParseData(manager, text, userData);
        }

        public bool ParseData(IConfigManager manager, string text, object userData)
        {
            return ConfigTextParser.Parse(manager, text);
        }

        public void ReleaseDataAsset(object asset)
        {
            if (asset != null && m_ResourceManager != null)
            {
                try { m_ResourceManager.UnloadAsset(asset); } catch (System.Exception ex) { FrameworkLog.Warning("Config ReleaseDataAsset UnloadAsset threw: {0}", ex); }
            }
        }

        private static string AsText(object asset)
        {
            if (asset is string s) return s;
            if (asset is TextAsset ta) return ta.text;
            if (asset is byte[] bytes) return System.Text.Encoding.UTF8.GetString(bytes);
            return null;
        }
    }
}
