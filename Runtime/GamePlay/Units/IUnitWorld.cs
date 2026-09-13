//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Factions;
using EjoyFramework.GamePlay.Targeting;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 引擎无关的 3D 单位管理内核接口：在 <see cref="UnitModel"/> 之上提供
    /// <b>注册表 + 空间索引 + 索敌门面（facade）</b>，统一收拢单位的登记/注销、阵营分桶、
    /// 空间近邻查询，以及最佳目标采集（复用 <see cref="TargetingQuery"/>）。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="UnitWorld"/> 实现。<b>不是</b>框架模块：实现实例由场景层 <c>UnitManager</c> 持有并每帧驱动，
    /// <b>不要</b>经 <c>Framework.GetModule&lt;IUnitWorld&gt;()</c> 获取（那样只会得到一个与场景无关的空世界）；
    /// 游戏侧经 <c>UnitManager.Active.World</c> 访问。
    /// </remarks>
    public interface IUnitWorld
    {
        /// <summary>
        /// 本世界使用的阵营关系注册表（与内部索敌门面共享同一实例）。
        /// </summary>
        FactionRelations Relations { get; }

        /// <summary>
        /// 当前已登记的单位数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 遍历所有已登记单位（顺序不保证）。
        /// </summary>
        IEnumerable<UnitModel> Units { get; }

        /// <summary>
        /// 单位被登记后触发：(世界, 单位)。
        /// </summary>
        event Action<UnitWorld, UnitModel> OnUnitAdded;

        /// <summary>
        /// 单位被注销后触发：(世界, 单位)。<see cref="Clear"/> 不会逐个触发本事件。
        /// </summary>
        event Action<UnitWorld, UnitModel> OnUnitRemoved;

        /// <summary>
        /// 已登记单位死亡时转发：(世界, 单位, 来源Id)。同一条命只触发一次。
        /// </summary>
        event Action<UnitWorld, UnitModel, int> OnUnitDied;

        /// <summary>
        /// 登记一个单位：加入注册表与（登记时阵营的）阵营桶，并订阅其死亡事件转发为 <see cref="OnUnitDied"/>。
        /// </summary>
        /// <param name="unit">待登记单位；不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="unit"/> 为 <c>null</c> 时抛出。</exception>
        /// <exception cref="ArgumentException">已存在相同 <see cref="UnitModel.Id"/> 的单位时抛出。</exception>
        void Add(UnitModel unit);

        /// <summary>
        /// 注销指定单位：退订其死亡事件、从空间网格与阵营桶移除、从注册表移除，并触发 <see cref="OnUnitRemoved"/>。
        /// </summary>
        /// <param name="unitId">单位 Id。</param>
        /// <returns>存在并成功移除返回 <c>true</c>；不存在返回 <c>false</c>。</returns>
        bool Remove(int unitId);

        /// <summary>
        /// 按 Id 获取单位。
        /// </summary>
        /// <param name="unitId">单位 Id。</param>
        /// <returns>存在则返回单位；否则返回 <c>null</c>。</returns>
        UnitModel Get(int unitId);

        /// <summary>
        /// 判断是否已登记指定单位。
        /// </summary>
        /// <param name="unitId">单位 Id。</param>
        /// <returns>已登记返回 <c>true</c>。</returns>
        bool Contains(int unitId);

        /// <summary>
        /// 返回某阵营的单位桶（登记时阵营）。无任何单位时返回共享空列表。
        /// </summary>
        /// <param name="factionId">阵营 Id。</param>
        /// <returns>该阵营单位的只读列表视图（或共享空列表）。</returns>
        IReadOnlyList<UnitModel> UnitsOfFaction(int factionId);

        /// <summary>
        /// 用每个已登记单位的当前坐标刷新空间网格。单位移动坐标后需调用本方法，
        /// <see cref="QueryNearby"/> 与按射程的 <see cref="TryAcquireTarget"/> 才会反映最新位置。
        /// </summary>
        void SyncSpatial();

        /// <summary>
        /// 查询以 (x, y) 为圆心、<paramref name="radius"/> 为半径范围内的单位（基于空间网格），
        /// 结果写入 <paramref name="into"/>（写入前先清空）。结果取决于最近一次 <see cref="SyncSpatial"/>。
        /// </summary>
        /// <param name="x">圆心 X 坐标。</param>
        /// <param name="y">圆心 Y 坐标。</param>
        /// <param name="radius">查询半径（世界单位）。</param>
        /// <param name="into">输出单位列表；调用前会被清空，不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="into"/> 为 <c>null</c> 时抛出。</exception>
        void QueryNearby(float x, float y, float radius, List<UnitModel> into);

        /// <summary>
        /// 为 <paramref name="source"/> 依据过滤条件与策略采集最佳目标。
        /// </summary>
        /// <param name="source">索敌来源单位；不可为 <c>null</c>。</param>
        /// <param name="filter">过滤条件。</param>
        /// <param name="strategy">选择策略。</param>
        /// <param name="best">输出：选中的单位；返回 <c>false</c> 时为 <c>null</c>。</param>
        /// <returns>存在合格目标返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 为 <c>null</c> 时抛出。</exception>
        bool TryAcquireTarget(
            UnitModel source, in TargetFilter filter, TargetingStrategy strategy, out UnitModel best);

        /// <summary>
        /// 推进所有已登记单位的逻辑时间（逐个调用 <see cref="UnitModel.Tick(float)"/>）。
        /// </summary>
        /// <param name="deltaTime">时间增量（秒）。</param>
        void Tick(float deltaTime);

        /// <summary>
        /// 清空世界：退订所有单位的死亡事件、清空空间网格、阵营桶、死亡委托缓存与注册表。
        /// 本方法<b>不会</b>为每个单位触发 <see cref="OnUnitRemoved"/>。
        /// </summary>
        void Clear();
    }
}
