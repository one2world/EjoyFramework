//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.DataTable;
using EjoyFramework.Core.Resource;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 数据表组件。完全转发给 DataTableManager；Start 注入 ResourceManager + DefaultDataTableHelper。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/DataTable")]
    public sealed class DataTableComponent : GameFrameworkComponent
    {
        public event Action<string, float, object> LoadDataTableSuccess;
        public event Action<string, string, object> LoadDataTableFailure;

        private IDataTableManager m_DataTableManager;
        private DefaultDataTableHelper m_Helper;

        protected override void Awake()
        {
            base.Awake();
            m_DataTableManager = Framework.GetModule<IDataTableManager>();
            if (m_DataTableManager == null) { Log.Fatal("DataTable manager is invalid."); return; }
            m_DataTableManager.LoadDataTableSuccess += OnLoadSuccess;
            m_DataTableManager.LoadDataTableFailure += OnLoadFailure;

            IResourceManager rm = Framework.GetModule<IResourceManager>();
            if (rm == null) { Log.Fatal("Resource manager is invalid."); return; }
            // DataTableManager 自取 ResourceManager（懒拉取）
            m_Helper = new DefaultDataTableHelper(rm);
            m_DataTableManager.SetDataTableHelper(m_Helper);
        }

        protected override void OnDestroy()
        {
            if (m_DataTableManager != null)
            {
                m_DataTableManager.LoadDataTableSuccess -= OnLoadSuccess;
                m_DataTableManager.LoadDataTableFailure -= OnLoadFailure;
            }
            base.OnDestroy();
        }

        public int Count { get { return m_DataTableManager != null ? m_DataTableManager.Count : 0; } }

        public IDataTable<T> CreateDataTable<T>() where T : class, IDataRow, new()
        {
            return m_DataTableManager != null ? m_DataTableManager.CreateDataTable<T>() : null;
        }

        public IDataTable<T> CreateDataTable<T>(string name) where T : class, IDataRow, new()
        {
            return m_DataTableManager != null ? m_DataTableManager.CreateDataTable<T>(name) : null;
        }

        public IDataTable<T> GetDataTable<T>() where T : IDataRow
        {
            return m_DataTableManager != null ? m_DataTableManager.GetDataTable<T>() : null;
        }

        public IDataTable<T> GetDataTable<T>(string name) where T : IDataRow
        {
            return m_DataTableManager != null ? m_DataTableManager.GetDataTable<T>(name) : null;
        }

        public bool HasDataTable<T>() where T : IDataRow
        {
            return m_DataTableManager != null && m_DataTableManager.HasDataTable<T>();
        }

        public bool DestroyDataTable<T>() where T : IDataRow
        {
            return m_DataTableManager != null && m_DataTableManager.DestroyDataTable<T>();
        }

        public void LoadDataTable<T>(string assetName, object userData = null) where T : class, IDataRow, new()
        {
            LoadDataTable<T>(assetName, 0, userData);
        }

        public void LoadDataTable<T>(string assetName, int priority, object userData = null) where T : class, IDataRow, new()
        {
            if (m_DataTableManager == null) { Log.Fatal("DataTable manager is invalid."); return; }
            if (string.IsNullOrEmpty(assetName)) { Log.Error("DataTable asset name is invalid."); return; }
            m_DataTableManager.LoadDataTable<T>(assetName, priority, userData);
        }

        public bool ParseDataTable<T>(string text, object userData = null) where T : class, IDataRow, new()
        {
            return m_DataTableManager != null && m_DataTableManager.ParseDataTable<T>(text, userData);
        }

        private void OnLoadSuccess(object sender, LoadDataTableSuccessEventArgs e)
        {
            try { LoadDataTableSuccess?.Invoke(e.DataTableAssetName, e.Duration, e.UserData); }
            catch (Exception ex) { Log.Error("DataTableComponent.LoadDataTableSuccess threw: {0}", ex); }
        }

        private void OnLoadFailure(object sender, LoadDataTableFailureEventArgs e)
        {
            try { LoadDataTableFailure?.Invoke(e.DataTableAssetName, e.ErrorMessage, e.UserData); }
            catch (Exception ex) { Log.Error("DataTableComponent.LoadDataTableFailure threw: {0}", ex); }
        }
    }
}
