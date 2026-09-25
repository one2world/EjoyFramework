//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.DataNode
{
    /// <summary>
    /// 数据结点管理器。路径分隔符为 '.'、'/' 与反斜杠（可混用）；空段被忽略（"a..b"、".a."、"a/" 均等价于对应的规整路径，
    /// 旧实现会创建名为空串的结点，属于错误行为），只由分隔符组成的路径指向根结点。
    /// 路径按切片逐段查找（子结点表为 <see cref="StringMap{TValue}"/>），查找已存在结点零分配；只有新建结点时为段名分配 string。
    /// </summary>
    internal sealed class DataNodeManager : FrameworkModule, IDataNodeManager
    {
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
            ReadOnlySpan<char> trimmed = path.AsSpan().TrimEnd(s_Separators);
            if (trimmed.Length == 0) return;   // 只有分隔符：指向根，根不可移除
            int last = trimmed.LastIndexOfAny('.', '/', '\\');
            DataNode parent = last < 0 ? m_Root : m_Root.GetNodeByPath(trimmed.Slice(0, last), false);
            if (parent != null) parent.RemoveChild(trimmed.Slice(last + 1));
        }

        private static readonly char[] s_Separators = { '.', '/', '\\' };

        private static int IndexOfSeparator(ReadOnlySpan<char> path)
        {
            return path.IndexOfAny('.', '/', '\\');
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
            private StringMap<DataNode> m_Children;
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

            public DataNode GetChildInternal(ReadOnlySpan<char> name)
            {
                if (m_Children == null) return null;
                DataNode c;
                return m_Children.TryGetValue(name, out c) ? c : null;
            }

            public IDataNode GetOrAddChild(string name)
            {
                if (name == null) throw new FrameworkException("DataNode.GetOrAddChild: name is null.");
                if (m_Children == null) m_Children = new StringMap<DataNode>();
                DataNode c;
                if (!m_Children.TryGetValue(name, out c))
                {
                    c = new DataNode(name, this);
                    m_Children.Add(name, c);
                }
                return c;
            }

            private DataNode GetOrAddChild(ReadOnlySpan<char> name)
            {
                DataNode c = GetChildInternal(name);
                if (c != null) return c;
                if (m_Children == null) m_Children = new StringMap<DataNode>();
                c = new DataNode(name.ToString(), this);
                m_Children.Add(c.m_Name, c);
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
                if (name == null) return;
                RemoveChild(name.AsSpan());
            }

            public void RemoveChild(ReadOnlySpan<char> name)
            {
                if (m_Children == null) return;
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
                return GetNodeByPath(path.AsSpan(), createMissing);
            }

            public DataNode GetNodeByPath(ReadOnlySpan<char> path, bool createMissing)
            {
                ReadOnlySpan<char> rest = path;
                DataNode cur = this;
                while (true)
                {
                    int separator = IndexOfSeparator(rest);
                    ReadOnlySpan<char> seg = separator < 0 ? rest : rest.Slice(0, separator);
                    if (seg.Length > 0)
                    {
                        DataNode next = cur.GetChildInternal(seg);
                        if (next == null)
                        {
                            if (!createMissing) return null;
                            next = cur.GetOrAddChild(seg);
                        }
                        cur = next;
                    }
                    if (separator < 0) return cur;
                    rest = rest.Slice(separator + 1);
                }
            }
        }
    }
}
