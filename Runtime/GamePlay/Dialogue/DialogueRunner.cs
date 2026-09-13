//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 分支对话运行游标：在一张 <see cref="DialogueGraph"/> 上推进，维护当前节点、变量与可见选项。
    /// 表现层（文字逐字、立绘、语音）由游戏侧实现，本类只负责图遍历与变量/条件逻辑。
    ///
    /// 核心语义：
    /// 进入节点：先按顺序应用 <see cref="LineNode.OnEnterEffects"/>（选项节点无进入效果），再触发 <see cref="OnNodeEntered"/>。
    /// 分支节点：落入即就地解析（依序取首个通过分支的目标，否则取默认目标），持续解析直至落在台词/选项节点或结束；
    /// 分支节点永不作为 <see cref="Current"/> 暴露。
    /// 防环：单次导航内最多解析 <see cref="MaxBranchHops"/> 跳，并以已访问集合检测环路；超限/成环即安全结束（不抛异常）。
    /// <see cref="Advance"/>：仅对台词节点有效，移动到其 NextId；对选项节点调用抛 InvalidOperationException。
    /// NextId 为 null/空 => 结束并触发一次 <see cref="OnFinished"/>。
    /// <see cref="Choose"/>：对 <see cref="AvailableChoices"/> 取索引，应用该选项效果后跳向其 TargetId。
    /// 选项节点若无任何可见选项 => 视为死路 => 立即结束。
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class DialogueRunner
    {
        /// <summary>
        /// 单次导航内解析分支节点的最大跳数上限，用于兜底防止异常图结构造成的失控循环。
        /// </summary>
        public const int MaxBranchHops = 256;

        private readonly DialogueVariables m_Variables;

        private DialogueGraph m_Graph;
        private DialogueNode m_Current;
        private bool m_IsFinished;

        // 当前选项节点过滤后的可见选项快照；非选项节点时为空列表。
        private readonly List<DialogueChoice> m_AvailableChoices = new List<DialogueChoice>();

        // 解析分支时复用的已访问集合，避免每步导航分配。
        private readonly HashSet<string> m_VisitedDuringNavigation =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 构造运行器。
        /// </summary>
        /// <param name="variables">外部变量存储；为 null 时新建一个空存储。</param>
        public DialogueRunner(DialogueVariables variables = null)
        {
            m_Variables = variables ?? new DialogueVariables();
        }

        /// <summary>
        /// 运行期变量存储。
        /// </summary>
        public DialogueVariables Variables
        {
            get { return m_Variables; }
        }

        /// <summary>
        /// 当前节点：始终为台词节点或选项节点（分支节点已被自动解析，永不暴露）。
        /// 未开始或已结束时为 null。
        /// </summary>
        public DialogueNode Current
        {
            get { return m_Current; }
        }

        /// <summary>
        /// 是否已结束（自然走到末尾、命中空目标或选项死路）。
        /// </summary>
        public bool IsFinished
        {
            get { return m_IsFinished; }
        }

        /// <summary>
        /// 当前是否在等待玩家做出选择（即 <see cref="Current"/> 为选项节点）。
        /// </summary>
        public bool IsAwaitingChoice
        {
            get { return m_Current is ChoiceNode; }
        }

        /// <summary>
        /// 当前选项节点中显示条件通过的可见选项（按定义顺序）。
        /// 非选项节点时为空列表。<see cref="Choose"/> 的索引即对应此列表。
        /// </summary>
        public IReadOnlyList<DialogueChoice> AvailableChoices
        {
            get { return m_AvailableChoices; }
        }

        /// <summary>
        /// 每进入一个台词/选项节点触发（在其进入效果应用之后）：(运行器, 进入的节点)。
        /// </summary>
        public event Action<DialogueRunner, DialogueNode> OnNodeEntered;

        /// <summary>
        /// 对话结束时触发一次：(运行器)。
        /// </summary>
        public event Action<DialogueRunner> OnFinished;

        /// <summary>
        /// 从图的起始节点开始运行。
        /// </summary>
        /// <param name="graph">对话图，不可为 null。</param>
        /// <exception cref="ArgumentNullException">graph 为 null。</exception>
        public void Start(DialogueGraph graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            Start(graph, graph.StartNodeId);
        }

        /// <summary>
        /// 从指定节点开始运行。
        /// </summary>
        /// <param name="graph">对话图，不可为 null。</param>
        /// <param name="startNodeId">起始节点 Id，不可为空。</param>
        /// <exception cref="ArgumentNullException">graph 为 null。</exception>
        /// <exception cref="ArgumentException">startNodeId 为空。</exception>
        public void Start(DialogueGraph graph, string startNodeId)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (string.IsNullOrEmpty(startNodeId))
            {
                throw new ArgumentException("起始节点 Id 不能为空。", nameof(startNodeId));
            }

            m_Graph = graph;
            m_Current = null;
            m_IsFinished = false;
            m_AvailableChoices.Clear();

            NavigateTo(startNodeId);
        }

        /// <summary>
        /// 推进当前台词节点到其 NextId（自动解析分支）。NextId 为 null/空则结束。
        /// 仅对台词节点有效；对选项节点调用将抛出异常。
        /// </summary>
        /// <exception cref="InvalidOperationException">已结束、未开始，或当前为选项节点。</exception>
        public void Advance()
        {
            if (m_IsFinished)
            {
                throw new InvalidOperationException("对话已结束，无法推进。");
            }

            if (m_Current == null)
            {
                throw new InvalidOperationException("对话尚未开始，无法推进。");
            }

            if (m_Current is ChoiceNode)
            {
                throw new InvalidOperationException("当前为选项节点，请调用 Choose 而非 Advance。");
            }

            LineNode line = (LineNode)m_Current;
            NavigateTo(line.NextId);
        }

        /// <summary>
        /// 在可见选项中选择第 index 个：应用该选项效果后跳向其目标（自动解析分支）。
        /// 目标为 null/空则结束。
        /// </summary>
        /// <param name="availableChoiceIndex">在 <see cref="AvailableChoices"/> 中的索引。</param>
        /// <exception cref="InvalidOperationException">已结束、未开始，或当前不是选项节点。</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引越界。</exception>
        public void Choose(int availableChoiceIndex)
        {
            if (m_IsFinished)
            {
                throw new InvalidOperationException("对话已结束，无法选择。");
            }

            if (!(m_Current is ChoiceNode))
            {
                throw new InvalidOperationException("当前不是选项节点，无法选择。");
            }

            if (availableChoiceIndex < 0 || availableChoiceIndex >= m_AvailableChoices.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(availableChoiceIndex),
                    $"选项索引越界：{availableChoiceIndex}，可见选项数：{m_AvailableChoices.Count}。");
            }

            DialogueChoice choice = m_AvailableChoices[availableChoiceIndex];

            // 先应用选项效果，再导航——后续目标的分支条件可读取到这些副作用。
            IReadOnlyList<DialogueEffect> effects = choice.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                effects[i].Apply(m_Variables);
            }

            NavigateTo(choice.TargetId);
        }

        /// <summary>
        /// 导航到目标节点：解析途中的分支节点，落在台词/选项节点则进入之，
        /// 目标为空或分支链尽头则结束。
        /// </summary>
        private void NavigateTo(string targetId)
        {
            m_VisitedDuringNavigation.Clear();

            string nextId = targetId;
            int hops = 0;

            while (true)
            {
                // 空目标 => 结束。
                if (string.IsNullOrEmpty(nextId))
                {
                    Finish();
                    return;
                }

                // 防环：跳数上限或重复访问同一节点 => 安全结束。
                if (hops >= MaxBranchHops || !m_VisitedDuringNavigation.Add(nextId))
                {
                    Finish();
                    return;
                }

                hops++;

                DialogueNode node = m_Graph.GetNode(nextId);

                // 目标缺失 => 视为断链 => 安全结束。
                if (node == null)
                {
                    Finish();
                    return;
                }

                if (node is BranchNode branch)
                {
                    // 分支节点不暴露，继续解析其求解出的目标。
                    nextId = branch.Resolve(m_Variables);
                    continue;
                }

                // 落在台词/选项节点：进入并停下。
                EnterNode(node);
                return;
            }
        }

        /// <summary>
        /// 进入一个台词/选项节点：应用进入效果（仅台词节点），刷新可见选项，触发 OnNodeEntered。
        /// 若进入的是无可见选项的选项节点，则视为死路立即结束。
        /// </summary>
        private void EnterNode(DialogueNode node)
        {
            if (node is LineNode line)
            {
                IReadOnlyList<DialogueEffect> effects = line.OnEnterEffects;
                for (int i = 0; i < effects.Count; i++)
                {
                    effects[i].Apply(m_Variables);
                }

                m_AvailableChoices.Clear();
                m_Current = node;
                OnNodeEntered?.Invoke(this, node);
                return;
            }

            if (node is ChoiceNode choiceNode)
            {
                RebuildAvailableChoices(choiceNode);

                // 无任何可见选项 => 死路 => 结束。
                if (m_AvailableChoices.Count == 0)
                {
                    Finish();
                    return;
                }

                m_Current = node;
                OnNodeEntered?.Invoke(this, node);
            }
        }

        // 依显示条件过滤选项节点的选项，写入复用列表。
        private void RebuildAvailableChoices(ChoiceNode choiceNode)
        {
            m_AvailableChoices.Clear();
            IReadOnlyList<DialogueChoice> all = choiceNode.Choices;
            for (int i = 0; i < all.Count; i++)
            {
                DialogueChoice choice = all[i];
                if (choice.ShowCondition == null || choice.ShowCondition.Evaluate(m_Variables))
                {
                    m_AvailableChoices.Add(choice);
                }
            }
        }

        // 转入结束态并触发一次 OnFinished。
        private void Finish()
        {
            m_Current = null;
            m_AvailableChoices.Clear();
            m_IsFinished = true;
            OnFinished?.Invoke(this);
        }
    }
}
