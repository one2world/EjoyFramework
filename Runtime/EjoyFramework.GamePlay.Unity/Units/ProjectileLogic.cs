//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 投射物生成参数。作为 <see cref="UnitData.Payload"/> 透传给 <see cref="ProjectileLogic.OnShow(object)"/>，
    /// 用于构建引擎无关的 <see cref="ProjectileModel"/>。是一个纯数据类（POCO），不含任何行为。
    /// </summary>
    public sealed class ProjectileSpawnParams
    {
        /// <summary>
        /// 飞行速度（单位 / 秒）。
        /// </summary>
        public float Speed;

        /// <summary>
        /// 命中半径。
        /// </summary>
        public float HitRadius;

        /// <summary>
        /// 可额外穿透的目标数（0 = 命中首个目标即销毁）。
        /// </summary>
        public int Pierce;

        /// <summary>
        /// 寿命（秒）。
        /// </summary>
        public float Lifetime;

        /// <summary>
        /// 运动方式。
        /// </summary>
        public ProjectileMotion Motion;

        /// <summary>
        /// 命中后对目标造成的伤害（由游戏层命中结算时读取，本逻辑不直接使用）。
        /// </summary>
        public float Damage;

        /// <summary>
        /// 发射者阵营（供命中结算敌我判定，本逻辑不直接使用）。
        /// </summary>
        public int OwnerFactionId;
    }

    /// <summary>
    /// 投射物单位逻辑。继承 <see cref="UnitLogic"/>，把引擎无关的 <see cref="ProjectileModel"/>
    /// （运动 + 寿命 + 穿透）桥接到 Unity 表现层（Transform）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>职责边界</b>：本逻辑只负责<b>运动、寿命与销毁</b>——每帧推进 <see cref="ProjectileModel"/>、
    /// 把模型坐标写回 Transform，过期即请求回收。
    /// <b>命中结算属于游戏层职责</b>：游戏层应查询 <see cref="UnitManager.World"/> 中的潜在目标，
    /// 对每个目标调用 <see cref="ProjectileModel.Overlaps(float, float)"/> 判定重叠、
    /// <see cref="ProjectileModel.RegisterHit(int)"/> 登记命中并消耗穿透，
    /// 并据 <see cref="ProjectileSpawnParams.Damage"/> / <see cref="ProjectileSpawnParams.OwnerFactionId"/> 结算伤害。
    /// 本类刻意不引用任何索敌 / 伤害系统，保持单一关注点。
    /// </para>
    /// <para>
    /// <b>坐标映射</b>：Unity 世界水平面 (X, Z) ↔ <see cref="ProjectileModel"/> 的 2D 战斗平面 (X, Y)；
    /// Y 高度（Unity 的 y）由表现层保留不变。
    /// </para>
    /// <para>
    /// <b>与基类的关系</b>：基类 <see cref="UnitLogic"/> 在 <see cref="UnitLogic.OnUpdate(float, float)"/> 中是把
    /// Transform → <see cref="UnitLogic.Model"/> 同步（表现层驱动模型）；投射物相反，由
    /// <see cref="ProjectileModel"/> 的运动权威地驱动 Transform。两者作用于不同模型实例，互不冲突。
    /// </para>
    /// </remarks>
    public sealed class ProjectileLogic : UnitLogic
    {
        // 缺省参数：当未提供 ProjectileSpawnParams 时使用，保证健壮。
        private const float DefaultSpeed = 10f;
        private const float DefaultHitRadius = 0.5f;
        private const int DefaultPierce = 0;
        private const float DefaultLifetime = 5f;

        private ProjectileModel m_ProjectileModel;
        private float m_Damage;
        private int m_OwnerFactionId;
        private bool m_DespawnRequested;

        /// <summary>
        /// 本投射物的引擎无关运动 / 寿命 / 穿透内核。在 <see cref="OnShow(object)"/> 中按
        /// <see cref="ProjectileSpawnParams"/> 构建；隐藏后置空。
        /// </summary>
        public ProjectileModel ProjectileModel
        {
            get { return m_ProjectileModel; }
        }

        /// <summary>
        /// 命中目标时造成的伤害（来自 <see cref="ProjectileSpawnParams.Damage"/>），供游戏层命中结算读取。
        /// </summary>
        public float Damage
        {
            get { return m_Damage; }
        }

        /// <summary>
        /// 发射者阵营（来自 <see cref="ProjectileSpawnParams.OwnerFactionId"/>），供游戏层敌我判定读取。
        /// </summary>
        public int OwnerFactionId
        {
            get { return m_OwnerFactionId; }
        }

        /// <summary>
        /// 实体显示。先调用基类完成单位通用初始化，再从 <see cref="UnitData.Payload"/>（若为
        /// <see cref="ProjectileSpawnParams"/>）读取投射物参数，按 Transform 的世界 XZ 构建
        /// <see cref="ProjectileModel"/>。缺少参数时回退到缺省值，保持健壮。
        /// </summary>
        /// <param name="userData">应为 <see cref="UnitData"/>，其 <see cref="UnitData.Payload"/> 应为 <see cref="ProjectileSpawnParams"/>。</param>
        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            m_DespawnRequested = false;

            // 从 Transform 世界 XZ 取初始位置（基类已写入表现层位姿）。
            Transform cached = CachedTransform;
            float x = 0f;
            float y = 0f;
            if (cached != null)
            {
                Vector3 p = cached.position;
                x = p.x;
                y = p.z;
            }

            // 读取投射物参数（缺省回退）。
            float speed = DefaultSpeed;
            float hitRadius = DefaultHitRadius;
            int pierce = DefaultPierce;
            float lifetime = DefaultLifetime;
            ProjectileMotion motion = ProjectileMotion.Straight;
            m_Damage = 0f;
            m_OwnerFactionId = -1;

            var data = userData as UnitData;
            var spawnParams = data != null ? data.Payload as ProjectileSpawnParams : null;
            if (spawnParams != null)
            {
                speed = spawnParams.Speed;
                hitRadius = spawnParams.HitRadius;
                pierce = spawnParams.Pierce;
                lifetime = spawnParams.Lifetime > 0f ? spawnParams.Lifetime : DefaultLifetime;
                motion = spawnParams.Motion;
                m_Damage = spawnParams.Damage;
                m_OwnerFactionId = spawnParams.OwnerFactionId;
            }

            m_ProjectileModel = new ProjectileModel(x, y, speed, motion, lifetime)
            {
                HitRadius = hitRadius,
                Pierce = pierce
            };

            // 直线投射物：以当前朝向（前方 forward 投影到 XZ）作为初始方向。
            // 追踪投射物的目标位置由游戏层在每帧 Tick 前经 SetTargetPosition 更新。
            if (motion == ProjectileMotion.Straight && cached != null)
            {
                Vector3 forward = cached.forward;
                m_ProjectileModel.SetDirection(forward.x, forward.z);
            }
        }

        /// <summary>
        /// 实体轮询。先调用基类（基类会把 Transform 同步回其 <see cref="UnitLogic.Model"/>），
        /// 再推进 <see cref="ProjectileModel"/> 并把模型坐标写回 Transform（X→x，保持 y，Y→z）。
        /// 过期则请求回收（经 <see cref="UnitManager.Active"/> 回收，否则隐藏对象作为兜底）。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            ProjectileModel model = m_ProjectileModel;
            if (model == null)
            {
                return;
            }

            model.Tick(Time.deltaTime);

            // 把模型 2D 坐标写回表现层（保持 Unity 的 y 高度）。
            Transform cached = CachedTransform;
            if (cached != null)
            {
                Vector3 p = cached.position;
                p.x = model.X;
                p.z = model.Y;
                cached.position = p;
            }

            if (model.IsExpired)
            {
                RequestDespawn();
            }
        }

        /// <summary>
        /// 实体隐藏。清理投射物模型引用，再调用基类完成单位通用注销。
        /// </summary>
        /// <param name="isShutdown">是否是关闭实体管理器时触发。</param>
        /// <param name="userData">用户自定义数据。</param>
        protected override void OnHide(bool isShutdown, object userData)
        {
            m_ProjectileModel = null;
            base.OnHide(isShutdown, userData);
        }

        /// <summary>
        /// 实体回收（实例销毁前）。丢弃投射物相关引用，再调用基类。
        /// </summary>
        protected override void OnRecycle()
        {
            m_ProjectileModel = null;
            m_Damage = 0f;
            m_OwnerFactionId = -1;
            m_DespawnRequested = false;
            base.OnRecycle();
        }

        /// <summary>
        /// 请求回收本投射物：优先经 <see cref="UnitManager.Active"/> 按 <see cref="UnitLogic.UnitId"/> 回收；
        /// 管理器不可用时兜底停用 GameObject。带去重，避免一帧内重复请求。
        /// </summary>
        private void RequestDespawn()
        {
            if (m_DespawnRequested)
            {
                return;
            }

            m_DespawnRequested = true;

            UnitManager manager = UnitManager.Active;
            if (manager != null)
            {
                manager.Despawn(UnitId);
            }
            else if (CachedTransform != null)
            {
                CachedTransform.gameObject.SetActive(false);
            }
        }
    }
}
