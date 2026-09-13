//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Factions;
using EjoyFramework.Core.Unity;
using UnityEngine;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 机关 / 陷阱（机关）的生成载荷。作为 <see cref="UnitData.Payload"/> 透传给 <see cref="MechanismLogic"/>，
    /// 用于在 <see cref="MechanismLogic.OnShow(object)"/> 中构建引擎无关的 <see cref="MechanismTrigger"/>。
    /// </summary>
    /// <remarks>
    /// 普通数据类（POCO），不含行为；缺省值已为安全默认（无害的零配置）。
    /// </remarks>
    public sealed class MechanismSpawnParams
    {
        /// <summary>
        /// 触发模式。
        /// </summary>
        public TriggerMode Mode;

        /// <summary>
        /// 两次发火之间的冷却（秒，&gt;= 0）。
        /// </summary>
        public float Cooldown;

        /// <summary>
        /// 邻近触发半径（世界单位）。仅 <see cref="TriggerMode.Proximity"/> 使用。
        /// </summary>
        public float Radius;

        /// <summary>
        /// 定时发火周期（秒，&gt; 0）。仅 <see cref="TriggerMode.Timer"/> 使用。
        /// </summary>
        public float Interval;

        /// <summary>
        /// 最大触发次数（<c>&lt;= 0</c> 无限）。
        /// </summary>
        public int MaxTriggers;

        /// <summary>
        /// 发火造成的效果伤害（供游戏子类在 <see cref="MechanismLogic.OnFire"/> 中使用）。
        /// </summary>
        public float EffectDamage;

        /// <summary>
        /// 目标阵营基准 Id：用于 <see cref="TriggerMode.Proximity"/> 的“合格目标”判定。
        /// 范围内单位的阵营若与该 Id <b>敌对</b>（按世界阵营关系表解析），则视为合格目标。
        /// </summary>
        public int TargetFactionId;
    }

    /// <summary>
    /// 机关 / 陷阱的 Unity 逻辑组件。继承 <see cref="UnitLogic"/>，把引擎无关的
    /// <see cref="MechanismTrigger"/> 与 Unity 生命周期桥接起来：邻近模式下每帧用世界空间查询计算
    /// “是否有合格目标在范围内”，再推进触发器；发火时调用可重写的 <see cref="OnFire"/> 钩子执行实际效果。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>机关是静止单位</b>：通常不参与移动，常常<b>无生命</b>（依赖 <see cref="UnitModel"/> 的可选生命设计保持不可摧毁）。
    /// 是否可被摧毁由 <see cref="UnitData.MaxHealth"/> 决定，与本类的触发逻辑正交。
    /// </para>
    /// <para>
    /// <b>查询边界</b>：邻近判定通过
    /// <see cref="UnitWorld.QueryNearby(float, float, float, List{UnitModel})"/>
    /// 取半径内单位（结果取决于 <see cref="UnitManager"/> 每帧的 <c>SyncSpatial</c>），
    /// 再用世界 <see cref="UnitWorld.Relations"/> 的 <see cref="FactionRelations.AreEnemies(int, int)"/>
    /// 判定是否为针对 <see cref="MechanismSpawnParams.TargetFactionId"/> 的敌对阵营单位。
    /// <see cref="UnitManager.Active"/> 在编辑器 / 未运行时可能为 null，本类对此健壮（无世界时视作无目标在范围内）。
    /// </para>
    /// <para>
    /// <b>发火效果</b>保持为可重写的 <see cref="OnFire"/> 钩子：默认仅记录日志，游戏子类 / Inspector 接线真正的
    /// VFX / 伤害逻辑。本类不强制任何具体效果。
    /// </para>
    /// </remarks>
    [AddComponentMenu("EjoyFramework/GamePlay/Mechanism Logic")]
    public class MechanismLogic : UnitLogic
    {
        private MechanismTrigger m_Trigger;
        private MechanismSpawnParams m_Params;

        // 复用的近邻查询缓冲，避免每帧分配。
        private readonly List<UnitModel> m_QueryBuffer = new List<UnitModel>();

        /// <summary>
        /// 本机关的引擎无关触发内核。在 <see cref="OnShow(object)"/> 中按 <see cref="MechanismSpawnParams"/> 构建；
        /// 缺省参数时退化为一个无害的默认触发器。
        /// </summary>
        public MechanismTrigger Trigger
        {
            get { return m_Trigger; }
        }

        /// <summary>
        /// 本机关的生成参数（可能为 null，当未提供 <see cref="MechanismSpawnParams"/> 时）。
        /// </summary>
        public MechanismSpawnParams Params
        {
            get { return m_Params; }
        }

        /// <summary>
        /// 实体显示。先调用基类（建立 <see cref="UnitLogic.Model"/> 并自注册进世界），
        /// 再从 <see cref="UnitData.Payload"/> 读取 <see cref="MechanismSpawnParams"/> 构建触发器并订阅其发火事件。
        /// 对缺失 / 非法参数保持健壮（退化为默认触发器）。
        /// </summary>
        /// <param name="userData">应为 <see cref="UnitData"/>，其 <see cref="UnitData.Payload"/> 应为 <see cref="MechanismSpawnParams"/>。</param>
        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            m_Params = (userData as UnitData)?.Payload as MechanismSpawnParams;

            // 解绑可能残留的旧订阅（对象池复用时），再重建触发器。
            UnsubscribeTrigger();

            TriggerMode mode = m_Params != null ? m_Params.Mode : TriggerMode.Manual;
            m_Trigger = new MechanismTrigger(mode);

            if (m_Params != null)
            {
                m_Trigger.Cooldown = m_Params.Cooldown;
                m_Trigger.Radius = m_Params.Radius;
                // Interval 仅在为正时有意义；MechanismTrigger 对非正值会回退为 1。
                if (m_Params.Interval > 0f)
                {
                    m_Trigger.Interval = m_Params.Interval;
                }
                m_Trigger.MaxTriggers = m_Params.MaxTriggers;
            }

            m_Trigger.OnFired += HandleTriggerFired;
        }

        /// <summary>
        /// 实体轮询。先调用基类（把 Transform 世界位置同步回模型）。随后：
        /// 邻近模式计算 <c>targetInRange</c> 并以此推进触发器；定时 / 手动模式仅推进冷却 / 周期。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            MechanismTrigger trigger = m_Trigger;
            if (trigger == null)
            {
                return;
            }

            if (trigger.Mode == TriggerMode.Proximity)
            {
                bool inRange = QualifiedTargetInRange(trigger.Radius);
                trigger.Tick(Time.deltaTime, inRange);
            }
            else
            {
                trigger.Tick(Time.deltaTime);
            }
        }

        /// <summary>
        /// 实体隐藏。解绑触发器发火事件后调用基类（从世界注销）。保留触发器实例以便下次复用 / 检视。
        /// </summary>
        /// <param name="isShutdown">是否是关闭实体管理器时触发。</param>
        /// <param name="userData">用户自定义数据。</param>
        protected override void OnHide(bool isShutdown, object userData)
        {
            UnsubscribeTrigger();
            base.OnHide(isShutdown, userData);
        }

        /// <summary>
        /// 实体回收（实例销毁前）。丢弃触发器与参数引用后调用基类。
        /// </summary>
        protected override void OnRecycle()
        {
            UnsubscribeTrigger();
            m_Trigger = null;
            m_Params = null;
            m_QueryBuffer.Clear();

            base.OnRecycle();
        }

        /// <summary>
        /// 发火效果钩子。<b>默认仅记录日志</b>，游戏子类应重写此方法以执行真正的效果
        /// （播放 VFX、对范围内敌人造成 <see cref="MechanismSpawnParams.EffectDamage"/> 伤害等）。
        /// 由 <see cref="MechanismTrigger.OnFired"/> 在每次发火时触发。
        /// </summary>
        protected virtual void OnFire()
        {
            Log.Info("[MechanismLogic] Mechanism {0} fired (count={1}); override OnFire to apply the effect.",
                UnitId, m_Trigger != null ? m_Trigger.TriggerCount : 0);
        }

        // ---------------------------------------------------------------
        // 内部
        // ---------------------------------------------------------------

        /// <summary>
        /// 桥接：把 <see cref="MechanismTrigger.OnFired"/> 转发到可重写的 <see cref="OnFire"/>。
        /// </summary>
        private void HandleTriggerFired(MechanismTrigger trigger)
        {
            OnFire();
        }

        /// <summary>
        /// 解绑触发器发火订阅（幂等）。
        /// </summary>
        private void UnsubscribeTrigger()
        {
            if (m_Trigger != null)
            {
                m_Trigger.OnFired -= HandleTriggerFired;
            }
        }

        /// <summary>
        /// 查询边界：用世界空间索引取半径内单位，判断是否存在一个“合格目标”
        /// （阵营与 <see cref="MechanismSpawnParams.TargetFactionId"/> 敌对、可被选取、且非机关自身）。
        /// 无世界 / 无模型时返回 false。
        /// </summary>
        /// <param name="radius">查询半径。</param>
        /// <returns>是否有合格目标在范围内。</returns>
        private bool QualifiedTargetInRange(float radius)
        {
            UnitManager manager = UnitManager.Active;
            UnitModel self = Model;
            if (manager == null || manager.World == null || self == null || radius <= 0f)
            {
                return false;
            }

            UnitWorld world = manager.World;
            world.QueryNearby(self.PositionX, self.PositionY, radius, m_QueryBuffer);

            int targetFaction = m_Params != null ? m_Params.TargetFactionId : self.FactionId;
            FactionRelations relations = world.Relations;

            for (int i = 0; i < m_QueryBuffer.Count; i++)
            {
                UnitModel candidate = m_QueryBuffer[i];
                if (candidate == null || candidate.Id == self.Id)
                {
                    continue;
                }

                if (!candidate.IsTargetable || !candidate.IsAlive)
                {
                    continue;
                }

                // 合格目标：与基准阵营敌对。relations 由世界保证非 null。
                if (relations != null && relations.AreEnemies(targetFaction, candidate.FactionId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
