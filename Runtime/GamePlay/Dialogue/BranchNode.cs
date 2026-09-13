//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 一条分支：条件 + 目标。用于旗标驱动的自动路由（如乙女线路），不向玩家呈现选项。
    /// 不可变值对象。
    /// </summary>
    public sealed class DialogueBranch
    {
        private readonly DialogueCondition m_Condition;
        private readonly string m_TargetId;

        /// <summary>
        /// 构造分支。
        /// </summary>
        /// <param name="condition">分支条件，不可为 null。</param>
        /// <param name="targetId">条件通过时跳转的目标节点 Id；null/空表示结束。</param>
        /// <exception cref="ArgumentNullException">condition 为 null。</exception>
        public DialogueBranch(DialogueCondition condition, string targetId)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            m_Condition = condition;
            m_TargetId = targetId;
        }

        /// <summary>
        /// 分支条件。
        /// </summary>
        public DialogueCondition Condition
        {
            get { return m_Condition; }
        }

        /// <summary>
        /// 条件通过时跳转的目标节点 Id。
        /// </summary>
        public string TargetId
        {
            get { return m_TargetId; }
        }
    }

    /// <summary>
    /// 分支节点：落入即依序求值各分支条件，跳向首个通过分支的目标；
    /// 全部不通过时取 <see cref="DefaultTargetId"/>；后者为 null/空则结束对话。
    /// 分支节点永不作为运行游标的 Current 暴露——运行器会持续解析直到落在台词/选项节点或结束。
    /// 不可变值对象。
    /// </summary>
    public sealed class BranchNode : DialogueNode
    {
        private readonly IReadOnlyList<DialogueBranch> m_Branches;
        private readonly string m_DefaultTargetId;

        /// <summary>
        /// 构造分支节点。
        /// </summary>
        /// <param name="id">节点 Id，不可为空。</param>
        /// <param name="branches">分支集合，不可为 null（可为空，则总是走默认目标）。</param>
        /// <param name="defaultTargetId">无分支通过时的默认目标；null/空表示结束。</param>
        /// <exception cref="ArgumentNullException">branches 为 null。</exception>
        public BranchNode(string id, IReadOnlyList<DialogueBranch> branches, string defaultTargetId)
            : base(id)
        {
            if (branches == null)
            {
                throw new ArgumentNullException(nameof(branches));
            }

            DialogueBranch[] copy = new DialogueBranch[branches.Count];
            for (int i = 0; i < branches.Count; i++)
            {
                if (branches[i] == null)
                {
                    throw new ArgumentException("分支不能为 null。", nameof(branches));
                }

                copy[i] = branches[i];
            }

            m_Branches = copy;
            m_DefaultTargetId = defaultTargetId;
        }

        /// <summary>
        /// 全部分支（只读）。求值时依序取首个条件通过者。
        /// </summary>
        public IReadOnlyList<DialogueBranch> Branches
        {
            get { return m_Branches; }
        }

        /// <summary>
        /// 默认目标节点 Id。无分支通过时采用；null/空表示结束。
        /// </summary>
        public string DefaultTargetId
        {
            get { return m_DefaultTargetId; }
        }

        /// <summary>
        /// 依据变量存储求解本节点应跳向的目标节点 Id。
        /// 依序取首个条件通过的分支目标，全部不通过则取默认目标。
        /// </summary>
        /// <param name="vars">变量存储。</param>
        /// <returns>目标节点 Id；null/空表示结束。</returns>
        internal string Resolve(DialogueVariables vars)
        {
            for (int i = 0; i < m_Branches.Count; i++)
            {
                DialogueBranch branch = m_Branches[i];
                if (branch.Condition.Evaluate(vars))
                {
                    return branch.TargetId;
                }
            }

            return m_DefaultTargetId;
        }

        /// <summary>
        /// 创建分支节点构造器。
        /// </summary>
        /// <param name="id">节点 Id。</param>
        /// <returns>构造器。</returns>
        public static Builder Create(string id)
        {
            return new Builder(id);
        }

        /// <summary>
        /// <see cref="BranchNode"/> 的链式构造器。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private readonly List<DialogueBranch> m_Branches = new List<DialogueBranch>();
            private string m_DefaultTargetId;

            internal Builder(string id)
            {
                m_Id = id;
            }

            /// <summary>
            /// 追加一条分支。
            /// </summary>
            /// <param name="condition">分支条件。</param>
            /// <param name="targetId">通过时的目标。</param>
            public Builder When(DialogueCondition condition, string targetId)
            {
                m_Branches.Add(new DialogueBranch(condition, targetId));
                return this;
            }

            /// <summary>
            /// 设置默认目标（无分支通过时）。
            /// </summary>
            public Builder Default(string defaultTargetId)
            {
                m_DefaultTargetId = defaultTargetId;
                return this;
            }

            /// <summary>
            /// 构建分支节点。
            /// </summary>
            public BranchNode Build()
            {
                return new BranchNode(m_Id, m_Branches, m_DefaultTargetId);
            }
        }
    }
}
