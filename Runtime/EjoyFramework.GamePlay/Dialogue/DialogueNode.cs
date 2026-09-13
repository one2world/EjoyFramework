//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 对话节点抽象基类。每个节点拥有图内唯一的 <see cref="Id"/>。
    /// 具体子类：<see cref="LineNode"/>（台词）、<see cref="ChoiceNode"/>（选项）、<see cref="BranchNode"/>（旗标自动路由）。
    /// </summary>
    public abstract class DialogueNode
    {
        private readonly string m_Id;

        /// <summary>
        /// 构造节点。
        /// </summary>
        /// <param name="id">节点 Id，不可为空。</param>
        /// <exception cref="ArgumentException">id 为空。</exception>
        protected DialogueNode(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("节点 Id 不能为空。", nameof(id));
            }

            m_Id = id;
        }

        /// <summary>
        /// 节点 Id，在所属图内唯一。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }
    }
}
