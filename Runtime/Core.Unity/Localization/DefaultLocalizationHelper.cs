//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Localization;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认本地化 helper。
    /// 文本格式：每非空非注释行两列 TAB 分隔： Key \t Value
    /// </summary>
    public sealed class DefaultLocalizationHelper : ILocalizationHelper
    {
        private readonly IResourceManager m_ResourceManager;

        public DefaultLocalizationHelper(IResourceManager resourceManager)
        {
            m_ResourceManager = resourceManager;
        }

        public bool ReadData(ILocalizationManager manager, string assetName, object asset, object userData)
        {
            if (asset == null) return false;
            string text = AsText(asset);
            if (text == null)
            {
                Log.Error("DefaultLocalizationHelper: asset '{0}' is not a TextAsset (got {1}).", assetName, asset.GetType().FullName);
                return false;
            }
            return ParseData(manager, text, userData);
        }

        public bool ParseData(ILocalizationManager manager, string text, object userData)
        {
            return LocalizationTextParser.Parse(manager, text);
        }

        public void ReleaseDataAsset(object asset)
        {
            if (asset != null && m_ResourceManager != null)
            {
                try { m_ResourceManager.UnloadAsset(asset); } catch (System.Exception ex) { FrameworkLog.Warning("Localization ReleaseDataAsset UnloadAsset threw: {0}", ex); }
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
