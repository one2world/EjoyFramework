//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.DataNode
{
    /// <summary>
    /// 数据结点管理器接口。
    /// </summary>
    public interface IDataNodeManager
    {
        /// <summary>
        /// 获取根数据结点。
        /// </summary>
        IDataNode Root { get; }

        /// <summary>
        /// 获取数据结点。
        /// </summary>
        IDataNode GetNode(string path);

        /// <summary>
        /// 获取或创建数据结点。
        /// </summary>
        IDataNode GetOrAddNode(string path);

        /// <summary>
        /// 移除数据结点。
        /// </summary>
        void RemoveNode(string path);

        /// <summary>
        /// 清空所有数据结点。
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// 数据结点接口。
    /// </summary>
    public interface IDataNode
    {
        /// <summary>
        /// 获取数据结点的名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取数据结点的完整名称。
        /// </summary>
        string FullName { get; }

        /// <summary>
        /// 获取父数据结点。
        /// </summary>
        IDataNode Parent { get; }

        /// <summary>
        /// 获取子数据结点数量。
        /// </summary>
        int ChildCount { get; }

        /// <summary>
        /// 获取或设置数据结点的数据。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <returns>数据。</returns>
        T GetData<T>() where T : Variable;

        /// <summary>
        /// 设置数据结点的数据。
        /// </summary>
        void SetData<T>(T data) where T : Variable;

        /// <summary>
        /// 获取子数据结点。
        /// </summary>
        IDataNode GetChild(string name);

        /// <summary>
        /// 获取或创建子数据结点。
        /// </summary>
        IDataNode GetOrAddChild(string name);

        /// <summary>
        /// 获取所有子数据结点。
        /// </summary>
        IDataNode[] GetAllChild();

        /// <summary>
        /// 移除子数据结点。
        /// </summary>
        void RemoveChild(string name);

        /// <summary>
        /// 清空所有子数据结点。
        /// </summary>
        void Clear();
    }
}
