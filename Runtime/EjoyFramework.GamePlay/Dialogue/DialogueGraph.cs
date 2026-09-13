//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 一张分支对话图：节点集合 + 起始节点 Id。节点以唯一 Id 索引。
    /// 仅描述结构，运行时游标由 <see cref="DialogueRunner"/> 维护，因此同一张图可被多个运行器共享。
    /// 纯逻辑、与引擎无关。
    /// </summary>
    public sealed class DialogueGraph
    {
        private readonly string m_Id;
        private readonly string m_StartNodeId;
        private readonly Dictionary<string, DialogueNode> m_Nodes =
            new Dictionary<string, DialogueNode>(StringComparer.Ordinal);

        /// <summary>
        /// 构造对话图。
        /// </summary>
        /// <param name="id">图 Id，不可为空。</param>
        /// <param name="startNodeId">起始节点 Id，不可为空。</param>
        /// <exception cref="ArgumentException">id 或 startNodeId 为空。</exception>
        public DialogueGraph(string id, string startNodeId)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("图 Id 不能为空。", nameof(id));
            }

            if (string.IsNullOrEmpty(startNodeId))
            {
                throw new ArgumentException("起始节点 Id 不能为空。", nameof(startNodeId));
            }

            m_Id = id;
            m_StartNodeId = startNodeId;
        }

        /// <summary>
        /// 图 Id。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 起始节点 Id。
        /// </summary>
        public string StartNodeId
        {
            get { return m_StartNodeId; }
        }

        /// <summary>
        /// 已注册节点数量。
        /// </summary>
        public int NodeCount
        {
            get { return m_Nodes.Count; }
        }

        /// <summary>
        /// 添加一个节点（链式返回自身）。
        /// </summary>
        /// <param name="node">节点，不可为 null。</param>
        /// <returns>图自身，便于链式调用。</returns>
        /// <exception cref="ArgumentNullException">node 为 null。</exception>
        /// <exception cref="ArgumentException">节点 Id 已存在。</exception>
        public DialogueGraph AddNode(DialogueNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (m_Nodes.ContainsKey(node.Id))
            {
                throw new ArgumentException($"节点 Id 已存在：{node.Id}", nameof(node));
            }

            m_Nodes.Add(node.Id, node);
            return this;
        }

        /// <summary>
        /// 获取节点，不存在返回 null。
        /// </summary>
        /// <param name="id">节点 Id。</param>
        /// <returns>节点或 null。</returns>
        public DialogueNode GetNode(string id)
        {
            if (id != null && m_Nodes.TryGetValue(id, out DialogueNode node))
            {
                return node;
            }

            return null;
        }

        /// <summary>
        /// 尝试获取节点。
        /// </summary>
        /// <param name="id">节点 Id。</param>
        /// <param name="node">输出节点。</param>
        /// <returns>存在返回 true。</returns>
        public bool TryGetNode(string id, out DialogueNode node)
        {
            if (id != null)
            {
                return m_Nodes.TryGetValue(id, out node);
            }

            node = null;
            return false;
        }
    }
}
