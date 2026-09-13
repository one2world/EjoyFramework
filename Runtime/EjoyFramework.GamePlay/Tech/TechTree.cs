//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Tech
{
    /// <summary>
    /// 科技 / 升级树容器：管理一组 <see cref="TechNode"/> 的定义、运行期等级与解锁状态。
    /// 纯逻辑实现，不依赖引擎；线程不安全，预期在单线程游戏逻辑中使用。
    /// <para>
    /// 状态语义：所有前置节点均已解锁（等级 &gt;= 1）且自身等级为 0 时为
    /// <see cref="TechNodeStatus.Available"/>；任一前置未解锁时为 <see cref="TechNodeStatus.Locked"/>；
    /// 自身等级在 [1, MaxLevel) 区间内时为 <see cref="TechNodeStatus.Unlocked"/>；等级等于 MaxLevel 时为
    /// <see cref="TechNodeStatus.Maxed"/>。前置列表中引用的缺失节点 Id 视为永远无法满足，因而保持 Locked。
    /// </para>
    /// </summary>
    public sealed class TechTree
    {
        private readonly Dictionary<string, TechNode> m_Nodes =
            new Dictionary<string, TechNode>(StringComparer.Ordinal);

        private readonly Dictionary<string, int> m_Levels =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// 当某个节点等级发生变化时触发。参数为 (树, 节点 Id, 新等级)。
        /// </summary>
        public event Action<TechTree, string, int> OnLevelChanged;

        /// <summary>
        /// 当某个节点由 <see cref="TechNodeStatus.Locked"/> 转变为 <see cref="TechNodeStatus.Available"/> 时触发。
        /// 参数为 (树, 节点 Id)。对同一节点的同一次转变只会触发一次。
        /// </summary>
        public event Action<TechTree, string> OnNodeAvailable;

        /// <summary>
        /// 已定义的全部节点。
        /// </summary>
        public IEnumerable<TechNode> Nodes
        {
            get { return m_Nodes.Values; }
        }

        /// <summary>
        /// 定义（注册）一个节点。
        /// </summary>
        /// <param name="node">要定义的节点，不可为空。</param>
        /// <exception cref="ArgumentNullException">当 node 为空时抛出。</exception>
        /// <exception cref="ArgumentException">当存在相同 Id 的节点时抛出。</exception>
        public void Define(TechNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (m_Nodes.ContainsKey(node.Id))
            {
                throw new ArgumentException(
                    string.Format("科技节点 '{0}' 已定义，不允许重复。", node.Id), nameof(node));
            }

            m_Nodes.Add(node.Id, node);
            m_Levels.Add(node.Id, 0);

            // 若节点在定义之时就已处于 Available（无前置或前置已满足），视为“天生可用”而非
            // 由 Locked 转变而来：预先标记，避免后续级联把它当作新可用节点重复通知。
            if (GetStatus(node.Id) == TechNodeStatus.Available)
            {
                m_AvailableFired.Add(node.Id);
            }
        }

        /// <summary>
        /// 判断是否存在指定 Id 的节点。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>存在返回 true。</returns>
        public bool Has(string id)
        {
            return id != null && m_Nodes.ContainsKey(id);
        }

        /// <summary>
        /// 获取指定 Id 的节点定义。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>对应节点；不存在时返回 null。</returns>
        public TechNode GetNode(string id)
        {
            if (id == null)
            {
                return null;
            }

            TechNode node;
            m_Nodes.TryGetValue(id, out node);
            return node;
        }

        /// <summary>
        /// 获取指定节点的当前等级。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>当前等级；未解锁或节点不存在时返回 0。</returns>
        public int GetLevel(string id)
        {
            if (id == null)
            {
                return 0;
            }

            int level;
            return m_Levels.TryGetValue(id, out level) ? level : 0;
        }

        /// <summary>
        /// 获取指定节点的状态。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>节点状态。节点不存在时返回 <see cref="TechNodeStatus.Locked"/>。</returns>
        public TechNodeStatus GetStatus(string id)
        {
            TechNode node = GetNode(id);
            if (node == null)
            {
                return TechNodeStatus.Locked;
            }

            int level = GetLevel(id);
            if (level >= node.MaxLevel)
            {
                return TechNodeStatus.Maxed;
            }

            if (level >= 1)
            {
                return TechNodeStatus.Unlocked;
            }

            // 等级为 0：依据前置条件区分 Locked / Available。
            return ArePrerequisitesMet(id) ? TechNodeStatus.Available : TechNodeStatus.Locked;
        }

        /// <summary>
        /// 判断指定节点的所有前置节点是否均已解锁（等级 &gt;= 1）。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>所有前置均已解锁返回 true。节点不存在、或任一前置缺失/未解锁时返回 false。</returns>
        public bool ArePrerequisitesMet(string id)
        {
            TechNode node = GetNode(id);
            if (node == null)
            {
                return false;
            }

            IReadOnlyList<string> prerequisites = node.Prerequisites;
            for (int i = 0; i < prerequisites.Count; i++)
            {
                // 缺失的前置 Id 在 IsUnlocked 中等级视为 0，自然判为未满足，因而保持 Locked。
                if (!IsUnlocked(prerequisites[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 判断指定节点当前是否可以解锁 / 升级。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>前置已满足且当前等级小于 MaxLevel 时返回 true。</returns>
        public bool CanUnlock(string id)
        {
            TechNode node = GetNode(id);
            if (node == null)
            {
                return false;
            }

            return GetLevel(id) < node.MaxLevel && ArePrerequisitesMet(id);
        }

        /// <summary>
        /// 解锁 / 升级指定节点一级。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>
        /// 当 <see cref="CanUnlock(string)"/> 为 true 时执行：等级 +1，触发 <see cref="OnLevelChanged"/>；
        /// 若本次为首次解锁（0 -&gt; 1），则重新评估其它 <see cref="TechNodeStatus.Locked"/> 节点，
        /// 对其中前置已全部满足的节点触发 <see cref="OnNodeAvailable"/>（支持链式级联）；返回 true。
        /// 否则不产生任何副作用并返回 false。
        /// </returns>
        public bool Unlock(string id)
        {
            if (!CanUnlock(id))
            {
                return false;
            }

            // CanUnlock 已确保节点存在。
            int previousLevel = GetLevel(id);
            int newLevel = previousLevel + 1;
            m_Levels[id] = newLevel;

            Action<TechTree, string, int> levelChanged = OnLevelChanged;
            if (levelChanged != null)
            {
                levelChanged(this, id, newLevel);
            }

            // 仅首次解锁（0 -> 1）会让本节点 IsUnlocked，从而可能解锁依赖它的节点。
            if (previousLevel == 0)
            {
                CascadeNewlyAvailable();
            }

            return true;
        }

        /// <summary>
        /// 判断节点是否已解锁（等级 &gt;= 1）。缺失节点视为未解锁。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>等级 &gt;= 1 返回 true。</returns>
        public bool IsUnlocked(string id)
        {
            return GetLevel(id) >= 1;
        }

        /// <summary>
        /// 判断节点是否已满级（等级 == MaxLevel）。缺失节点返回 false。
        /// </summary>
        /// <param name="id">节点标识。</param>
        /// <returns>等级等于 MaxLevel 返回 true。</returns>
        public bool IsMaxed(string id)
        {
            TechNode node = GetNode(id);
            return node != null && GetLevel(id) == node.MaxLevel;
        }

        /// <summary>
        /// 重新扫描全部节点，对所有由 Locked 转为 Available 的节点触发一次 <see cref="OnNodeAvailable"/>。
        /// 使用 <see cref="m_AvailableFired"/> 去重，确保同一节点的同一次 Locked-&gt;Available 转变只通知一次。
        /// 循环至稳定，以兼容外部回调在通知期间进一步解锁节点而产生的链式级联。
        /// </summary>
        private void CascadeNewlyAvailable()
        {
            if (OnNodeAvailable == null)
            {
                return;
            }

            bool changed = true;
            while (changed)
            {
                changed = false;

                // 快照键集合，避免在遍历期间因外部回调改动字典（Mono/IL2CPP 风险）。
                List<string> ids = new List<string>(m_Nodes.Keys);
                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];

                    // 已触发过的节点直接跳过，保证不重复通知。
                    if (m_AvailableFired.Contains(id))
                    {
                        continue;
                    }

                    // 仅在节点确实处于 Available（等级为 0 且前置已满足）时通知。
                    if (GetStatus(id) != TechNodeStatus.Available)
                    {
                        continue;
                    }

                    m_AvailableFired.Add(id);

                    Action<TechTree, string> available = OnNodeAvailable;
                    if (available != null)
                    {
                        available(this, id);
                    }

                    // 触发可用本身不改变等级；但外部回调可能在此期间解锁节点，
                    // 故标记 changed 以再扫描一轮，捕获新产生的可用节点。
                    changed = true;
                }
            }
        }

        // 记录已经通知过 Locked->Available 的节点 Id，用于去重。
        private readonly HashSet<string> m_AvailableFired = new HashSet<string>(StringComparer.Ordinal);
    }
}
