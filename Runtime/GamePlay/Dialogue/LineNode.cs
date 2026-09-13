//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 台词/旁白节点：承载一句说话内容。<see cref="NextId"/> 为 null/空表示该句之后对话结束。
    /// 进入节点时按顺序应用 <see cref="OnEnterEffects"/>。<see cref="VoiceId"/> 与 <see cref="Portrait"/>
    /// 为不透明元数据，由游戏侧自行解析（音频/立绘等）。不可变值对象。
    /// </summary>
    public sealed class LineNode : DialogueNode
    {
        private static readonly IReadOnlyList<DialogueEffect> s_EmptyEffects = new DialogueEffect[0];

        private readonly string m_Speaker;
        private readonly string m_Text;
        private readonly string m_NextId;
        private readonly string m_VoiceId;
        private readonly string m_Portrait;
        private readonly IReadOnlyList<DialogueEffect> m_OnEnterEffects;

        /// <summary>
        /// 构造台词节点。
        /// </summary>
        /// <param name="id">节点 Id，不可为空。</param>
        /// <param name="speaker">说话人，可为空（旁白）。</param>
        /// <param name="text">台词文本。</param>
        /// <param name="nextId">下一节点 Id；null/空表示结束。</param>
        /// <param name="voiceId">语音元数据，可选。</param>
        /// <param name="portrait">立绘元数据，可选。</param>
        /// <param name="onEnterEffects">进入时应用的效果，可为 null。</param>
        public LineNode(
            string id,
            string speaker,
            string text,
            string nextId,
            string voiceId = null,
            string portrait = null,
            IReadOnlyList<DialogueEffect> onEnterEffects = null)
            : base(id)
        {
            m_Speaker = speaker;
            m_Text = text;
            m_NextId = nextId;
            m_VoiceId = voiceId;
            m_Portrait = portrait;
            m_OnEnterEffects = CopyEffects(onEnterEffects);
        }

        /// <summary>
        /// 说话人。旁白时可为 null/空。
        /// </summary>
        public string Speaker
        {
            get { return m_Speaker; }
        }

        /// <summary>
        /// 台词文本。
        /// </summary>
        public string Text
        {
            get { return m_Text; }
        }

        /// <summary>
        /// 下一节点 Id。null/空表示对话在此结束。
        /// </summary>
        public string NextId
        {
            get { return m_NextId; }
        }

        /// <summary>
        /// 语音元数据（不透明）。
        /// </summary>
        public string VoiceId
        {
            get { return m_VoiceId; }
        }

        /// <summary>
        /// 立绘元数据（不透明）。
        /// </summary>
        public string Portrait
        {
            get { return m_Portrait; }
        }

        /// <summary>
        /// 节点被进入时应用的效果列表（只读，非 null）。
        /// </summary>
        public IReadOnlyList<DialogueEffect> OnEnterEffects
        {
            get { return m_OnEnterEffects; }
        }

        /// <summary>
        /// 创建一个台词节点构造器，便于链式配置说话人、元数据与进入效果。
        /// </summary>
        /// <param name="id">节点 Id。</param>
        /// <param name="text">台词文本。</param>
        /// <returns>构造器。</returns>
        public static Builder Create(string id, string text)
        {
            return new Builder(id, text);
        }

        // 复制为不可变快照，杜绝外部后续改动影响节点。
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

        /// <summary>
        /// <see cref="LineNode"/> 的链式构造器。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private readonly string m_Text;
            private string m_Speaker;
            private string m_NextId;
            private string m_VoiceId;
            private string m_Portrait;
            private List<DialogueEffect> m_Effects;

            internal Builder(string id, string text)
            {
                m_Id = id;
                m_Text = text;
            }

            /// <summary>
            /// 设置说话人。
            /// </summary>
            public Builder Speaker(string speaker)
            {
                m_Speaker = speaker;
                return this;
            }

            /// <summary>
            /// 设置下一节点 Id。
            /// </summary>
            public Builder Next(string nextId)
            {
                m_NextId = nextId;
                return this;
            }

            /// <summary>
            /// 设置语音元数据。
            /// </summary>
            public Builder Voice(string voiceId)
            {
                m_VoiceId = voiceId;
                return this;
            }

            /// <summary>
            /// 设置立绘元数据。
            /// </summary>
            public Builder Portrait(string portrait)
            {
                m_Portrait = portrait;
                return this;
            }

            /// <summary>
            /// 追加一个进入时效果。
            /// </summary>
            /// <param name="effect">效果，不可为 null。</param>
            public Builder OnEnter(DialogueEffect effect)
            {
                if (effect == null)
                {
                    throw new ArgumentNullException(nameof(effect));
                }

                m_Effects ??= new List<DialogueEffect>();
                m_Effects.Add(effect);
                return this;
            }

            /// <summary>
            /// 构建台词节点。
            /// </summary>
            public LineNode Build()
            {
                return new LineNode(m_Id, m_Speaker, m_Text, m_NextId, m_VoiceId, m_Portrait, m_Effects);
            }
        }
    }
}
