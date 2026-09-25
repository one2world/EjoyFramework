//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.RedDot
{
    /// <summary>
    /// 红点管理器（完整实现）。
    ///
    /// 数据结构：StringMap&lt;Node&gt;（规整路径 → 节点），每个 Node 记录：
    ///   - OwnLeafCount：该节点作为叶子来源时的自身计数
    ///   - Aggregate   ：有效计数 = OwnLeafCount + 所有子节点 Aggregate 之和（即"后代叶子计数之和"）
    ///   - Parent      ：父节点引用（root 节点为 null）
    ///   - Children    ：子节点数组
    ///   - Subscribers ：该节点的有效计数变化订阅者（写时复制数组）
    ///
    /// 传播：SetLeafCount 设置叶子 OwnLeafCount 后，从该节点起沿 Parent 引用逐级向上
    ///       重算 Aggregate = OwnLeafCount + sum(child.Aggregate)；
    ///       仅当某节点 Aggregate 真正发生变化时才触发其订阅者（旧值 vs 新值）。
    ///
    /// 零分配：已存在节点上的 GetCount / IsActive / SetLeafCount / 传播 / 触发都不分配——
    /// 路径已规整时直接查表，未规整（首尾 '/'、连续 '/'、段两侧空白）时在栈缓冲里规整后按切片查表；
    /// 订阅者存为写时复制数组，触发时遍历快照（与多播委托相同的"触发时快照"语义），不再 GetInvocationList。
    /// 只有首次创建节点与增删订阅会分配。
    /// </summary>
    internal sealed class RedDotManager : FrameworkModule, IRedDotManager
    {
        /// <summary>栈上规整路径的缓冲上限，更长的路径租池化缓冲。</summary>
        private const int StackPathLength = 256;

        private static readonly Action<int>[] s_NoSubscribers = new Action<int>[0];

        private readonly StringMap<Node> m_Nodes = new StringMap<Node>();

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
            Node node = GetOrCreate(path);
            if (node == null)
            {
                FrameworkLog.Warning("[RedDot] SetLeafCount: path is invalid.");
                return;
            }
            if (count < 0) count = 0;

            node.OwnLeafCount = count;

            // 从本节点起逐级向上重算 Aggregate，沿途仅对真正变化的节点触发订阅者。
            Recompute(node);
        }

        public int GetCount(string path)
        {
            Node node = Find(path);
            return node != null ? node.Aggregate : 0;
        }

        public bool IsActive(string path)
        {
            return GetCount(path) > 0;
        }

        public void Subscribe(string path, Action<int> onChanged)
        {
            Framework.EnsureMainThread(nameof(Subscribe));
            if (onChanged == null) throw new FrameworkException("RedDot subscriber is invalid.");
            Node node = GetOrCreate(path);
            if (node == null)
            {
                FrameworkLog.Warning("[RedDot] Subscribe: path is invalid.");
                return;
            }

            Action<int>[] old = node.Subscribers;
            Action<int>[] list = new Action<int>[old.Length + 1];
            Array.Copy(old, list, old.Length);
            list[old.Length] = onChanged;
            node.Subscribers = list;
        }

        public void Unsubscribe(string path, Action<int> onChanged)
        {
            Framework.EnsureMainThread(nameof(Unsubscribe));
            if (onChanged == null) return;
            Node node = Find(path);
            if (node == null) return;

            // 与多播委托 -= 相同：移除最后一个相等的订阅。
            Action<int>[] old = node.Subscribers;
            for (int i = old.Length - 1; i >= 0; i--)
            {
                if (!old[i].Equals(onChanged)) continue;
                if (old.Length == 1)
                {
                    node.Subscribers = s_NoSubscribers;
                    return;
                }

                Action<int>[] list = new Action<int>[old.Length - 1];
                Array.Copy(old, 0, list, 0, i);
                Array.Copy(old, i + 1, list, i, old.Length - i - 1);
                node.Subscribers = list;
                return;
            }
        }

        public void Clear()
        {
            Framework.EnsureMainThread(nameof(Clear));
            m_Nodes.Clear();
        }

        // ===== 内部实现 =====

        /// <summary>
        /// 查找已存在节点，零分配。路径无效或不存在返回 null。
        /// </summary>
        private Node Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Node node;
            if (IsNormalized(path))
            {
                return m_Nodes.TryGetValue(path, out node) ? node : null;
            }

            if (path.Length <= StackPathLength)
            {
                Span<char> buffer = stackalloc char[StackPathLength];
                int length = Normalize(path, buffer);
                return length > 0 && m_Nodes.TryGetValue(buffer.Slice(0, length), out node) ? node : null;
            }

            char[] rented = CharBufferPool.Rent(path.Length);
            try
            {
                int length = Normalize(path, rented);
                return length > 0 && m_Nodes.TryGetValue(new ReadOnlySpan<char>(rented, 0, length), out node) ? node : null;
            }
            finally
            {
                CharBufferPool.Return(rented);
            }
        }

        /// <summary>
        /// 获取节点；不存在则连同所有祖先一起创建并挂接父子关系。路径无效返回 null。
        /// </summary>
        private Node GetOrCreate(string path)
        {
            Node existing = Find(path);
            if (existing != null) return existing;
            if (string.IsNullOrEmpty(path)) return null;

            string normalized;
            if (IsNormalized(path))
            {
                normalized = path;
            }
            else
            {
                char[] rented = CharBufferPool.Rent(path.Length);
                try
                {
                    int length = Normalize(path, rented);
                    if (length == 0) return null;
                    normalized = new string(rented, 0, length);
                }
                finally
                {
                    CharBufferPool.Return(rented);
                }
            }

            return GetOrCreateNormalized(normalized);
        }

        private Node GetOrCreateNormalized(string normalizedPath)
        {
            Node node;
            if (m_Nodes.TryGetValue(normalizedPath, out node)) return node;

            int slash = normalizedPath.LastIndexOf('/');
            Node parent = slash < 0 ? null : GetOrCreateNormalized(normalizedPath.Substring(0, slash));
            node = new Node(parent);
            m_Nodes.Add(normalizedPath, node);
            if (parent != null) parent.AddChild(node);
            return node;
        }

        /// <summary>
        /// 路径是否已是规整形式：非空、首尾不是 '/'、没有空段、段两侧没有空白。
        /// </summary>
        private static bool IsNormalized(string path)
        {
            int length = path.Length;
            if (length == 0 || path[0] == '/' || path[length - 1] == '/') return false;
            for (int i = 0; i < length; i++)
            {
                char c = path[i];
                if (c == '/')
                {
                    if (path[i + 1] == '/' || char.IsWhiteSpace(path[i - 1]) || char.IsWhiteSpace(path[i + 1])) return false;
                }
                else if ((i == 0 || i == length - 1) && char.IsWhiteSpace(c))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 规整路径写入 destination（容量不小于 path.Length）：按 '/' 切段、每段去两侧空白、丢弃空段、以单个 '/' 连接。
        /// 返回写入长度，0 表示无有效段。
        /// </summary>
        private static int Normalize(string path, Span<char> destination)
        {
            int written = 0;
            int start = 0;
            while (start <= path.Length)
            {
                int end = path.IndexOf('/', start);
                if (end < 0) end = path.Length;
                ReadOnlySpan<char> segment = path.AsSpan(start, end - start).Trim();
                if (segment.Length > 0)
                {
                    if (written > 0) destination[written++] = '/';
                    segment.CopyTo(destination.Slice(written));
                    written += segment.Length;
                }
                start = end + 1;
            }
            return written;
        }

        /// <summary>
        /// 从 start 起逐级向上重算 Aggregate。
        /// 每个节点 Aggregate = OwnLeafCount + sum(child.Aggregate)；
        /// 仅当某节点的 Aggregate 真正变化时才触发其订阅者（携带新值）。
        /// </summary>
        private static void Recompute(Node start)
        {
            Node node = start;
            while (node != null)
            {
                int newAggregate = node.OwnLeafCount;
                Node[] children = node.Children;
                for (int i = 0; i < node.ChildCount; i++)
                {
                    newAggregate += children[i].Aggregate;
                }

                if (newAggregate == node.Aggregate)
                {
                    // 本节点未变：其有效计数对父节点的贡献不变，无需继续上溯。
                    break;
                }

                node.Aggregate = newAggregate;
                FireChanged(node, newAggregate);

                node = node.Parent;
            }
        }

        private static void FireChanged(Node node, int newValue)
        {
            // 遍历触发时的快照：回调里增删订阅会换新数组，不影响本轮。
            Action<int>[] list = node.Subscribers;
            for (int i = 0; i < list.Length; i++)
            {
                // 逐个隔离，单个订阅者抛异常不影响其余。
                try { list[i](newValue); }
                catch (Exception ex) { FrameworkLog.Error("[RedDot] subscriber threw: {0}", ex); }
            }
        }

        private sealed class Node
        {
            public readonly Node Parent;
            public int OwnLeafCount;
            public int Aggregate;
            public Node[] Children;
            public int ChildCount;
            public Action<int>[] Subscribers = s_NoSubscribers;

            public Node(Node parent)
            {
                Parent = parent;
            }

            public void AddChild(Node child)
            {
                if (Children == null)
                {
                    Children = new Node[4];
                }
                else if (ChildCount == Children.Length)
                {
                    Array.Resize(ref Children, ChildCount * 2);
                }

                Children[ChildCount++] = child;
            }
        }
    }
}
