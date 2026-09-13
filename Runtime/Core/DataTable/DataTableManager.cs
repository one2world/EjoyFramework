//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core.DataTable
{
    /// <summary>
    /// 数据表管理器实现。
    /// 加载链：LoadDataTable&lt;T&gt; → ResourceManager.LoadAsset → Helper.ReadData → 多行 AddDataRow → LoadDataTableSuccess
    /// </summary>
    internal sealed class DataTableManager : FrameworkModule, IDataTableManager
    {
        private readonly Dictionary<TypeNamePair, DataTableBase> m_Tables = new Dictionary<TypeNamePair, DataTableBase>();
        private IResourceManager m_ResourceManager;
        private IDataTableHelper m_Helper;
        private AssetLoadCoordinator m_LoadCoordinator;

        public event EventHandler<LoadDataTableSuccessEventArgs> LoadDataTableSuccess;
        public event EventHandler<LoadDataTableFailureEventArgs> LoadDataTableFailure;

        public DataTableManager()
        {
        }

        /// <summary>测试用构造：直接注入 IResourceManager。</summary>
        internal DataTableManager(IResourceManager resourceManager)
        {
            if (resourceManager == null) throw new FrameworkException("Resource manager is invalid.");
            m_ResourceManager = resourceManager;
        }

        // Priority 0：业务数据模块。
        public override int Priority { get { return 0; } }

        // 必需配置自检：依赖 DataTableHelper 解析数据表资产。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Helper != null; } }
        public override string ConfigurationHint { get { return "Call SetDataTableHelper(...) before use."; } }

        public override void Update(float a, float b) { }
        public override void Shutdown()
        {
            foreach (var kv in m_Tables)
            {
                try { kv.Value.Shutdown(); }
                catch (Exception ex) { FrameworkLog.Error("DataTableManager.Shutdown: table '{0}' Shutdown threw: {1}", kv.Key, ex); }
            }
            m_Tables.Clear();
            m_LoadCoordinator?.Clear();
        }

        public int Count { get { return m_Tables.Count; } }

        private IResourceManager Resource
        {
            get { return m_ResourceManager ?? (m_ResourceManager = Framework.GetModule<IResourceManager>()); }
        }

        // 懒构造加载协调器：注入本模块的成功/失败/释放回调，封装在飞加载跟踪与同步完成时序。
        // 解析逻辑按类型在每次 LoadDataTable<T> 调用时以闭包注入（见 BeginLoad）。
        private AssetLoadCoordinator LoadCoordinator
        {
            get
            {
                return m_LoadCoordinator ?? (m_LoadCoordinator = new AssetLoadCoordinator(
                    FireSuccess, FireFailure, ReleaseAsset,
                    "DataTableHelper.ReadData threw: ", "DataTableHelper.ReadData returned false or threw."));
            }
        }

        public void SetDataTableHelper(IDataTableHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetDataTableHelper));
            if (helper == null) throw new FrameworkException("DataTable helper is invalid.");
            m_Helper = helper;
        }

        public void LoadDataTable<T>(string dataTableAssetName, int priority, object userData) where T : class, IDataRow, new()
        {
            LoadDataTable<T>(string.Empty, dataTableAssetName, priority, userData);
        }

        public void LoadDataTable<T>(string name, string dataTableAssetName, int priority, object userData) where T : class, IDataRow, new()
        {
            Framework.EnsureMainThread(nameof(LoadDataTable));
            if (m_Helper == null) throw new FrameworkException("DataTable helper is not set.");
            if (string.IsNullOrEmpty(dataTableAssetName)) throw new FrameworkException("DataTable asset name is invalid.");

            IDataTable<T> table = GetDataTable<T>(name);
            if (table == null) table = CreateDataTable<T>(name);

            var handle = Resource.LoadAssetWithHandle(dataTableAssetName, priority, dataTableAssetName);
            // 解析闭包捕获强类型 table；内部自捕获异常并返回 false（保持原 "returned false or threw" 语义）。
            LoadCoordinator.BeginLoad(dataTableAssetName, handle, userData, (assetName, asset, ud) =>
            {
                try { return m_Helper.ReadData<T>(table, assetName, asset, ud); }
                catch (Exception ex) { FrameworkLog.Error("DataTableHelper.ReadData threw: {0}", ex); return false; }
            });
        }

        public bool ParseDataTable<T>(string text, object userData) where T : class, IDataRow, new()
        {
            if (m_Helper == null) throw new FrameworkException("DataTable helper is not set.");
            IDataTable<T> table = GetDataTable<T>();
            if (table == null) table = CreateDataTable<T>();
            return m_Helper.ParseData<T>(table, text, userData);
        }

        public bool HasDataTable<T>() where T : IDataRow { return m_Tables.ContainsKey(new TypeNamePair(typeof(T))); }
        public bool HasDataTable<T>(string name) where T : IDataRow { return m_Tables.ContainsKey(new TypeNamePair(typeof(T), name)); }

        public IDataTable<T> GetDataTable<T>() where T : IDataRow
        {
            DataTableBase t;
            return m_Tables.TryGetValue(new TypeNamePair(typeof(T)), out t) ? (IDataTable<T>)t : null;
        }

        public IDataTable<T> GetDataTable<T>(string name) where T : IDataRow
        {
            DataTableBase t;
            return m_Tables.TryGetValue(new TypeNamePair(typeof(T), name), out t) ? (IDataTable<T>)t : null;
        }

        public DataTableBase[] GetAllDataTables()
        {
            var arr = new DataTableBase[m_Tables.Count];
            int i = 0;
            foreach (var kv in m_Tables) arr[i++] = kv.Value;
            return arr;
        }

        public IDataTable<T> CreateDataTable<T>() where T : class, IDataRow, new() { return CreateDataTable<T>(string.Empty); }

        public IDataTable<T> CreateDataTable<T>(string name) where T : class, IDataRow, new()
        {
            Framework.EnsureMainThread(nameof(CreateDataTable));
            var key = new TypeNamePair(typeof(T), name);
            if (m_Tables.ContainsKey(key)) throw new FrameworkException(Utility.Text.Format("DataTable '{0}' already exists.", key));
            var table = new DataTable<T>(name);
            m_Tables.Add(key, table);
            return table;
        }

        public bool DestroyDataTable<T>() where T : IDataRow { return DestroyByKey(new TypeNamePair(typeof(T))); }
        public bool DestroyDataTable<T>(string name) where T : IDataRow { return DestroyByKey(new TypeNamePair(typeof(T), name)); }

        private bool DestroyByKey(TypeNamePair key)
        {
            Framework.EnsureMainThread(nameof(DestroyDataTable));
            DataTableBase t;
            if (m_Tables.TryGetValue(key, out t))
            {
                try { t.Shutdown(); }
                catch (Exception ex) { FrameworkLog.Error("DataTableManager.DestroyByKey: table '{0}' Shutdown threw: {1}", key, ex); }
                m_Tables.Remove(key);
                return true;
            }
            return false;
        }

        private void FireSuccess(string assetName, float duration, object userData)
        {
            var h = LoadDataTableSuccess;
            if (h != null)
            {
                var args = LoadDataTableSuccessEventArgs.Create(assetName, duration, userData);
                try { h(this, args); } catch (Exception ex) { FrameworkLog.Error("LoadDataTableSuccess threw: {0}", ex); }
                ReferencePool.Release(args);
            }
        }

        private void FireFailure(string assetName, string error, object userData)
        {
            FrameworkLog.Error("LoadDataTable failed: '{0}' err={1}", assetName, error);
            var h = LoadDataTableFailure;
            if (h != null)
            {
                var args = LoadDataTableFailureEventArgs.Create(assetName, error, userData);
                try { h(this, args); } catch (Exception ex) { FrameworkLog.Error("LoadDataTableFailure threw: {0}", ex); }
                ReferencePool.Release(args);
            }
        }

        private void ReleaseAsset(object asset)
        {
            if (m_Helper != null)
            {
                try { m_Helper.ReleaseDataAsset(asset); }
                catch (Exception ex) { FrameworkLog.Error("DataTableManager.ReleaseAsset: ReleaseDataAsset threw: {0}", ex); }
            }
        }

        private sealed class DataTable<T> : DataTableBase, IDataTable<T> where T : class, IDataRow, new()
        {
            private readonly Dictionary<int, T> m_Rows = new Dictionary<int, T>();

            public DataTable(string name) : base(name) { }

            public override Type Type { get { return typeof(T); } }
            public override int Count { get { return m_Rows.Count; } }
            public string FullName { get { return Utility.Text.GetFullName(typeof(T), Name); } }

            public bool HasDataRow(int id) { return m_Rows.ContainsKey(id); }
            public T GetDataRow(int id) { T r; return m_Rows.TryGetValue(id, out r) ? r : null; }
            public T[] GetAllDataRows()
            {
                var arr = new T[m_Rows.Count];
                int i = 0;
                foreach (var kv in m_Rows) arr[i++] = kv.Value;
                return arr;
            }

            public bool AddDataRow(string dataRowString, object userData)
            {
                T row = new T();
                if (!row.ParseDataRow(dataRowString, userData)) return false;
                if (m_Rows.ContainsKey(row.Id)) return false;
                m_Rows.Add(row.Id, row);
                return true;
            }

            public bool RemoveDataRow(int id) { return m_Rows.Remove(id); }
            internal override void Shutdown() { m_Rows.Clear(); }
        }
    }
}
