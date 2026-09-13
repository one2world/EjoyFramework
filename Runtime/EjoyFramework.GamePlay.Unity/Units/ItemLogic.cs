//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 世界拾取物的生成参数。作为 <see cref="UnitData.Payload"/> 透传给 <see cref="ItemLogic"/>，
    /// 用于在 <see cref="ItemLogic.OnShow(object)"/> 中构建 <see cref="ItemPickup"/>。
    /// </summary>
    /// <remarks>
    /// 普通数据类（POCO），不含任何行为。<see cref="Contents"/> 为 null 或空时表示拾取物无内容物
    /// （<see cref="ItemLogic"/> 对此健壮）。
    /// </remarks>
    public sealed class ItemSpawnParams
    {
        /// <summary>
        /// 交互（拾取）半径。
        /// </summary>
        public float InteractRange;

        /// <summary>
        /// 是否自动拾取。
        /// </summary>
        public bool AutoCollect;

        /// <summary>
        /// 拾取物内容物列表（可为 null / 空）。
        /// </summary>
        public List<ItemContent> Contents;
    }

    /// <summary>
    /// 世界拾取物的 Unity 表现层封装。继承 <see cref="UnitLogic"/>，持有一个引擎无关的
    /// <see cref="ItemPickup"/> 内核，把"交互范围 + 内容物 + 一次性拾取"逻辑桥接到单位实体上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>非战斗单位</b>：世界物件没有战斗数值，沿用 <see cref="UnitModel"/> 的可选生命设计
    /// （不配置生命即不可摧毁），并在 <see cref="OnShow(object)"/> 中把
    /// <see cref="UnitModel.IsTargetable"/> 置为 false（物件不应被索敌选中）。
    /// </para>
    /// <para>
    /// <b>谁来驱动拾取</b>：本类<b>不</b>主动扫描所有单位去找拾取者。由<b>游戏层</b>（它知道玩家 / 拾取者是谁）
    /// 调用 <see cref="TryCollectBy(float, float, out IReadOnlyList{ItemContent})"/> 传入拾取者世界坐标；
    /// 本类负责范围判定与一次性拾取，成功后经 <see cref="UnitManager.Active"/> 回收实体。
    /// </para>
    /// <para>
    /// <b>背包边界</b>：拾取成功输出的内容物只是"要发放什么"的快照；把内容物真正入账到
    /// Inventory / Wallet 是<b>游戏层的职责</b>——游戏子类应重写 <see cref="OnCollected(IReadOnlyList{ItemContent})"/>
    /// 完成入账。本类与背包系统解耦。
    /// </para>
    /// <para>
    /// <b>坐标映射</b>：Unity 世界水平面 (X, Z) → <see cref="UnitModel"/> 的 2D 平面 (X, Y)。
    /// 拾取物位置取自 <see cref="UnitModel.PositionX"/> / <see cref="UnitModel.PositionY"/>。
    /// </para>
    /// </remarks>
    public class ItemLogic : UnitLogic
    {
        private ItemPickup m_Pickup;

        /// <summary>
        /// 本拾取物的引擎无关逻辑内核。在 <see cref="OnShow(object)"/> 中按 <see cref="ItemSpawnParams"/> 构建；
        /// 无参数时仍会建立一个空内容物、默认半径的拾取物。
        /// </summary>
        public ItemPickup Pickup
        {
            get { return m_Pickup; }
        }

        /// <summary>
        /// 实体显示。先调用基类建立 <see cref="UnitLogic.Model"/>，再把单位标记为非战斗目标，
        /// 并按 <see cref="ItemSpawnParams"/> 构建 <see cref="ItemPickup"/>。
        /// </summary>
        /// <param name="userData">应为 <see cref="UnitData"/>，其 <see cref="UnitData.Payload"/> 可为 <see cref="ItemSpawnParams"/>。</param>
        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            // 物件不是战斗目标，避免被索敌选中。
            UnitModel model = Model;
            if (model != null)
            {
                model.IsTargetable = false;
            }

            // 从 UnitData.Payload 读取拾取参数（健壮处理缺失 / 类型不符）。
            var spawnParams = (userData as UnitData)?.Payload as ItemSpawnParams;
            BuildPickup(spawnParams);
        }

        /// <summary>
        /// 实体轮询。先调用基类把表现层位置同步回模型。本类不在此自动扫描拾取者——
        /// 拾取由游戏层经 <see cref="TryCollectBy(float, float, out IReadOnlyList{ItemContent})"/> 驱动。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 实体隐藏。清空拾取内核引用后调用基类完成注销。
        /// </summary>
        /// <param name="isShutdown">是否是关闭实体管理器时触发。</param>
        /// <param name="userData">用户自定义数据。</param>
        protected override void OnHide(bool isShutdown, object userData)
        {
            m_Pickup = null;
            base.OnHide(isShutdown, userData);
        }

        /// <summary>
        /// 由游戏层驱动的一次性拾取尝试。检查拾取者是否在范围内（拾取物位于
        /// <see cref="UnitModel.PositionX"/> / <see cref="UnitModel.PositionY"/>），命中则拾取并发放内容物，
        /// 成功后经 <see cref="UnitManager.Active"/> 回收本实体。
        /// </summary>
        /// <remarks>
        /// 入参为拾取者的 Unity 世界水平坐标 (X, Z)，会与模型 2D 平面 (X, Y) 对齐比较。
        /// 把发放的内容物入账到背包仍是游戏层的职责，经重写 <see cref="OnCollected(IReadOnlyList{ItemContent})"/> 完成。
        /// </remarks>
        /// <param name="collectorWorldX">拾取者世界坐标 X。</param>
        /// <param name="collectorWorldZ">拾取者世界坐标 Z（映射为模型 Y）。</param>
        /// <param name="granted">命中时输出已发放内容物的只读快照；否则输出空列表。</param>
        /// <returns>本次是否成功拾取。</returns>
        public bool TryCollectBy(float collectorWorldX, float collectorWorldZ, out IReadOnlyList<ItemContent> granted)
        {
            granted = System.Array.Empty<ItemContent>();

            ItemPickup pickup = m_Pickup;
            UnitModel model = Model;
            if (pickup == null || model == null)
            {
                return false;
            }

            // 拾取者世界 (X, Z) → 模型平面 (X, Y)。
            if (!pickup.IsInRange(model.PositionX, model.PositionY, collectorWorldX, collectorWorldZ))
            {
                return false;
            }

            if (!pickup.TryCollect(out granted))
            {
                return false;
            }

            OnCollected(granted);

            // 拾取成功后回收实体（编辑器 / 未运行时 Active 可能为 null）。
            UnitManager.Active?.Despawn(UnitId);
            return true;
        }

        /// <summary>
        /// 拾取成功后的发放回调钩子。基类实现为空——把内容物真正入账到 Inventory / Wallet 是
        /// <b>游戏层的职责</b>，游戏子类应重写本方法完成入账。
        /// </summary>
        /// <param name="granted">本次发放的内容物只读快照。</param>
        protected virtual void OnCollected(IReadOnlyList<ItemContent> granted)
        {
        }

        /// <summary>
        /// 按生成参数构建 <see cref="ItemPickup"/>。参数缺失时退化为默认半径、无内容物的拾取物。
        /// </summary>
        /// <param name="spawnParams">拾取参数，可为 null。</param>
        private void BuildPickup(ItemSpawnParams spawnParams)
        {
            if (spawnParams != null)
            {
                m_Pickup = new ItemPickup(spawnParams.InteractRange, spawnParams.AutoCollect);

                List<ItemContent> contents = spawnParams.Contents;
                if (contents != null)
                {
                    for (int i = 0; i < contents.Count; i++)
                    {
                        ItemContent c = contents[i];
                        m_Pickup.AddContent(c.ItemId, c.Count);
                    }
                }
            }
            else
            {
                // 缺参时退化为可被拾取的空拾取物（半径 0：仅原地可拾取）。
                m_Pickup = new ItemPickup(0f);
            }
        }
    }
}
