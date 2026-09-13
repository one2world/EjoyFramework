//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.Core.AI
{
    /// <summary>行为树节点执行结果。</summary>
    public enum NodeStatus
    {
        Success,
        Failure,
        Running,
    }

    /// <summary>行为树节点基类（业务派生 Action / Condition / Decorator）。</summary>
    public abstract class BehaviorNode<TContext>
    {
        public string Name;
        public abstract NodeStatus Tick(TContext context, Blackboard<TContext> blackboard);
        public virtual void Reset() { }   // 业务可重置内部状态
    }

    /// <summary>Sequence：按顺序执行子节点；遇 Failure 立即返回 Failure，全部 Success 才返回 Success。</summary>
    public sealed class SequenceNode<T> : BehaviorNode<T>
    {
        private readonly List<BehaviorNode<T>> m_Children = new List<BehaviorNode<T>>();
        private int m_Current;

        public SequenceNode<T> Add(BehaviorNode<T> child) { m_Children.Add(child); return this; }

        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
        {
            while (m_Current < m_Children.Count)
            {
                var s = m_Children[m_Current].Tick(ctx, bb);
                if (s == NodeStatus.Running) return NodeStatus.Running;
                if (s == NodeStatus.Failure) { m_Current = 0; return NodeStatus.Failure; }
                m_Current++;
            }
            m_Current = 0;
            return NodeStatus.Success;
        }

        public override void Reset()
        {
            m_Current = 0;
            foreach (var c in m_Children) c.Reset();
        }
    }

    /// <summary>Selector：按顺序尝试子节点；遇 Success 立即返回 Success，全部 Failure 才返回 Failure。</summary>
    public sealed class SelectorNode<T> : BehaviorNode<T>
    {
        private readonly List<BehaviorNode<T>> m_Children = new List<BehaviorNode<T>>();
        private int m_Current;

        public SelectorNode<T> Add(BehaviorNode<T> child) { m_Children.Add(child); return this; }

        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
        {
            while (m_Current < m_Children.Count)
            {
                var s = m_Children[m_Current].Tick(ctx, bb);
                if (s == NodeStatus.Running) return NodeStatus.Running;
                if (s == NodeStatus.Success) { m_Current = 0; return NodeStatus.Success; }
                m_Current++;
            }
            m_Current = 0;
            return NodeStatus.Failure;
        }

        public override void Reset()
        {
            m_Current = 0;
            foreach (var c in m_Children) c.Reset();
        }
    }

    /// <summary>Inverter：反转子节点结果（Success ↔ Failure；Running 保持）。</summary>
    public sealed class InverterNode<T> : BehaviorNode<T>
    {
        public BehaviorNode<T> Child;
        public InverterNode(BehaviorNode<T> child) { Child = child; }
        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
        {
            var s = Child.Tick(ctx, bb);
            if (s == NodeStatus.Success) return NodeStatus.Failure;
            if (s == NodeStatus.Failure) return NodeStatus.Success;
            return NodeStatus.Running;
        }
        public override void Reset() { Child?.Reset(); }
    }

    /// <summary>Condition：业务提供 Func 判断；返回 Success / Failure（无 Running）。</summary>
    public sealed class ConditionNode<T> : BehaviorNode<T>
    {
        public System.Func<T, Blackboard<T>, bool> Predicate;
        public ConditionNode(System.Func<T, Blackboard<T>, bool> pred) { Predicate = pred; }
        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
            => Predicate(ctx, bb) ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>Action：业务提供 Func；返回 NodeStatus。</summary>
    public sealed class ActionNode<T> : BehaviorNode<T>
    {
        public System.Func<T, Blackboard<T>, NodeStatus> Run;
        public ActionNode(System.Func<T, Blackboard<T>, NodeStatus> run) { Run = run; }
        public override NodeStatus Tick(T ctx, Blackboard<T> bb) => Run(ctx, bb);
    }

    /// <summary>Succeeder：无论子节点返回什么都返回 Success（Running 时仍透传 Running）。</summary>
    public sealed class SucceederNode<T> : BehaviorNode<T>
    {
        public BehaviorNode<T> Child;
        public SucceederNode(BehaviorNode<T> child) { Child = child; }
        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
        {
            var s = Child.Tick(ctx, bb);
            return s == NodeStatus.Running ? NodeStatus.Running : NodeStatus.Success;
        }
        public override void Reset() { Child?.Reset(); }
    }

    /// <summary>Parallel 成功策略：决定何时返回 Success / Failure。</summary>
    public enum ParallelPolicy
    {
        RequireOne,     // 任一子节点 Success → Success；全部 Failure → Failure
        RequireAll,     // 全部 Success → Success；任一 Failure → Failure
    }

    /// <summary>
    /// Parallel：每次 Tick 同时推进所有子节点。
    /// RequireOne：任一子 Success 即 Success；全部结束且无 Success 时 Failure。
    /// RequireAll：任一子 Failure 即 Failure；全部 Success 才 Success。
    /// 仍有子节点 Running 且未触发提前结束条件时返回 Running。
    /// </summary>
    public sealed class ParallelNode<T> : BehaviorNode<T>
    {
        private readonly List<BehaviorNode<T>> m_Children = new List<BehaviorNode<T>>();
        private readonly ParallelPolicy m_Policy;

        public ParallelNode(ParallelPolicy policy = ParallelPolicy.RequireAll) { m_Policy = policy; }

        public ParallelNode<T> Add(BehaviorNode<T> child) { m_Children.Add(child); return this; }

        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
        {
            int successCount = 0;
            int failureCount = 0;
            for (int i = 0; i < m_Children.Count; i++)
            {
                var s = m_Children[i].Tick(ctx, bb);
                if (s == NodeStatus.Success)
                {
                    successCount++;
                    if (m_Policy == ParallelPolicy.RequireOne) { Reset(); return NodeStatus.Success; }
                }
                else if (s == NodeStatus.Failure)
                {
                    failureCount++;
                    if (m_Policy == ParallelPolicy.RequireAll) { Reset(); return NodeStatus.Failure; }
                }
            }

            if (m_Policy == ParallelPolicy.RequireAll)
            {
                if (successCount == m_Children.Count) { Reset(); return NodeStatus.Success; }
            }
            else // RequireOne
            {
                if (failureCount == m_Children.Count) { Reset(); return NodeStatus.Failure; }
            }
            return NodeStatus.Running;
        }

        public override void Reset()
        {
            foreach (var c in m_Children) c.Reset();
        }
    }

    /// <summary>
    /// Repeat：重复执行子节点。
    /// count &gt; 0：执行 count 次（每次完成一轮才计数），全部成功则 Success，期间子 Failure 立即 Failure。
    /// count &lt;= 0：无限重复直到子节点 Failure（用于守护循环），子 Failure 时返回 Failure。
    /// 子节点 Running 时透传 Running，不消耗计数。
    /// </summary>
    public sealed class RepeatNode<T> : BehaviorNode<T>
    {
        public BehaviorNode<T> Child;
        private readonly int m_Count;
        private int m_Completed;

        public RepeatNode(BehaviorNode<T> child, int count = 0) { Child = child; m_Count = count; }

        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
        {
            while (true)
            {
                var s = Child.Tick(ctx, bb);
                if (s == NodeStatus.Running) return NodeStatus.Running;
                if (s == NodeStatus.Failure) { m_Completed = 0; Child.Reset(); return NodeStatus.Failure; }

                // Success：完成一轮
                Child.Reset();
                m_Completed++;
                if (m_Count > 0 && m_Completed >= m_Count) { m_Completed = 0; return NodeStatus.Success; }
                // count <= 0 时无限重复：每完成一轮即让出（返回 Running），下一次 Tick 再开新一轮。
                // 若就地继续 while(true)，子节点同步返回 Success（如 ConditionNode）会导致永不退出、卡死主线程。
                if (m_Count <= 0) return NodeStatus.Running;
            }
        }

        public override void Reset() { m_Completed = 0; Child?.Reset(); }
    }

    /// <summary>
    /// Cooldown：时间闸门。子节点上次返回 Success 后，在 cooldownSeconds 内被再次 Tick 时直接返回 Failure。
    /// 冷却就绪时透传子节点结果，并在子节点 Success 时重新开始计时。
    /// 时间由 BehaviorTree.Tick(context, elapseSeconds) 累加到黑板的保留 key（见 BehaviorTree.DeltaKey）。
    /// 不依赖 UnityEngine.Time —— 完全由调用方传入的 elapseSeconds 驱动。
    /// </summary>
    public sealed class CooldownNode<T> : BehaviorNode<T>
    {
        public BehaviorNode<T> Child;
        private readonly float m_CooldownSeconds;
        private float m_Remaining;

        public CooldownNode(BehaviorNode<T> child, float cooldownSeconds)
        {
            Child = child;
            m_CooldownSeconds = cooldownSeconds;
        }

        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
        {
            if (m_Remaining > 0f)
            {
                m_Remaining -= bb.Get<float>(BehaviorTree<T>.DeltaKey);
                if (m_Remaining > 0f) return NodeStatus.Failure;
                m_Remaining = 0f;
            }

            var s = Child.Tick(ctx, bb);
            if (s == NodeStatus.Success) m_Remaining = m_CooldownSeconds;
            return s;
        }

        public override void Reset() { m_Remaining = 0f; Child?.Reset(); }
    }

    /// <summary>
    /// Wait：纯时间等待节点。累计被 Tick 的 elapseSeconds 直到达到 waitSeconds，期间返回 Running，达到后返回 Success。
    /// 时间来源同 CooldownNode：黑板保留 key（BehaviorTree.DeltaKey），不使用 UnityEngine.Time。
    /// </summary>
    public sealed class WaitNode<T> : BehaviorNode<T>
    {
        private readonly float m_WaitSeconds;
        private float m_Elapsed;

        public WaitNode(float waitSeconds) { m_WaitSeconds = waitSeconds; }

        public override NodeStatus Tick(T ctx, Blackboard<T> bb)
        {
            m_Elapsed += bb.Get<float>(BehaviorTree<T>.DeltaKey);
            if (m_Elapsed >= m_WaitSeconds) { m_Elapsed = 0f; return NodeStatus.Success; }
            return NodeStatus.Running;
        }

        public override void Reset() { m_Elapsed = 0f; }
    }

    /// <summary>
    /// 行为树：业务 new BehaviorTree<TOwner>(root, blackboard) 后每帧 Tick(owner)。
    /// </summary>
    public sealed class BehaviorTree<TContext>
    {
        /// <summary>时间闸门节点（Cooldown / Wait）读取每帧 delta 的黑板保留 key。</summary>
        public const string DeltaKey = "__bt_delta";

        public BehaviorNode<TContext> Root { get; }
        public Blackboard<TContext> Blackboard { get; }
        public NodeStatus LastStatus { get; private set; }

        public BehaviorTree(BehaviorNode<TContext> root, Blackboard<TContext> blackboard = null)
        {
            Root = root ?? throw new FrameworkException("Behavior tree root is null.");
            Blackboard = blackboard ?? new Blackboard<TContext>();
        }

        public NodeStatus Tick(TContext context)
        {
            LastStatus = Root.Tick(context, Blackboard);
            return LastStatus;
        }

        /// <summary>
        /// 带帧时间的 Tick：将 elapseSeconds 写入黑板保留 key，供时间闸门节点（Cooldown / Wait）读取。
        /// 时间完全由调用方传入，不依赖 UnityEngine.Time。
        /// </summary>
        public NodeStatus Tick(TContext context, float elapseSeconds)
        {
            Blackboard.Set(DeltaKey, elapseSeconds);
            return Tick(context);
        }

        public void Reset() => Root.Reset();
    }
}
