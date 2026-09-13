//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Unity;
using UnityEngine;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 挂在已生成实体上的单位 MonoBehaviour。继承框架 <see cref="EntityLogic"/>，
    /// 把引擎无关的 <see cref="UnitModel"/> 与 Unity 表现层（Transform / Animator / Collider）桥接起来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>生命周期</b>（由框架 <c>DefaultEntityHelper</c> 驱动）：
    /// <list type="bullet">
    /// <item><see cref="OnInit"/> 仅首次实例化触发（池复用被基类 <c>m_Inited</c> 守卫跳过），用于缓存组件引用。</item>
    /// <item><see cref="OnShow"/> 每次显示触发：从 <see cref="UnitData"/> 重建 / 重置 <see cref="UnitModel"/>，并自注册进世界。</item>
    /// <item><see cref="OnUpdate"/> 每帧把 Transform 的世界位置同步回 <see cref="UnitModel"/>（不在此推进模型 Tick，避免与 <see cref="UnitManager"/> 双重 Tick）。</item>
    /// <item><see cref="OnHide"/> 每次隐藏触发：从世界注销并清理引用。</item>
    /// <item><see cref="OnRecycle"/> 实例销毁前触发：丢弃所有引用。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>自注册</b>：通过静态 <see cref="UnitManager.Active"/> 在 <see cref="OnShow"/> 注册、<see cref="OnHide"/> 注销，
    /// 避免从 <c>IEntity.Handle</c> 反查逻辑组件。<see cref="UnitManager.Active"/> 在编辑器 / 未运行时可能为 null，本类对此健壮。
    /// </para>
    /// <para>
    /// <b>坐标映射</b>：Unity 世界水平面 (X, Z) → <see cref="UnitModel"/> 的 2D 战斗平面 (X, Y)。
    /// </para>
    /// </remarks>
    // 非 sealed：作为单位种类继承骨架的基类（Projectile / Mechanism / Item / 角色 等子类继承本类）。
    public class UnitLogic : EntityLogic
    {
        private UnitModel m_Model;
        private UnitKind m_Kind;
        private UnitManager m_OwnerWorld;

        private Animator m_Animator;
        private Collider m_Collider;

        /// <summary>
        /// 本单位的引擎无关逻辑内核。在 <see cref="OnShow"/> 中按 <see cref="UnitData"/> 创建或池化重置；
        /// 隐藏后仍保留实例以便下次复用（仅清空注册关系）。
        /// </summary>
        public UnitModel Model
        {
            get { return m_Model; }
        }

        /// <summary>
        /// 单位大类（来自 <see cref="UnitData.Kind"/>）。
        /// </summary>
        public UnitKind Kind
        {
            get { return m_Kind; }
        }

        /// <summary>
        /// 缓存的 <see cref="UnityEngine.Animator"/>，可能为 null（实体无 Animator 时）。
        /// </summary>
        public Animator Animator
        {
            get { return m_Animator; }
        }

        /// <summary>
        /// 缓存的 <see cref="UnityEngine.Collider"/>，可能为 null（实体无 Collider 时）。
        /// </summary>
        public Collider Collider
        {
            get { return m_Collider; }
        }

        /// <summary>
        /// 单位 Id；模型尚未建立时返回 -1。
        /// </summary>
        public int UnitId
        {
            get { return m_Model != null ? m_Model.Id : -1; }
        }

        /// <summary>
        /// 实体初始化（仅首次）。先调用基类以缓存 <see cref="EntityLogic.CachedTransform"/>，
        /// 再 null-safe 地缓存 Animator / Collider。
        /// </summary>
        /// <param name="userData">用户自定义数据。</param>
        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            m_Animator = GetComponentInChildren<Animator>();
            m_Collider = GetComponent<Collider>();
        }

        /// <summary>
        /// 实体显示。从 <see cref="UnitData"/> 建立 / 重置模型，写入表现层位姿，并自注册进世界。
        /// </summary>
        /// <param name="userData">应为 <see cref="UnitData"/>；为 null 时记录警告并提前返回。</param>
        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            var data = userData as UnitData;
            if (data == null)
            {
                Log.Warning("[UnitLogic] OnShow received null/invalid UnitData; unit not initialized.");
                return;
            }

            m_Kind = data.Kind;

            // 复用既有模型（Id 一致时仅 Reset），否则新建。
            if (m_Model != null && m_Model.Id == data.UnitId)
            {
                m_Model.Reset(data.FactionId);
            }
            else
            {
                m_Model = new UnitModel(data.UnitId, data.FactionId);
            }

            if (data.MaxHealth > 0f)
            {
                m_Model.ConfigureHealth(data.MaxHealth);
            }

            // 写入表现层位姿。
            Transform cached = CachedTransform;
            if (cached != null)
            {
                cached.localPosition = data.Position;
                cached.localRotation = data.Rotation;
            }

            // 世界水平面 (X, Z) → 模型 2D 平面 (X, Y)。
            m_Model.SetPosition(data.Position.x, data.Position.z);
            m_Model.IsTargetable = true;

            // 自注册进世界（编辑器 / 未运行时 Active 可能为 null）。
            m_OwnerWorld = UnitManager.Active;
            if (m_OwnerWorld != null)
            {
                m_OwnerWorld.Register(this);
            }
        }

        /// <summary>
        /// 实体轮询。把 Transform 的世界位置同步回模型的 2D 坐标，使模型位置跟随表现层
        /// （移动系统后续会移动 Transform）。<b>不</b>在此推进模型 Tick——由 <see cref="UnitManager"/> 每帧统一推进世界，避免双重 Tick。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            UnitModel model = m_Model;
            Transform cached = CachedTransform;
            if (model != null && cached != null)
            {
                Vector3 p = cached.position;
                model.SetPosition(p.x, p.z);
            }
        }

        /// <summary>
        /// 实体隐藏。从世界注销并清理瞬态引用（保留 <see cref="Model"/> 实例以便下次复用）。
        /// </summary>
        /// <param name="isShutdown">是否是关闭实体管理器时触发。</param>
        /// <param name="userData">用户自定义数据。</param>
        protected override void OnHide(bool isShutdown, object userData)
        {
            if (m_OwnerWorld != null)
            {
                m_OwnerWorld.Unregister(this);
                m_OwnerWorld = null;
            }

            base.OnHide(isShutdown, userData);
        }

        /// <summary>
        /// 实体回收（实例销毁前）。丢弃全部引用。
        /// </summary>
        protected override void OnRecycle()
        {
            m_OwnerWorld = null;
            m_Model = null;
            m_Animator = null;
            m_Collider = null;

            base.OnRecycle();
        }
    }
}
