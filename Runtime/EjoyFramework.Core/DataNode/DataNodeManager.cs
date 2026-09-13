//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.DataNode
{
    internal sealed class DataNodeManager : FrameworkModule, IDataNodeManager
    {
        private static readonly char[] s_PathSeparators = { '.', '/', '\\' };
        private DataNode m_Root;

        public DataNodeManager() { m_Root = new DataNode("Root", null); }

        // Priority 0：业务数据模块（树形 K/V 缓存）。
        public override int Priority { get { return 0; } }
        public override void Update(float a, float b) { }
        public override void Shutdown() { m_Root.Clear(); }

        public IDataNode Root { get { return m_Root; } }

        public IDataNode GetNode(string path)
        {
            return string.IsNullOrEmpty(path) ? m_Root : m_Root.GetNodeByPath(path, false);
        }

        public IDataNode GetOrAddNode(string path)
        {
            Framework.EnsureMainThread(nameof(GetOrAddNode));
            return string.IsNullOrEmpty(path) ? m_Root : m_Root.GetNodeByPath(path, true);
        }

        public void RemoveNode(string path)
        {
            Framework.EnsureMainThread(nameof(RemoveNode));
            if (string.IsNullOrEmpty(path)) return;
            string[] segs = path.Split(s_PathSeparators);
            DataNode parent = m_Root;
            for (int i = 0; i < segs.Length - 1; i++)
            {
                parent = parent.GetChildInternal(segs[i]);
                if (parent == null) return;
            }
            parent.RemoveChild(segs[segs.Length - 1]);
        }

        public void Clear()
        {
            Framework.EnsureMainThread(nameof(Clear));
            m_Root.Clear();
        }

        private sealed class DataNode : IDataNode
        {
            private readonly string m_Name;
            private readonly DataNode m_Parent;
            private Variable m_Data;
            private Dictionary<string, DataNode> m_Children;
            // FullName 缓存：name/parent 一旦构造便不可变（无重命名/重挂载 API），故惰性计算一次即可，
            // 避免每次 O(depth^2) 的字符串拼接与分配。
            private string m_FullNameCache;

            public DataNode(string name, DataNode parent) { m_Name = name; m_Parent = parent; }

            public string Name { get { return m_Name; } }
            public string FullName
            {
                get
                {
                    if (m_FullNameCache != null) return m_FullNameCache;
                    m_FullNameCache = m_Parent == null ? m_Name : m_Parent.FullName + "." + m_Name;
                    return m_FullNameCache;
                }
            }
            public IDataNode Parent { get { return m_Parent; } }
            public int ChildCount { get { return m_Children == null ? 0 : m_Children.Count; } }

            public T GetData<T>() where T : Variable { return (T)m_Data; }
            public void SetData<T>(T data) where T : Variable
            {
                if (m_Data != null) ReferencePool.Release(m_Data);
                m_Data = data;
            }

            public IDataNode GetChild(string name)
            {
                return GetChildInternal(name);
            }

            public DataNode GetChildInternal(string name)
            {
                if (m_Children == null || name == null) return null;
                DataNode c;
                return m_Children.TryGetValue(name, out c) ? c : null;
            }

            public IDataNode GetOrAddChild(string name)
            {
                if (m_Children == null) m_Children = new Dictionary<string, DataNode>();
                DataNode c;
                if (!m_Children.TryGetValue(name, out c))
                {
                    c = new DataNode(name, this);
                    m_Children.Add(name, c);
                }
                return c;
            }

            public IDataNode[] GetAllChild()
            {
                if (m_Children == null) return Array.Empty<IDataNode>();
                var arr = new IDataNode[m_Children.Count];
                int i = 0;
                foreach (var kv in m_Children) arr[i++] = kv.Value;
                return arr;
            }

            public void RemoveChild(string name)
            {
                if (m_Children == null || name == null) return;
                DataNode c;
                if (m_Children.TryGetValue(name, out c))
                {
                    c.Clear();
                    m_Children.Remove(name);
                }
            }

            public void Clear()
            {
                if (m_Data != null) { ReferencePool.Release(m_Data); m_Data = null; }
                if (m_Children != null)
                {
                    foreach (var kv in m_Children) kv.Value.Clear();
                    m_Children.Clear();
                }
            }

            public DataNode GetNodeByPath(string path, bool createMissing)
            {
                string[] segs = path.Split(s_PathSeparators);
                DataNode cur = this;
                for (int i = 0; i < segs.Length; i++)
                {
                    string seg = segs[i];
                    DataNode next = cur.GetChildInternal(seg);
                    if (next == null)
                    {
                        if (!createMissing) return null;
                        next = (DataNode)cur.GetOrAddChild(seg);
                    }
                    cur = next;
                }
                return cur;
            }
        }
    }
}
