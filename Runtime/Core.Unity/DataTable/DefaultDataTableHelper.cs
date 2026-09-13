//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.DataTable;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认数据表 helper。
    /// 文本格式：每行一条记录；跳过空行与 # 开头的注释行；TAB 分隔列由 IDataRow.ParseDataRow 自行解析。
    /// </summary>
    public sealed class DefaultDataTableHelper : IDataTableHelper
    {
        private readonly IResourceManager m_ResourceManager;

        public DefaultDataTableHelper(IResourceManager resourceManager)
        {
            m_ResourceManager = resourceManager;
        }

        public bool ReadData<T>(IDataTable<T> table, string assetName, object asset, object userData) where T : class, IDataRow, new()
        {
            if (asset == null || table == null) return false;
            string text = AsText(asset);
            if (text == null)
            {
                Log.Error("DefaultDataTableHelper: asset '{0}' is not a TextAsset (got {1}).", assetName, asset.GetType().FullName);
                return false;
            }
            return ParseData<T>(table, text, userData);
        }

        public bool ParseData<T>(IDataTable<T> table, string text, object userData) where T : class, IDataRow, new()
        {
            if (table == null) return false;
            return DataTableTextParser.Parse(table, text, userData);
        }

        public void ReleaseDataAsset(object asset)
        {
            if (asset != null && m_ResourceManager != null)
            {
                try { m_ResourceManager.UnloadAsset(asset); } catch (System.Exception ex) { FrameworkLog.Warning("DataTable ReleaseDataAsset UnloadAsset threw: {0}", ex); }
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
