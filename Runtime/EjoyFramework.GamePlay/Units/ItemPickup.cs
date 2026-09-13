//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 世界拾取物的一条内容物（道具 Id + 数量）。引擎无关的不可变值类型。
    /// </summary>
    public readonly struct ItemContent
    {
        private readonly string m_ItemId;
        private readonly int m_Count;

        /// <summary>
        /// 构造一条内容物。
        /// </summary>
        /// <param name="itemId">道具标识（业务定义，框架不解释其含义）。</param>
        /// <param name="count">数量。</param>
        public ItemContent(string itemId, int count)
        {
            m_ItemId = itemId;
            m_Count = count;
        }

        /// <summary>
        /// 道具标识。
        /// </summary>
        public string ItemId
        {
            get { return m_ItemId; }
        }

        /// <summary>
        /// 道具数量。
        /// </summary>
        public int Count
        {
            get { return m_Count; }
        }
    }

    /// <summary>
    /// 世界拾取物（掉落 / 战利品）的引擎无关逻辑内核：交互范围 + 内容物 + 一次性拾取。
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>
    /// <b>引擎无关</b>：仅依赖 <c>System.*</c>，不引用任何 UnityEngine 类型，因此可单元测试，
    /// 也可由网络层在服务端模拟。坐标使用 2D 平面 (X, Y)，由 Unity 层把世界 (X, Z) 映射进来。
    /// </item>
    /// <item>
    /// <b>解耦背包</b>：本类只负责"是否在范围内 / 一次性发放内容物"，<b>不</b>依赖任何背包 / 钱包系统；
    /// <see cref="TryCollect(out IReadOnlyList{ItemContent})"/> 输出已发放内容物的快照，
    /// 由<b>游戏层</b>负责把它们入账到 Inventory / Wallet。
    /// </item>
    /// <item>
    /// <b>单线程</b>：本类型不是线程安全的，所有访问应发生在同一逻辑线程。
    /// 范围检测（<see cref="SqrDistanceTo"/> / <see cref="IsInRange"/>）不产生堆分配。
    /// </item>
    /// </list>
    /// </remarks>
    public sealed class ItemPickup
    {
        private readonly List<ItemContent> m_Contents;
        private readonly bool m_AutoCollect;

        private float m_InteractRange;
        private bool m_IsCollected;

        /// <summary>
        /// 构造一个拾取物。
        /// </summary>
        /// <param name="interactRange">交互（拾取）半径。</param>
        /// <param name="autoCollect">是否自动拾取（进入范围即可被游戏驱动拾取），缺省 true。</param>
        public ItemPickup(float interactRange, bool autoCollect = true)
        {
            m_InteractRange = interactRange;
            m_AutoCollect = autoCollect;
            m_Contents = new List<ItemContent>();
            m_IsCollected = false;
        }

        /// <summary>
        /// 交互（拾取）半径。可在运行时调整。
        /// </summary>
        public float InteractRange
        {
            get { return m_InteractRange; }
            set { m_InteractRange = value; }
        }

        /// <summary>
        /// 是否自动拾取。该标志仅描述拾取物的意图，是否真正拾取由游戏层驱动。
        /// </summary>
        public bool AutoCollect
        {
            get { return m_AutoCollect; }
        }

        /// <summary>
        /// 是否已被拾取。一旦为 true，再次 <see cref="TryCollect(out IReadOnlyList{ItemContent})"/> 即为幂等空操作，
        /// 直到调用 <see cref="Reset"/>。
        /// </summary>
        public bool IsCollected
        {
            get { return m_IsCollected; }
        }

        /// <summary>
        /// 本拾取物的内容物快照（只读视图）。随 <see cref="AddContent(string, int)"/> 累积。
        /// </summary>
        public IReadOnlyList<ItemContent> Contents
        {
            get { return m_Contents; }
        }

        /// <summary>
        /// 追加一条内容物。同一 <paramref name="itemId"/> 多次追加会<b>累加</b>到既有条目的数量上
        /// （而非新增重复条目）。
        /// </summary>
        /// <param name="itemId">道具标识。</param>
        /// <param name="count">数量增量。</param>
        public void AddContent(string itemId, int count)
        {
            for (int i = 0; i < m_Contents.Count; i++)
            {
                ItemContent existing = m_Contents[i];
                if (existing.ItemId == itemId)
                {
                    m_Contents[i] = new ItemContent(itemId, existing.Count + count);
                    return;
                }
            }

            m_Contents.Add(new ItemContent(itemId, count));
        }

        /// <summary>
        /// 计算拾取物与拾取者在 2D 平面上的平方距离（避免开方，无堆分配）。
        /// </summary>
        /// <param name="itemX">拾取物 X。</param>
        /// <param name="itemY">拾取物 Y。</param>
        /// <param name="collectorX">拾取者 X。</param>
        /// <param name="collectorY">拾取者 Y。</param>
        /// <returns>平方欧氏距离。</returns>
        public float SqrDistanceTo(float itemX, float itemY, float collectorX, float collectorY)
        {
            float dx = collectorX - itemX;
            float dy = collectorY - itemY;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// 判断拾取者是否在交互范围内：欧氏距离 &lt;= <see cref="InteractRange"/>（边界包含）。
        /// 经平方比较实现，无开方、无堆分配。
        /// </summary>
        /// <param name="itemX">拾取物 X。</param>
        /// <param name="itemY">拾取物 Y。</param>
        /// <param name="collectorX">拾取者 X。</param>
        /// <param name="collectorY">拾取者 Y。</param>
        /// <returns>是否在范围内。</returns>
        public bool IsInRange(float itemX, float itemY, float collectorX, float collectorY)
        {
            float sqrDist = SqrDistanceTo(itemX, itemY, collectorX, collectorY);
            return sqrDist <= m_InteractRange * m_InteractRange;
        }

        /// <summary>
        /// 一次性拾取：若<b>已</b>拾取则输出空列表并返回 false（幂等）；否则标记为已拾取、触发
        /// <see cref="OnCollected"/>，输出当前内容物快照并返回 true。
        /// </summary>
        /// <remarks>
        /// 本核心<b>不</b>依赖背包：<paramref name="granted"/> 是要发放的内容物快照，
        /// 由游戏层负责把它们入账到 Inventory / Wallet。
        /// </remarks>
        /// <param name="granted">命中时输出已发放内容物的只读快照；未命中时输出空列表。</param>
        /// <returns>本次是否成功拾取。</returns>
        public bool TryCollect(out IReadOnlyList<ItemContent> granted)
        {
            if (m_IsCollected)
            {
                granted = Array.Empty<ItemContent>();
                return false;
            }

            m_IsCollected = true;

            Action<ItemPickup> handler = OnCollected;
            if (handler != null)
            {
                handler(this);
            }

            granted = m_Contents;
            return true;
        }

        /// <summary>
        /// 重置拾取状态（对象池复用时使用）：解除"已拾取"标记，使其可再次被拾取。
        /// <b>保留</b>内容物与 <see cref="InteractRange"/>；如需清空内容物请由调用方另行处理。
        /// </summary>
        public void Reset()
        {
            m_IsCollected = false;
        }

        /// <summary>
        /// 被成功拾取时触发一次：(拾取物自身)。同一次拾取只触发一次（<see cref="Reset"/> 后可再次触发）。
        /// </summary>
        public event Action<ItemPickup> OnCollected;
    }
}
