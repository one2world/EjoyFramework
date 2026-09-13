//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 一个对话选项：展示文本、跳转目标、可选的显示条件与选中后效果。
    /// <see cref="ShowCondition"/> 为 null 表示始终显示。选中时按顺序应用 <see cref="Effects"/>。
    /// 不可变值对象。
    /// </summary>
    public sealed class DialogueChoice
    {
        private static readonly IReadOnlyList<DialogueEffect> s_EmptyEffects = new DialogueEffect[0];

        private readonly string m_Text;
        private readonly string m_TargetId;
        private readonly DialogueCondition m_ShowCondition;
        private readonly IReadOnlyList<DialogueEffect> m_Effects;

        /// <summary>
        /// 构造选项。
        /// </summary>
        /// <param name="text">选项展示文本。</param>
        /// <param name="targetId">选中后跳转的目标节点 Id；null/空表示选中即结束。</param>
        /// <param name="showCondition">显示条件，null 表示始终显示。</param>
        /// <param name="effects">选中后应用的效果，可为 null。</param>
        public DialogueChoice(
            string text,
            string targetId,
            DialogueCondition showCondition = null,
            IReadOnlyList<DialogueEffect> effects = null)
        {
            m_Text = text;
            m_TargetId = targetId;
            m_ShowCondition = showCondition;
            m_Effects = CopyEffects(effects);
        }

        /// <summary>
        /// 选项展示文本。
        /// </summary>
        public string Text
        {
            get { return m_Text; }
        }

        /// <summary>
        /// 选中后跳转的目标节点 Id。null/空表示选中即结束对话。
        /// </summary>
        public string TargetId
        {
            get { return m_TargetId; }
        }

        /// <summary>
        /// 显示条件。null 表示始终显示。
        /// </summary>
        public DialogueCondition ShowCondition
        {
            get { return m_ShowCondition; }
        }

        /// <summary>
        /// 被选中时应用的效果列表（只读，非 null）。
        /// </summary>
        public IReadOnlyList<DialogueEffect> Effects
        {
            get { return m_Effects; }
        }

        private static IReadOnlyList<DialogueEffect> CopyEffects(IReadOnlyList<DialogueEffect> source)
        {
            if (source == null || source.Count == 0)
            {
                return s_EmptyEffects;
            }

            DialogueEffect[] copy = new DialogueEffect[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }

    /// <summary>
    /// 选项节点：呈现一组 <see cref="DialogueChoice"/> 供玩家选择。
    /// <see cref="Prompt"/> 为可选的提示/说话人文本。不可变值对象。
    /// </summary>
    public sealed class ChoiceNode : DialogueNode
    {
        private readonly string m_Prompt;
        private readonly IReadOnlyList<DialogueChoice> m_Choices;

        /// <summary>
        /// 构造选项节点。
        /// </summary>
        /// <param name="id">节点 Id，不可为空。</param>
        /// <param name="prompt">提示文本，可为空。</param>
        /// <param name="choices">选项集合，不可为 null 或空。</param>
        /// <exception cref="ArgumentException">choices 为 null 或空。</exception>
        public ChoiceNode(string id, string prompt, IReadOnlyList<DialogueChoice> choices)
            : base(id)
        {
            if (choices == null || choices.Count == 0)
            {
                throw new ArgumentException("选项节点至少需要一个选项。", nameof(choices));
            }

            DialogueChoice[] copy = new DialogueChoice[choices.Count];
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i] == null)
                {
                    throw new ArgumentException("选项不能为 null。", nameof(choices));
                }

                copy[i] = choices[i];
            }

            m_Prompt = prompt;
            m_Choices = copy;
        }

        /// <summary>
        /// 提示/说话人文本（可选）。
        /// </summary>
        public string Prompt
        {
            get { return m_Prompt; }
        }

        /// <summary>
        /// 全部选项（只读，含未通过显示条件者）。运行期可见的子集见 <see cref="DialogueRunner.AvailableChoices"/>。
        /// </summary>
        public IReadOnlyList<DialogueChoice> Choices
        {
            get { return m_Choices; }
        }

        /// <summary>
        /// 创建选项节点构造器。
        /// </summary>
        /// <param name="id">节点 Id。</param>
        /// <returns>构造器。</returns>
        public static Builder Create(string id)
        {
            return new Builder(id);
        }

        /// <summary>
        /// <see cref="ChoiceNode"/> 的链式构造器。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private string m_Prompt;
            private readonly List<DialogueChoice> m_Choices = new List<DialogueChoice>();

            internal Builder(string id)
            {
                m_Id = id;
            }

            /// <summary>
            /// 设置提示文本。
            /// </summary>
            public Builder Prompt(string prompt)
            {
                m_Prompt = prompt;
                return this;
            }

            /// <summary>
            /// 追加一个选项。
            /// </summary>
            /// <param name="text">选项文本。</param>
            /// <param name="targetId">跳转目标 Id。</param>
            /// <param name="showCondition">显示条件，null 表示始终显示。</param>
            /// <param name="effects">选中后效果，可为 null。</param>
            public Builder AddChoice(
                string text,
                string targetId,
                DialogueCondition showCondition = null,
                IReadOnlyList<DialogueEffect> effects = null)
            {
                m_Choices.Add(new DialogueChoice(text, targetId, showCondition, effects));
                return this;
            }

            /// <summary>
            /// 追加一个已构造的选项。
            /// </summary>
            public Builder AddChoice(DialogueChoice choice)
            {
                if (choice == null)
                {
                    throw new ArgumentNullException(nameof(choice));
                }

                m_Choices.Add(choice);
                return this;
            }

            /// <summary>
            /// 构建选项节点。
            /// </summary>
            public ChoiceNode Build()
            {
                return new ChoiceNode(m_Id, m_Prompt, m_Choices);
            }
        }
    }
}
