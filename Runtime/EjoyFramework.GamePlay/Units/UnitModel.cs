//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Attributes;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 3D 游戏单位的引擎无关逻辑内核。实现 <see cref="ITargetableUnit"/>，
    /// 可组合属性系统（<see cref="AttributeSet"/>）与可推进行为（<see cref="ITickable"/>，如战斗层的技能系统）。
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>
    /// <b>所有战斗能力均为可选</b>。一个单位可以没有生命、没有属性、没有技能
    /// （例如世界物件或不可摧毁的机关）。基类不强制任何战斗状态。
    /// </item>
    /// <item>
    /// <b>可选生命（关键设计）</b>：在调用 <see cref="ConfigureHealth(float, float)"/> 之前，
    /// 单位被视为<b>不可摧毁</b>：<see cref="IsDamageable"/> 为 false、<see cref="IsAlive"/> 恒为 true、
    /// <see cref="TakeDamage(float, int)"/> 与 <see cref="Kill(int)"/> 均为空操作；
    /// 此时 <see cref="Health"/> 返回哨兵值 1f，<see cref="MaxHealth"/> 返回 0f。
    /// 这让物件/机关能以零战斗状态直接成为 UnitModel。
    /// </item>
    /// <item>
    /// 引擎无关：不引用任何 UnityEngine 类型。坐标使用 2D 战斗平面 (X, Y)，
    /// Unity 层负责把世界水平面 (XZ) 映射到 (X, Y)。
    /// </item>
    /// <item>
    /// 与 MonoBehaviour 解耦，因此可单元测试，也可由网络层在服务端模拟。
    /// </item>
    /// <item>
    /// <b>单线程</b>：本类型不是线程安全的，所有访问应发生在同一逻辑线程。
    /// 热路径（<see cref="Tick(float)"/>、<see cref="TakeDamage(float, int)"/>）不产生堆分配。
    /// </item>
    /// </list>
    /// </remarks>
    public sealed class UnitModel : ITargetableUnit
    {
        /// <summary>
        /// 不可摧毁单位（未配置生命）对外暴露的 <see cref="Health"/> 哨兵值。
        /// </summary>
        public const float IndestructibleHealthSentinel = 1f;

        private readonly int m_Id;

        private int m_FactionId;
        private float m_PositionX;
        private float m_PositionY;
        private float m_Progress;
        private float m_Threat;
        private int m_Priority;
        private bool m_IsTargetable;

        private bool m_IsDamageable;
        private float m_MaxHealth;
        private float m_Health;
        private bool m_DeathDispatched;

        private AttributeSet m_Attributes;
        private ITickable m_Behavior;

        /// <summary>
        /// 构造一个单位。默认不可摧毁（未配置生命）、可被选取（<see cref="IsTargetable"/> 为 true）。
        /// </summary>
        /// <param name="id">单位唯一标识（不可变）。</param>
        /// <param name="factionId">初始阵营标识。</param>
        public UnitModel(int id, int factionId)
        {
            m_Id = id;
            m_FactionId = factionId;
            m_IsTargetable = true;
        }

        // ---------------------------------------------------------------
        // 身份与空间
        // ---------------------------------------------------------------

        /// <summary>
        /// 单位唯一标识（构造后不可变，<see cref="Reset(int)"/> 也会保留）。
        /// </summary>
        public int Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 阵营标识，用于敌我判定。
        /// </summary>
        public int FactionId
        {
            get { return m_FactionId; }
            set { m_FactionId = value; }
        }

        /// <summary>
        /// 2D 战斗平面上的 X 坐标。
        /// </summary>
        public float PositionX
        {
            get { return m_PositionX; }
            set { m_PositionX = value; }
        }

        /// <summary>
        /// 2D 战斗平面上的 Y 坐标。
        /// </summary>
        public float PositionY
        {
            get { return m_PositionY; }
            set { m_PositionY = value; }
        }

        /// <summary>
        /// 一次性设置坐标。
        /// </summary>
        /// <param name="x">X 坐标。</param>
        /// <param name="y">Y 坐标。</param>
        public void SetPosition(float x, float y)
        {
            m_PositionX = x;
            m_PositionY = y;
        }

        /// <summary>
        /// 进度值，例如塔防中沿路径推进的距离，用于排序。
        /// </summary>
        public float Progress
        {
            get { return m_Progress; }
            set { m_Progress = value; }
        }

        /// <summary>
        /// 仇恨/威胁值，供索敌排序使用。
        /// </summary>
        public float Threat
        {
            get { return m_Threat; }
            set { m_Threat = value; }
        }

        /// <summary>
        /// 选取优先级，数值越大越优先。
        /// </summary>
        public int Priority
        {
            get { return m_Priority; }
            set { m_Priority = value; }
        }

        /// <summary>
        /// 单位当前是否可被选取为目标，默认 true。
        /// </summary>
        public bool IsTargetable
        {
            get { return m_IsTargetable; }
            set { m_IsTargetable = value; }
        }

        // ---------------------------------------------------------------
        // 可选生命
        // ---------------------------------------------------------------

        /// <summary>
        /// 单位是否可被伤害。仅在调用过 <see cref="ConfigureHealth(float, float)"/> 后为 true。
        /// </summary>
        public bool IsDamageable
        {
            get { return m_IsDamageable; }
        }

        /// <summary>
        /// 当前生命值。未配置生命（不可摧毁）时返回哨兵值
        /// <see cref="IndestructibleHealthSentinel"/>（即 1f）。
        /// </summary>
        public float Health
        {
            get { return m_IsDamageable ? m_Health : IndestructibleHealthSentinel; }
        }

        /// <summary>
        /// 最大生命值。未配置生命（不可摧毁）时返回 0f。
        /// </summary>
        public float MaxHealth
        {
            get { return m_IsDamageable ? m_MaxHealth : 0f; }
        }

        /// <summary>
        /// 单位是否存活。不可摧毁单位（物件/机关）恒为 true；
        /// 可被伤害单位则为 <c>Health &gt; 0</c>。
        /// </summary>
        public bool IsAlive
        {
            get { return !m_IsDamageable || m_Health > 0f; }
        }

        /// <summary>
        /// 配置生命，使单位变为可被伤害。
        /// </summary>
        /// <param name="maxHealth">最大生命值（小于 0 时钳制为 0）。</param>
        /// <param name="currentHealth">初始当前生命；传入负值表示按满血（等于 maxHealth）。
        /// 否则钳制到 [0, maxHealth]。</param>
        public void ConfigureHealth(float maxHealth, float currentHealth = -1f)
        {
            if (maxHealth < 0f)
            {
                maxHealth = 0f;
            }

            m_IsDamageable = true;
            m_MaxHealth = maxHealth;

            float resolved = currentHealth < 0f ? maxHealth : currentHealth;
            if (resolved < 0f)
            {
                resolved = 0f;
            }
            else if (resolved > maxHealth)
            {
                resolved = maxHealth;
            }

            m_Health = resolved;
            m_DeathDispatched = m_Health <= 0f;
        }

        /// <summary>
        /// 设置最大生命值，通常用于由 buff 驱动的上限变化。仅在已配置生命时有效，否则为空操作。
        /// </summary>
        /// <param name="maxHealth">新的最大生命值（小于 0 时钳制为 0）。</param>
        /// <param name="keepRatio">为 true 时保持当前生命占比；为 false 时保留当前生命数值并钳制到新上限。</param>
        public void SetMaxHealth(float maxHealth, bool keepRatio = true)
        {
            if (!m_IsDamageable)
            {
                return;
            }

            if (maxHealth < 0f)
            {
                maxHealth = 0f;
            }

            if (keepRatio)
            {
                float ratio = m_MaxHealth > 0f ? m_Health / m_MaxHealth : 1f;
                m_MaxHealth = maxHealth;
                m_Health = maxHealth * ratio;
            }
            else
            {
                m_MaxHealth = maxHealth;
                if (m_Health > maxHealth)
                {
                    m_Health = maxHealth;
                }
            }
        }

        /// <summary>
        /// 施加伤害。当单位不可被伤害、已死亡，或伤害量小于等于 0 时直接返回
        /// <c>DamageResult(0, false)</c> 且不触发任何事件。
        /// 否则将实际伤害钳制到剩余生命，扣血后先触发 <see cref="OnDamaged"/>（携带实际扣除量），
        /// 若本次伤害致命则随后触发一次 <see cref="OnDied"/>。
        /// </summary>
        /// <param name="amount">伤害量。</param>
        /// <param name="sourceId">伤害来源标识，缺省 -1 表示无来源。</param>
        /// <returns>伤害结算结果。</returns>
        public DamageResult TakeDamage(float amount, int sourceId = -1)
        {
            if (!m_IsDamageable || amount <= 0f || m_Health <= 0f)
            {
                return new DamageResult(0f, false);
            }

            float applied = amount > m_Health ? m_Health : amount;
            m_Health -= applied;

            bool wasLethal = m_Health <= 0f;
            if (wasLethal)
            {
                m_Health = 0f;
            }

            Action<UnitModel, float, int> damaged = OnDamaged;
            if (damaged != null)
            {
                damaged(this, applied, sourceId);
            }

            if (wasLethal)
            {
                DispatchDeath(sourceId);
            }

            return new DamageResult(applied, wasLethal);
        }

        /// <summary>
        /// 治疗，将生命值上调并钳制到 <see cref="MaxHealth"/>。
        /// 当单位不可被伤害、已死亡，或治疗量小于等于 0 时为空操作。
        /// 实际回复量大于 0 时触发 <see cref="OnHealed"/>（携带实际回复量）。
        /// </summary>
        /// <param name="amount">治疗量。</param>
        public void Heal(float amount)
        {
            if (!m_IsDamageable || amount <= 0f || m_Health <= 0f)
            {
                return;
            }

            float before = m_Health;
            float healed = m_Health + amount;
            if (healed > m_MaxHealth)
            {
                healed = m_MaxHealth;
            }

            m_Health = healed;
            float restored = m_Health - before;
            if (restored > 0f)
            {
                Action<UnitModel, float> handler = OnHealed;
                if (handler != null)
                {
                    handler(this, restored);
                }
            }
        }

        /// <summary>
        /// 强制击杀：将生命置 0 并触发死亡事件。仅在单位可被伤害且当前存活时生效，否则为空操作。
        /// 会先以剩余生命触发 <see cref="OnDamaged"/>，再触发一次 <see cref="OnDied"/>。
        /// </summary>
        /// <param name="sourceId">击杀来源标识，缺省 -1 表示无来源。</param>
        public void Kill(int sourceId = -1)
        {
            if (!m_IsDamageable || m_Health <= 0f)
            {
                return;
            }

            float remaining = m_Health;
            m_Health = 0f;

            Action<UnitModel, float, int> damaged = OnDamaged;
            if (damaged != null)
            {
                damaged(this, remaining, sourceId);
            }

            DispatchDeath(sourceId);
        }

        // ---------------------------------------------------------------
        // 可选组合能力
        // ---------------------------------------------------------------

        /// <summary>
        /// 单位的属性集合。在调用 <see cref="AttachAttributes(AttributeSet)"/> 之前为 null。
        /// </summary>
        public AttributeSet Attributes
        {
            get { return m_Attributes; }
        }

        /// <summary>
        /// 单位挂接的可推进行为（如战斗层技能系统）。以 <see cref="ITickable"/> 抽象呈现，
        /// 由上层以可插拔组合方式注入，故 Units 不依赖具体战斗实现。挂接前为 null。
        /// </summary>
        public ITickable Behavior
        {
            get { return m_Behavior; }
        }

        /// <summary>
        /// 挂接属性集合。
        /// </summary>
        /// <param name="set">属性集合实例。</param>
        public void AttachAttributes(AttributeSet set)
        {
            m_Attributes = set;
        }

        /// <summary>
        /// 挂接一个可推进行为（如战斗层的技能系统）。约定：若该行为依赖属性（如技能消耗/冷却），
        /// 应构建在本单位的 <see cref="Attributes"/> 之上（即 <c>new AbilitySystem(unit.Attributes)</c>），
        /// 以保证作用于同一属性集；本方法不强制校验该约定。每个单位至多挂接一个行为。
        /// </summary>
        /// <param name="behavior">实现 <see cref="ITickable"/> 的行为实例。</param>
        public void AttachBehavior(ITickable behavior)
        {
            m_Behavior = behavior;
        }

        /// <summary>
        /// 便捷方法：若已挂接属性集合且其中存在指定属性，则用该属性的最终值驱动最大生命。
        /// </summary>
        /// <param name="attributeId">最大生命对应的属性标识，缺省 "maxHealth"。</param>
        /// <param name="keepRatio">透传给 <see cref="SetMaxHealth(float, bool)"/> 的占比保持开关。</param>
        public void SyncMaxHealthFromAttribute(string attributeId = "maxHealth", bool keepRatio = true)
        {
            if (m_Attributes != null && m_Attributes.Has(attributeId))
            {
                SetMaxHealth(m_Attributes.GetValue(attributeId), keepRatio);
            }
        }

        // ---------------------------------------------------------------
        // 生命周期
        // ---------------------------------------------------------------

        /// <summary>
        /// 推进逻辑时间。若已挂接可推进行为（<see cref="Behavior"/>）则调用其 <see cref="ITickable.Tick(float)"/>
        /// （如推进技能冷却与效果）；否则为空操作。属性系统不需要 Tick。
        /// </summary>
        /// <param name="deltaTime">本次时间增量（秒）。</param>
        public void Tick(float deltaTime)
        {
            ITickable behavior = m_Behavior;
            if (behavior != null)
            {
                behavior.Tick(deltaTime);
            }
        }

        /// <summary>
        /// 重置为可复用的干净状态（对象池回收时使用）：清除生命（回到不可摧毁状态）、
        /// 解除属性/技能挂接、坐标归零、Progress/Threat/Priority 归零、IsTargetable 恢复为 true、
        /// 应用新的阵营标识。<see cref="Id"/> 不变。
        /// </summary>
        /// <param name="factionId">复用后的新阵营标识。</param>
        public void Reset(int factionId)
        {
            m_FactionId = factionId;

            m_PositionX = 0f;
            m_PositionY = 0f;
            m_Progress = 0f;
            m_Threat = 0f;
            m_Priority = 0;
            m_IsTargetable = true;

            m_IsDamageable = false;
            m_MaxHealth = 0f;
            m_Health = 0f;
            m_DeathDispatched = false;

            m_Attributes = null;
            m_Behavior = null;
        }

        // ---------------------------------------------------------------
        // 事件
        // ---------------------------------------------------------------

        /// <summary>
        /// 受到伤害时触发：(单位, 实际扣除量, 来源Id)。
        /// </summary>
        public event Action<UnitModel, float, int> OnDamaged;

        /// <summary>
        /// 生命值跨越到 0 及以下时触发一次：(单位, 来源Id)。同一条命只会触发一次。
        /// </summary>
        public event Action<UnitModel, int> OnDied;

        /// <summary>
        /// 被治疗时触发：(单位, 实际回复量)。
        /// </summary>
        public event Action<UnitModel, float> OnHealed;

        /// <summary>
        /// 派发死亡事件，保证同一条命只触发一次（防止 TakeDamage 致命后再 Kill 的重复触发）。
        /// </summary>
        private void DispatchDeath(int sourceId)
        {
            if (m_DeathDispatched)
            {
                return;
            }

            m_DeathDispatched = true;

            Action<UnitModel, int> handler = OnDied;
            if (handler != null)
            {
                handler(this, sourceId);
            }
        }
    }
}
