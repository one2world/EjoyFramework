//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Entity;
using EjoyFramework.GamePlay.Factions;
using EjoyFramework.GamePlay.Targeting;
using EjoyFramework.Core.Unity;
using UnityEngine;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 场景级单位驱动 + 生成门面。负责：
    /// <list type="bullet">
    /// <item>持有引擎无关的 <see cref="UnitWorld"/>（空间索引 + 索敌 + 每帧 Tick）。</item>
    /// <item>经框架核心实体系统生成 / 回收单位实体（<see cref="UnitLogic"/> 在其 OnShow/OnHide 自注册 / 注销）。</item>
    /// <item>每帧统一推进世界一次，避免每个单位各自 Tick 造成的双重推进。</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>生成 + 注册接线</b>：经核心
    /// <see cref="IEntityManager.ShowEntity(int, string, string, int, object)"/> 直接把 <see cref="UnitData"/>
    /// 作为 <c>userData</c> 透传（核心层不会像 <c>EntityComponent</c> 那样包进 <c>EntityData</c>），
    /// <see cref="UnitLogic.OnShow(object)"/> 即可 <c>userData as UnitData</c> 取回；
    /// 注册改由 <see cref="UnitLogic"/> 经静态 <see cref="Active"/> 自注册，避免从 <c>IEntity.Handle</c> 反查逻辑组件。
    /// </para>
    /// <para>
    /// <b>静态 <see cref="Active"/></b> 在 <see cref="Awake"/> 设置、<see cref="OnDestroy"/> 清空；
    /// 编辑器 / 未运行时可能为 null，<see cref="UnitLogic"/> 对此健壮。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/GamePlay/Unit Manager")]
    public sealed class UnitManager : MonoBehaviour
    {
        /// <summary>
        /// 当前活动的单位管理器。供 <see cref="UnitLogic"/> 自注册使用；可能为 null。
        /// </summary>
        public static UnitManager Active { get; private set; }

        [Tooltip("空间索引（AOI）网格单元大小")]
        [SerializeField]
        private float m_AoiCellSize = 10f;

        [Tooltip("是否每帧自动推进世界（SyncSpatial + Tick）。关闭后需由外部手动驱动 TickWorld。")]
        [SerializeField]
        private bool m_AutoTick = true;

        private UnitWorld m_World;
        private IEntityManager m_EntityManager;

        /// <summary>
        /// 引擎无关的单位世界。<see cref="Awake"/> 后非 null。
        /// </summary>
        public UnitWorld World
        {
            get { return m_World; }
        }

        private void Awake()
        {
            Active = this;
            // 默认构建一个全新的阵营关系表；调用方可在 Awake 之后经 World 重建或注入自定义关系。
            m_World = new UnitWorld();
            m_World.SetAoiCellSize(m_AoiCellSize);
        }

        private void OnDestroy()
        {
            // 确定性清理：UnitWorld 不是框架模块（不会被 Framework.Shutdown 驱动），
            // 故在管理器销毁时退订世界对各单位死亡事件的订阅并清空运行时状态。
            m_World?.Clear();

            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>
        /// 注入自定义阵营关系并重建世界。须在任何单位生成 / 注册之前调用（否则已注册单位会丢失）。
        /// </summary>
        /// <param name="relations">阵营关系表；为 null 时使用全新空表。</param>
        public void ConfigureWorld(FactionRelations relations)
        {
            m_World = new UnitWorld();
            m_World.SetRelations(relations ?? new FactionRelations());
            m_World.SetAoiCellSize(m_AoiCellSize);
        }

        // ---------------------------------------------------------------
        // 生成 / 回收
        // ---------------------------------------------------------------

        /// <summary>
        /// 生成一个单位实体。经核心实体系统显示实体，<see cref="UnitData"/> 原样作为 userData 透传，
        /// <see cref="UnitLogic"/> 在其 OnShow 自注册进世界。
        /// </summary>
        /// <param name="data">生成载荷（非 null）。</param>
        /// <param name="assetName">实体资源名称。</param>
        /// <param name="entityGroup">实体组名称。</param>
        /// <param name="priority">加载优先级，缺省 0。</param>
        /// <returns>生成单位的 Id（即 <see cref="UnitData.UnitId"/>）；失败返回 -1。</returns>
        public int Spawn(UnitData data, string assetName, string entityGroup, int priority = 0)
        {
            if (data == null)
            {
                Log.Warning("[UnitManager] Spawn called with null UnitData.");
                return -1;
            }

            IEntityManager em = EntityManager;
            if (em == null)
            {
                Log.Error("[UnitManager] Entity manager is unavailable; cannot spawn unit {0}.", data.UnitId);
                return -1;
            }

            // 核心 ShowEntity 不包装 userData——UnitData 原样透传给 UnitLogic.OnShow。
            em.ShowEntity(data.UnitId, assetName, entityGroup, priority, data);
            return data.UnitId;
        }

        /// <summary>
        /// 依据模板生成一个单位。模板提供资源名 / 实体组，并经
        /// <see cref="UnitDefinition.CreateData(int, Vector3, Quaternion, int)"/> 产出载荷。
        /// </summary>
        /// <param name="definition">单位模板（非 null）。</param>
        /// <param name="unitId">本次生成的单位唯一标识。</param>
        /// <param name="position">生成位置（世界坐标）。</param>
        /// <param name="rotation">生成朝向；传入 <c>default</c> 时按 identity 处理。</param>
        /// <param name="factionOverride">阵营覆盖；<c>&gt;= 0</c> 时覆盖模板默认阵营。</param>
        /// <param name="priority">加载优先级，缺省 0。</param>
        /// <returns>生成单位的 Id；失败返回 -1。</returns>
        public int Spawn(
            UnitDefinition definition,
            int unitId,
            Vector3 position,
            Quaternion rotation = default,
            int factionOverride = -1,
            int priority = 0)
        {
            if (definition == null)
            {
                Log.Warning("[UnitManager] Spawn called with null UnitDefinition.");
                return -1;
            }

            UnitData data = definition.CreateData(unitId, position, rotation, factionOverride);
            return Spawn(data, definition.AssetName, definition.EntityGroup, priority);
        }

        /// <summary>
        /// 回收一个单位实体。经核心实体系统隐藏实体，<see cref="UnitLogic.OnHide"/> 自注销。
        /// </summary>
        /// <param name="unitId">要回收的单位 Id。</param>
        public void Despawn(int unitId)
        {
            IEntityManager em = EntityManager;
            if (em == null)
            {
                return;
            }

            em.HideEntity(unitId);
        }

        // ---------------------------------------------------------------
        // 注册 / 注销（由 UnitLogic 自调用）
        // ---------------------------------------------------------------

        /// <summary>
        /// 把单位逻辑的模型加入世界。由 <see cref="UnitLogic.OnShow(object)"/> 调用。
        /// </summary>
        /// <param name="logic">单位逻辑。</param>
        internal void Register(UnitLogic logic)
        {
            if (logic == null || logic.Model == null || m_World == null)
            {
                return;
            }

            m_World.Add(logic.Model);
        }

        /// <summary>
        /// 把单位逻辑的模型移出世界。由 <see cref="UnitLogic.OnHide"/> 调用。
        /// </summary>
        /// <param name="logic">单位逻辑。</param>
        internal void Unregister(UnitLogic logic)
        {
            if (logic == null || logic.Model == null || m_World == null)
            {
                return;
            }

            m_World.Remove(logic.Model.Id);
        }

        // ---------------------------------------------------------------
        // 每帧驱动
        // ---------------------------------------------------------------

        private void Update()
        {
            if (m_AutoTick)
            {
                TickWorld(Time.deltaTime);
            }
        }

        /// <summary>
        /// 推进世界一帧：刷新空间索引并 Tick 全部模型一次。当 <see cref="m_AutoTick"/> 关闭时由外部调用。
        /// </summary>
        /// <param name="deltaTime">本次时间增量（秒）。</param>
        public void TickWorld(float deltaTime)
        {
            if (m_World == null)
            {
                return;
            }

            m_World.SyncSpatial();
            m_World.Tick(deltaTime);
        }

        // ---------------------------------------------------------------
        // 查询透传
        // ---------------------------------------------------------------

        /// <summary>
        /// 按 Id 取回世界中的单位模型；不存在时返回 null。
        /// </summary>
        /// <param name="unitId">单位 Id。</param>
        /// <returns>单位模型或 null。</returns>
        public UnitModel Get(int unitId)
        {
            return m_World != null ? m_World.Get(unitId) : null;
        }

        /// <summary>
        /// 索敌透传：为来源单位按过滤条件与策略选择最佳目标。
        /// </summary>
        /// <param name="source">索敌来源单位。</param>
        /// <param name="filter">目标过滤条件。</param>
        /// <param name="strategy">目标选择策略。</param>
        /// <param name="best">命中时输出最佳目标，否则为 null。</param>
        /// <returns>是否成功选到目标。</returns>
        public bool TryAcquireTarget(UnitModel source, in TargetFilter filter, TargetingStrategy strategy, out UnitModel best)
        {
            if (m_World == null)
            {
                best = null;
                return false;
            }

            return m_World.TryAcquireTarget(source, in filter, strategy, out best);
        }

        // ---------------------------------------------------------------
        // 内部
        // ---------------------------------------------------------------

        /// <summary>
        /// 惰性拉取并缓存核心实体管理器。
        /// </summary>
        private IEntityManager EntityManager
        {
            get
            {
                if (m_EntityManager == null)
                {
                    m_EntityManager = Framework.GetModule<IEntityManager>();
                }

                return m_EntityManager;
            }
        }
    }
}
