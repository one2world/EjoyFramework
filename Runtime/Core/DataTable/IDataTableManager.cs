//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.DataTable
{
    /// <summary>
    /// 数据表管理器接口。
    /// </summary>
    public interface IDataTableManager
    {
        int Count { get; }

        event EventHandler<LoadDataTableSuccessEventArgs> LoadDataTableSuccess;
        event EventHandler<LoadDataTableFailureEventArgs> LoadDataTableFailure;

        void SetDataTableHelper(IDataTableHelper helper);

        /// <summary>
        /// 异步加载数据表资源（路径下应为 UTF-8 文本；调用前需先 CreateDataTable&lt;T&gt;）。
        /// </summary>
        void LoadDataTable<T>(string dataTableAssetName, int priority, object userData) where T : class, IDataRow, new();

        /// <summary>
        /// 异步加载数据表（带名称变体）。
        /// </summary>
        void LoadDataTable<T>(string name, string dataTableAssetName, int priority, object userData) where T : class, IDataRow, new();

        /// <summary>
        /// 同步解析单段文本到指定数据表。
        /// </summary>
        bool ParseDataTable<T>(string text, object userData) where T : class, IDataRow, new();

        bool HasDataTable<T>() where T : IDataRow;
        bool HasDataTable<T>(string name) where T : IDataRow;

        IDataTable<T> GetDataTable<T>() where T : IDataRow;
        IDataTable<T> GetDataTable<T>(string name) where T : IDataRow;

        DataTableBase[] GetAllDataTables();

        IDataTable<T> CreateDataTable<T>() where T : class, IDataRow, new();
        IDataTable<T> CreateDataTable<T>(string name) where T : class, IDataRow, new();

        bool DestroyDataTable<T>() where T : IDataRow;
        bool DestroyDataTable<T>(string name) where T : IDataRow;
    }

    /// <summary>
    /// 数据表文本解析 helper。负责把 TextAsset/byte[]/string 拆分为多行并喂给 IDataTable&lt;T&gt;.AddDataRow。
    /// </summary>
    public interface IDataTableHelper
    {
        bool ReadData<T>(IDataTable<T> table, string assetName, object asset, object userData) where T : class, IDataRow, new();
        bool ParseData<T>(IDataTable<T> table, string text, object userData) where T : class, IDataRow, new();
        void ReleaseDataAsset(object asset);
    }

    public interface IDataTable<T> where T : IDataRow
    {
        string Name { get; }
        string FullName { get; }
        Type Type { get; }
        int Count { get; }
        bool HasDataRow(int id);
        T GetDataRow(int id);
        T[] GetAllDataRows();
        bool AddDataRow(string dataRowString, object userData);

        /// <summary>
        /// 从字符切片解析并加入一行：行类型实现 <see cref="ISpanDataRow"/> 时直接从切片解析（零中间字符串），
        /// 否则退回生成一个 string 交给 <see cref="IDataRow.ParseDataRow(string, object)"/>。
        /// 解析失败或 Id 重复返回 false。
        /// </summary>
        bool AddDataRow(ReadOnlySpan<char> dataRow, object userData);

        bool RemoveDataRow(int id);
    }

    public interface IDataRow
    {
        int Id { get; }
        bool ParseDataRow(string dataRowString, object userData);
    }

    /// <summary>
    /// 可直接从字符切片解析的数据行（配合 <see cref="TextFieldReader"/>，只有字符串列分配）。
    /// 配置工具链生成的行默认实现它；手写行可选实现。解析失败应返回 false 而不是抛异常，
    /// 且先解析到局部变量、全部成功后再赋值，不留半状态。
    /// </summary>
    public interface ISpanDataRow : IDataRow
    {
        bool ParseDataRow(ReadOnlySpan<char> dataRow, object userData);
    }

    public abstract class DataTableBase
    {
        private readonly string m_Name;
        public DataTableBase(string name) { m_Name = name ?? string.Empty; }
        public string Name { get { return m_Name; } }
        public abstract Type Type { get; }
        public abstract int Count { get; }
        internal abstract void Shutdown();
    }

    public sealed class LoadDataTableSuccessEventArgs : FrameworkEventArgs
    {
        public string DataTableAssetName { get; private set; }
        public float Duration { get; private set; }
        public object UserData { get; private set; }

        public override void Clear() { DataTableAssetName = null; Duration = 0f; UserData = null; }

        public static LoadDataTableSuccessEventArgs Create(string assetName, float duration, object userData)
        {
            var e = ReferencePool.Acquire<LoadDataTableSuccessEventArgs>();
            e.DataTableAssetName = assetName;
            e.Duration = duration;
            e.UserData = userData;
            return e;
        }
    }

    public sealed class LoadDataTableFailureEventArgs : FrameworkEventArgs
    {
        public string DataTableAssetName { get; private set; }
        public string ErrorMessage { get; private set; }
        public object UserData { get; private set; }

        public override void Clear() { DataTableAssetName = null; ErrorMessage = null; UserData = null; }

        public static LoadDataTableFailureEventArgs Create(string assetName, string errorMessage, object userData)
        {
            var e = ReferencePool.Acquire<LoadDataTableFailureEventArgs>();
            e.DataTableAssetName = assetName;
            e.ErrorMessage = errorMessage;
            e.UserData = userData;
            return e;
        }
    }
}
