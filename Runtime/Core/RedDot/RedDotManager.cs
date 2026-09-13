//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.RedDot
{
    /// <summary>
    /// 红点管理器（完整实现）。
    ///
    /// 数据结构：Dictionary&lt;string, Node&gt;，每个 Node 记录：
    ///   - OwnLeafCount：该节点作为叶子来源时的自身计数
    ///   - Aggregate   ：有效计数 = OwnLeafCount + 所有子节点 Aggregate 之和（即"后代叶子计数之和"）
    ///   - ParentPath  ：父节点规整路径（root 节点为 null）
    ///   - Children    ：子节点路径集合
    ///   - Subscribers ：该节点的有效计数变化订阅者
    ///
    /// 传播：SetLeafCount 设置叶子 OwnLeafCount 后，从该节点起逐级向上
    ///       重算 Aggregate = OwnLeafCount + sum(child.Aggregate)；
    ///       仅当某节点 Aggregate 真正发生变化时才触发其订阅者（旧值 vs 新值）。
    /// </summary>
    internal sealed class RedDotManager : FrameworkModule, IRedDotManager
    {
        private readonly Dictionary<string, Node> m_Nodes = new Dictionary<string, Node>(StringComparer.Ordinal);

        public RedDotManager()
        {
        }

        // Priority 0：纯数据业务模块，无 Update 依赖。
        public override int Priority { get { return 0; } }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        public override void Shutdown()
        {
            m_Nodes.Clear();
        }

        // ===== 公开 API =====

        public void SetLeafCount(string path, int count)
        {
            Framework.EnsureMainThread(nameof(SetLeafCount));
            string normalized = Normalize(path);
            if (normalized == null)
            {
                FrameworkLog.Warning("[RedDot] SetLeafCount: path is invalid.");
                return;
            }
            if (count < 0) count = 0;

            Node node = GetOrCreate(normalized);
            node.OwnLeafCount = count;

            // 从本节点起逐级向上重算 Aggregate，沿途仅对真正变化的节点触发订阅者。
            Recompute(normalized);
        }

        public int GetCount(string path)
        {
            string normalized = Normalize(path);
            if (normalized == null) return 0;
            return m_Nodes.TryGetValue(normalized, out Node node) ? node.Aggregate : 0;
        }

        public bool IsActive(string path)
        {
            return GetCount(path) > 0;
        }

        public void Subscribe(string path, Action<int> onChanged)
        {
            Framework.EnsureMainThread(nameof(Subscribe));
            if (onChanged == null) throw new FrameworkException("RedDot subscriber is invalid.");
            string normalized = Normalize(path);
            if (normalized == null)
            {
                FrameworkLog.Warning("[RedDot] Subscribe: path is invalid.");
                return;
            }
            Node node = GetOrCreate(normalized);
            node.Subscribers += onChanged;
        }

        public void Unsubscribe(string path, Action<int> onChanged)
        {
            Framework.EnsureMainThread(nameof(Unsubscribe));
            if (onChanged == null) return;
            string normalized = Normalize(path);
            if (normalized == null) return;
            if (m_Nodes.TryGetValue(normalized, out Node node))
            {
                node.Subscribers -= onChanged;
            }
        }

        public void Clear()
        {
            Framework.EnsureMainThread(nameof(Clear));
            m_Nodes.Clear();
        }

        // ===== 内部实现 =====

        /// <summary>
        /// 规整路径：去首尾空白、去首尾 '/'、合并连续 '/'。无效返回 null。
        /// </summary>
        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string[] segments = path.Split('/');
            // 复用 StringBuilder 不值得（路径短），直接拼接。
            string result = null;
            for (int i = 0; i < segments.Length; i++)
            {
                string seg = segments[i].Trim();
                if (seg.Length == 0) continue;
                result = result == null ? seg : result + "/" + seg;
            }
            return result;
        }

        /// <summary>
        /// 取出父路径（规整路径），root 返回 null。
        /// </summary>
        private static string ParentOf(string normalizedPath)
        {
            int idx = normalizedPath.LastIndexOf('/');
            return idx < 0 ? null : normalizedPath.Substring(0, idx);
        }

        /// <summary>
        /// 获取节点；不存在则连同所有祖先一起创建并挂接父子关系。
        /// </summary>
        private Node GetOrCreate(string normalizedPath)
        {
            if (m_Nodes.TryGetValue(normalizedPath, out Node existing)) return existing;

            string parentPath = ParentOf(normalizedPath);
            var node = new Node { ParentPath = parentPath };
            m_Nodes.Add(normalizedPath, node);

            if (parentPath != null)
            {
                Node parent = GetOrCreate(parentPath);
                parent.Children.Add(normalizedPath);
            }
            return node;
        }

        /// <summary>
        /// 从 startPath 起逐级向上重算 Aggregate。
        /// 每个节点 Aggregate = OwnLeafCount + sum(child.Aggregate)；
        /// 仅当某节点的 Aggregate 真正变化时才触发其订阅者（携带新值）。
        /// </summary>
        private void Recompute(string startPath)
        {
            string current = startPath;
            while (current != null)
            {
                if (!m_Nodes.TryGetValue(current, out Node node)) break;

                int newAggregate = node.OwnLeafCount;
                foreach (string childPath in node.Children)
                {
                    if (m_Nodes.TryGetValue(childPath, out Node child))
                    {
                        newAggregate += child.Aggregate;
                    }
                }

                if (newAggregate == node.Aggregate)
                {
                    // 本节点未变：其有效计数对父节点的贡献不变，无需继续上溯。
                    break;
                }

                node.Aggregate = newAggregate;
                FireChanged(node, newAggregate);

                current = node.ParentPath;
            }
        }

        private static void FireChanged(Node node, int newValue)
        {
            Action<int> handler = node.Subscribers;
            if (handler == null) return;

            // 多播逐个隔离，单个订阅者抛异常不影响其余。
            Delegate[] list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action<int>)list[i])(newValue); }
                catch (Exception ex) { FrameworkLog.Error("[RedDot] subscriber threw: {0}", ex); }
            }
        }

        private sealed class Node
        {
            public int OwnLeafCount;
            public int Aggregate;
            public string ParentPath;
            public readonly HashSet<string> Children = new HashSet<string>(StringComparer.Ordinal);
            public Action<int> Subscribers;
        }
    }
}
