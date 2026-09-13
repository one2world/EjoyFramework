//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Factions;
using EjoyFramework.GamePlay.Spatial;
using EjoyFramework.GamePlay.Targeting;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 引擎无关的 3D 单位管理内核：在既有的 <see cref="UnitModel"/> 之上提供
    /// <b>注册表 + 空间索引 + 索敌门面（facade）</b>。它把单位的登记/注销、阵营分桶、
    /// 空间近邻查询，以及最佳目标采集（复用 <see cref="TargetingQuery"/>）统一收拢到一处，
    /// 完全可单元测试。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 组合关系：
    /// <list type="bullet">
    /// <item><b>注册表</b>：按 <see cref="UnitModel.Id"/> 索引的 <see cref="Dictionary{TKey,TValue}"/>。</item>
    /// <item><b>阵营桶</b>：阵营 Id → 单位列表，在 <see cref="Add"/>/<see cref="Remove"/> 时维护。</item>
    /// <item><b>空间索引</b>：复用 <see cref="AoiGrid"/> 均匀空间哈希网格做半径近邻查询。</item>
    /// <item><b>索敌</b>：内部持有一个由 <see cref="Relations"/> 构建的 <see cref="TargetingQuery"/>。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>阵营桶语义（重要）</b>：阵营桶记录的是“<b>登记时（Add 时）的阵营</b>”。
    /// 若单位运行时改变 <see cref="UnitModel.FactionId"/>，本类型<b>不会</b>自动重新分桶
    /// （以保持热路径零开销）。需要重新分桶时，请显式 <see cref="Remove"/> 后再 <see cref="Add"/>。
    /// 注意：索敌（<see cref="TryAcquireTarget"/>）始终读取单位的实时 <see cref="UnitModel.FactionId"/>
    /// 做敌我判定，<b>不</b>受阵营桶滞后影响。
    /// </para>
    /// <para>
    /// <b>空间索引语义</b>：网格不会自动跟随单位移动而更新；调用方在移动单位坐标后，需调用
    /// <see cref="SyncSpatial"/> 刷新网格，随后 <see cref="QueryNearby"/> 与按射程的
    /// <see cref="TryAcquireTarget"/> 才会反映最新位置。
    /// </para>
    /// <para>
    /// <b>单线程</b>：本类型非线程安全，复用了内部缓冲（近邻查询的 id 缓冲、单位缓冲、
    /// Tick 快照列表），所有访问应发生在同一逻辑线程。热路径
    /// （<see cref="Tick(float)"/>、<see cref="QueryNearby"/>、<see cref="TryAcquireTarget"/>）
    /// 除复用缓冲外不产生堆分配，不使用 LINQ。
    /// </para>
    /// <para>
    /// <b>所有权（重要）</b>：本类型<b>不是</b>框架模块（不继承 FrameworkModule），<b>不要</b>经
    /// <c>Framework.GetModule&lt;IUnitWorld&gt;()</c> 获取——那样只会得到一个与场景无关的空世界。
    /// 它由场景层 <c>UnitManager</c> 直接 new 持有并每帧驱动（<see cref="SyncSpatial"/> + <see cref="Tick(float)"/>）；
    /// 游戏侧统一经 <c>UnitManager.Active.World</c> 访问唯一的活动世界。提供无参构造（默认全新
    /// <see cref="FactionRelations"/> 与默认网格单元尺寸），自定义依赖请在构造后调用
    /// <see cref="SetRelations"/>/<see cref="SetAoiCellSize"/>（须在登记任何单位之前）。
    /// </para>
    /// </remarks>
    public sealed class UnitWorld : IUnitWorld
    {
        /// <summary>
        /// 默认空间哈希网格单元尺寸（世界单位），与历史构造默认值一致。
        /// </summary>
        private const float DefaultAoiCellSize = 10f;

        private FactionRelations m_Relations;
        private AoiGrid m_Grid;
        private TargetingQuery m_Query;

        /// <summary>
        /// 注册表：单位 Id → 单位实例。
        /// </summary>
        private readonly Dictionary<int, UnitModel> m_Units;

        /// <summary>
        /// 阵营桶：阵营 Id → 该阵营单位列表（登记时阵营）。
        /// </summary>
        private readonly Dictionary<int, List<UnitModel>> m_FactionBuckets;

        /// <summary>
        /// 记录每个已登记单位<b>登记时（Add 时）的阵营 Id</b>，用于 <see cref="Remove"/> 时
        /// 准确定位其所在阵营桶。若单位运行时改变 <see cref="UnitModel.FactionId"/>，出桶仍以此处
        /// 缓存的登记阵营为准，避免按实时阵营查错桶而在原桶留下僵尸引用。
        /// </summary>
        private readonly Dictionary<int, int> m_RegisteredFactions;

        /// <summary>
        /// 为每个已登记单位缓存其 <see cref="UnitModel.OnDied"/> 订阅委托，
        /// 以便 <see cref="Remove"/>/<see cref="Clear"/> 时精确退订（避免重复转发与内存泄漏）。
        /// </summary>
        private readonly Dictionary<int, Action<UnitModel, int>> m_DeathHandlers;

        /// <summary>
        /// 复用缓冲：近邻查询命中的实体 id（避免每次查询分配）。
        /// </summary>
        private readonly List<int> m_NearbyIdBuffer;

        /// <summary>
        /// 复用缓冲：索敌时收集的候选单位（按 <see cref="ITargetableUnit"/> 协变传入 <see cref="TargetingQuery"/>）。
        /// </summary>
        private readonly List<UnitModel> m_CandidateBuffer;

        /// <summary>
        /// 复用缓冲：<see cref="Tick(float)"/> 前对注册表取值快照，规避“遍历中被 OnDied 处理器调用 Remove 修改集合”的风险。
        /// </summary>
        private readonly List<UnitModel> m_TickSnapshot;

        /// <summary>
        /// 当某阵营无单位时，<see cref="UnitsOfFaction"/> 返回的共享空列表（只读语义，避免每次分配）。
        /// </summary>
        private readonly List<UnitModel> m_EmptyBucket;

        /// <summary>
        /// 构造单位世界（无参）。供 <c>UnitManager</c> 默认构造使用；默认使用全新的
        /// <see cref="FactionRelations"/> 与默认网格单元尺寸（<see cref="DefaultAoiCellSize"/>）。
        /// 如需自定义依赖，构造后调用 <see cref="SetRelations"/>/<see cref="SetAoiCellSize"/>。
        /// </summary>
        public UnitWorld()
        {
            m_Units = new Dictionary<int, UnitModel>();
            m_FactionBuckets = new Dictionary<int, List<UnitModel>>();
            m_RegisteredFactions = new Dictionary<int, int>();
            m_DeathHandlers = new Dictionary<int, Action<UnitModel, int>>();

            m_NearbyIdBuffer = new List<int>();
            m_CandidateBuffer = new List<UnitModel>();
            m_TickSnapshot = new List<UnitModel>();
            m_EmptyBucket = new List<UnitModel>();

            m_Relations = new FactionRelations();
            m_Grid = new AoiGrid(DefaultAoiCellSize);
            m_Query = new TargetingQuery(m_Relations);
        }

        /// <summary>
        /// 以指定依赖构造单位世界（程序集内便捷构造，供测试与本地构建使用）。
        /// 等价于无参构造后依次调用 <see cref="SetAoiCellSize"/> 与 <see cref="SetRelations"/>。
        /// </summary>
        /// <param name="relations">阵营关系注册表，用于索敌的敌我判定；不可为 <c>null</c>。</param>
        /// <param name="aoiCellSize">空间哈希网格的单元尺寸（世界单位），必须为正，默认 10f。</param>
        /// <exception cref="ArgumentNullException"><paramref name="relations"/> 为 <c>null</c> 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="aoiCellSize"/> 非正（含 NaN）时抛出。</exception>
        internal UnitWorld(FactionRelations relations, float aoiCellSize = DefaultAoiCellSize)
            : this()
        {
            if (relations == null)
            {
                throw new ArgumentNullException(nameof(relations));
            }

            SetAoiCellSize(aoiCellSize); // 单元尺寸非正时由 AoiGrid 抛 ArgumentOutOfRangeException。
            SetRelations(relations);
        }

        /// <summary>
        /// 注入阵营关系注册表，并据此重建内部索敌门面（<see cref="TargetingQuery"/>）。
        /// 与核心模块 SetLoader/SetHelper 的注入惯例一致。须在登记任何单位之前调用，
        /// 否则已登记单位的敌我判定可能与新关系不一致。
        /// </summary>
        /// <param name="relations">阵营关系注册表；不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="relations"/> 为 <c>null</c> 时抛出。</exception>
        public void SetRelations(FactionRelations relations)
        {
            if (relations == null)
            {
                throw new ArgumentNullException(nameof(relations));
            }

            m_Relations = relations;
            m_Query = new TargetingQuery(relations);
        }

        /// <summary>
        /// 注入空间哈希网格的单元尺寸并据此重建空间索引。须在登记任何单位之前调用，
        /// 否则需重新 <see cref="SyncSpatial"/> 才能在新网格上得到一致结果。
        /// </summary>
        /// <param name="aoiCellSize">单元尺寸（世界单位），必须为正。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="aoiCellSize"/> 非正（含 NaN）时抛出。</exception>
        public void SetAoiCellSize(float aoiCellSize)
        {
            // 单元尺寸非正（含 NaN）时由 AoiGrid 抛 ArgumentOutOfRangeException。
            m_Grid = new AoiGrid(aoiCellSize);
        }

        /// <summary>
        /// 本世界使用的阵营关系注册表（与内部索敌门面共享同一实例）。
        /// </summary>
        public FactionRelations Relations
        {
            get { return m_Relations; }
        }

        /// <summary>
        /// 当前已登记的单位数量。
        /// </summary>
        public int Count
        {
            get { return m_Units.Count; }
        }

        /// <summary>
        /// 遍历所有已登记单位（顺序不保证）。
        /// </summary>
        public IEnumerable<UnitModel> Units
        {
            get { return m_Units.Values; }
        }

        // ---------------------------------------------------------------
        // 事件
        // ---------------------------------------------------------------

        /// <summary>
        /// 单位被登记后触发：(世界, 单位)。
        /// </summary>
        public event Action<UnitWorld, UnitModel> OnUnitAdded;

        /// <summary>
        /// 单位被注销后触发：(世界, 单位)。<see cref="Clear"/> 不会逐个触发本事件。
        /// </summary>
        public event Action<UnitWorld, UnitModel> OnUnitRemoved;

        /// <summary>
        /// 已登记单位死亡时转发：(世界, 单位, 来源Id)。来自每个单位 <see cref="UnitModel.OnDied"/> 的转发，
        /// 同一条命只触发一次。
        /// </summary>
        public event Action<UnitWorld, UnitModel, int> OnUnitDied;

        // ---------------------------------------------------------------
        // 注册表
        // ---------------------------------------------------------------

        /// <summary>
        /// 登记一个单位：加入注册表与（登记时阵营的）阵营桶，并订阅其 <see cref="UnitModel.OnDied"/>
        /// 以便转发为 <see cref="OnUnitDied"/>。登记成功后触发 <see cref="OnUnitAdded"/>。
        /// </summary>
        /// <param name="unit">待登记单位；不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="unit"/> 为 <c>null</c> 时抛出。</exception>
        /// <exception cref="ArgumentException">已存在相同 <see cref="UnitModel.Id"/> 的单位时抛出。</exception>
        public void Add(UnitModel unit)
        {
            if (unit == null)
            {
                throw new ArgumentNullException(nameof(unit));
            }

            if (m_Units.ContainsKey(unit.Id))
            {
                throw new ArgumentException(
                    "已存在相同 Id 的单位：" + unit.Id + "。", nameof(unit));
            }

            m_Units.Add(unit.Id, unit);
            AddToFactionBucket(unit);

            // 订阅死亡事件并缓存委托，便于精确退订。
            Action<UnitModel, int> handler = OnUnitModelDied;
            m_DeathHandlers[unit.Id] = handler;
            unit.OnDied += handler;

            Action<UnitWorld, UnitModel> added = OnUnitAdded;
            if (added != null)
            {
                added(this, unit);
            }
        }

        /// <summary>
        /// 注销指定单位：退订其死亡事件、从空间网格与阵营桶移除、从注册表移除，并触发 <see cref="OnUnitRemoved"/>。
        /// </summary>
        /// <param name="unitId">单位 Id。</param>
        /// <returns>存在并成功移除返回 <c>true</c>；不存在返回 <c>false</c>。</returns>
        public bool Remove(int unitId)
        {
            if (!m_Units.TryGetValue(unitId, out UnitModel unit))
            {
                return false;
            }

            UnsubscribeDeath(unit);
            m_Grid.Remove(unitId);
            RemoveFromFactionBucket(unit);
            m_Units.Remove(unitId);

            Action<UnitWorld, UnitModel> removed = OnUnitRemoved;
            if (removed != null)
            {
                removed(this, unit);
            }

            return true;
        }

        /// <summary>
        /// 按 Id 获取单位。
        /// </summary>
        /// <param name="unitId">单位 Id。</param>
        /// <returns>存在则返回单位；否则返回 <c>null</c>。</returns>
        public UnitModel Get(int unitId)
        {
            m_Units.TryGetValue(unitId, out UnitModel unit);
            return unit;
        }

        /// <summary>
        /// 判断是否已登记指定单位。
        /// </summary>
        /// <param name="unitId">单位 Id。</param>
        /// <returns>已登记返回 <c>true</c>。</returns>
        public bool Contains(int unitId)
        {
            return m_Units.ContainsKey(unitId);
        }

        /// <summary>
        /// 返回某阵营的单位桶（登记时阵营）。无任何单位时返回共享空列表。
        /// </summary>
        /// <remarks>
        /// 返回的是<b>内部桶的实时引用</b>（非副本，零分配）：随 <see cref=”Add”/>/<see cref=”Remove”/> 变化。
        /// <b>调用方约束</b>：(1) 不得改写——即便经 <see cref=”IReadOnlyList{T}”/> 暴露，也不要回转型为 <c>List</c> 修改；
        /// (2) 不要跨 <see cref=”Add”/>/<see cref=”Remove”/> 长期持有，也不要在发生结构性修改期间枚举它
        /// （枚举会因桶被修改而失效抛异常）。需安全留存或跨帧使用请自行复制一份。详见类型说明中的”阵营桶语义”。
        /// </remarks>
        /// <param name="factionId">阵营 Id。</param>
        /// <returns>该阵营单位的只读列表视图（或共享空列表）。</returns>
        public IReadOnlyList<UnitModel> UnitsOfFaction(int factionId)
        {
            if (m_FactionBuckets.TryGetValue(factionId, out List<UnitModel> bucket))
            {
                return bucket;
            }

            return m_EmptyBucket;
        }

        // ---------------------------------------------------------------
        // 空间索引
        // ---------------------------------------------------------------

        /// <summary>
        /// 用每个已登记单位的当前 <see cref="UnitModel.PositionX"/>/<see cref="UnitModel.PositionY"/>
        /// 刷新空间网格。单位移动坐标后需调用本方法，<see cref="QueryNearby"/> 与按射程的
        /// <see cref="TryAcquireTarget"/> 才会反映最新位置。
        /// </summary>
        public void SyncSpatial()
        {
            foreach (KeyValuePair<int, UnitModel> kv in m_Units)
            {
                UnitModel unit = kv.Value;
                m_Grid.AddOrUpdate(unit.Id, unit.PositionX, unit.PositionY);
            }
        }

        /// <summary>
        /// 查询以 (x, y) 为圆心、<paramref name="radius"/> 为半径范围内的单位（基于空间网格），
        /// 结果写入 <paramref name="into"/>（写入前先清空）。结果取决于最近一次 <see cref="SyncSpatial"/>。
        /// </summary>
        /// <param name="x">圆心 X 坐标。</param>
        /// <param name="y">圆心 Y 坐标。</param>
        /// <param name="radius">查询半径（世界单位）。</param>
        /// <param name="into">输出单位列表；调用前会被清空，不可为 <c>null</c>。</param>
        /// <exception cref="ArgumentNullException"><paramref name="into"/> 为 <c>null</c> 时抛出。</exception>
        public void QueryNearby(float x, float y, float radius, List<UnitModel> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            m_Grid.QueryRadius(x, y, radius, m_NearbyIdBuffer);
            for (int i = 0; i < m_NearbyIdBuffer.Count; i++)
            {
                if (m_Units.TryGetValue(m_NearbyIdBuffer[i], out UnitModel unit))
                {
                    into.Add(unit);
                }
            }
        }

        // ---------------------------------------------------------------
        // 索敌门面
        // ---------------------------------------------------------------

        /// <summary>
        /// 为 <paramref name="source"/> 依据过滤条件与策略采集最佳目标。
        /// </summary>
        /// <remarks>
        /// 候选集合的选取：当 <see cref="TargetFilter.MaxRange"/> &gt; 0 时，先用空间网格
        /// （<see cref="QueryNearby"/>，以 source 坐标与 MaxRange）取近邻子集作为候选；
        /// 否则以全部已登记单位为候选。随后把候选（按 <see cref="ITargetableUnit"/> 协变）交给内部
        /// <see cref="TargetingQuery.TryAcquire"/>，并排除 source 自身（<c>excludeId = source.Id</c>）。
        /// 阵营/存活/可瞄准/射程过滤与策略选择均由 <see cref="TargetingQuery"/> 负责。
        /// 复用内部缓冲，<b>仅单线程</b>。
        /// </remarks>
        /// <param name="source">索敌来源单位；不可为 <c>null</c>。</param>
        /// <param name="filter">过滤条件。</param>
        /// <param name="strategy">选择策略。</param>
        /// <param name="best">输出：选中的单位；返回 <c>false</c> 时为 <c>null</c>。</param>
        /// <returns>存在合格目标返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 为 <c>null</c> 时抛出。</exception>
        public bool TryAcquireTarget(
            UnitModel source, in TargetFilter filter, TargetingStrategy strategy, out UnitModel best)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            best = null;

            // 收集候选到复用缓冲：有射程→近邻子集；无射程→全部单位。
            m_CandidateBuffer.Clear();
            if (filter.MaxRange > 0f)
            {
                // QueryNearby 会清空并填充传入列表，可直接复用候选缓冲。
                QueryNearby(source.PositionX, source.PositionY, filter.MaxRange, m_CandidateBuffer);
            }
            else
            {
                foreach (KeyValuePair<int, UnitModel> kv in m_Units)
                {
                    m_CandidateBuffer.Add(kv.Value);
                }
            }

            // List<UnitModel> 经接口协变作为 IReadOnlyList<ITargetableUnit> 传入（UnitModel : ITargetableUnit）。
            bool ok = m_Query.TryAcquire(
                m_CandidateBuffer,
                source.FactionId, source.PositionX, source.PositionY,
                filter, strategy, source.Id,
                out ITargetableUnit acquired);

            if (!ok)
            {
                return false;
            }

            // 回映为 UnitModel：候选全部来自本世界，命中后理应能在注册表中找到。
            best = acquired as UnitModel;
            return best != null;
        }

        // ---------------------------------------------------------------
        // 生命周期
        // ---------------------------------------------------------------

        /// <summary>
        /// 推进所有已登记单位的逻辑时间（逐个调用 <see cref="UnitModel.Tick(float)"/>）。
        /// </summary>
        /// <remarks>
        /// 先对注册表取值快照到复用列表再遍历：单位在 Tick 中可能因致命伤触发
        /// <see cref="UnitModel.OnDied"/>，进而其处理器可能调用 <see cref="Remove"/> 修改注册表；
        /// 直接 foreach 字典会在 Mono/IL2CPP 上抛“集合在枚举期间被修改”。快照可规避此风险，
        /// 且对快照时已不存在的单位不再 Tick。
        /// </remarks>
        /// <param name="deltaTime">时间增量（秒）。</param>
        public void Tick(float deltaTime)
        {
            m_TickSnapshot.Clear();
            foreach (KeyValuePair<int, UnitModel> kv in m_Units)
            {
                m_TickSnapshot.Add(kv.Value);
            }

            for (int i = 0; i < m_TickSnapshot.Count; i++)
            {
                UnitModel unit = m_TickSnapshot[i];
                // 若该单位已在本轮更早的回调中被移除，则跳过其 Tick。
                if (m_Units.ContainsKey(unit.Id))
                {
                    unit.Tick(deltaTime);
                }
            }

            m_TickSnapshot.Clear();
        }

        /// <summary>
        /// 清空世界：退订所有单位的死亡事件、清空空间网格、阵营桶、死亡委托缓存与注册表。
        /// 本方法<b>不会</b>为每个单位触发 <see cref="OnUnitRemoved"/>。
        /// </summary>
        public void Clear()
        {
            foreach (KeyValuePair<int, UnitModel> kv in m_Units)
            {
                UnitModel unit = kv.Value;
                if (m_DeathHandlers.TryGetValue(unit.Id, out Action<UnitModel, int> handler))
                {
                    unit.OnDied -= handler;
                }
            }

            m_DeathHandlers.Clear();
            m_FactionBuckets.Clear();
            m_RegisteredFactions.Clear();
            m_Grid.Clear();
            m_Units.Clear();

            m_NearbyIdBuffer.Clear();
            m_CandidateBuffer.Clear();
            m_TickSnapshot.Clear();
        }

        // ---------------------------------------------------------------
        // 内部辅助
        // ---------------------------------------------------------------

        /// <summary>
        /// 转发某单位的死亡事件为 <see cref="OnUnitDied"/>。
        /// </summary>
        private void OnUnitModelDied(UnitModel unit, int sourceId)
        {
            Action<UnitWorld, UnitModel, int> died = OnUnitDied;
            if (died != null)
            {
                died(this, unit, sourceId);
            }
        }

        /// <summary>
        /// 退订指定单位的死亡事件并清除其委托缓存。
        /// </summary>
        private void UnsubscribeDeath(UnitModel unit)
        {
            if (m_DeathHandlers.TryGetValue(unit.Id, out Action<UnitModel, int> handler))
            {
                unit.OnDied -= handler;
                m_DeathHandlers.Remove(unit.Id);
            }
        }

        /// <summary>
        /// 把单位加入其登记时阵营的桶（桶不存在则创建），并缓存其登记阵营 Id
        /// 以便 <see cref="Remove"/> 时按登记阵营准确出桶。
        /// </summary>
        private void AddToFactionBucket(UnitModel unit)
        {
            int registeredFaction = unit.FactionId;
            if (!m_FactionBuckets.TryGetValue(registeredFaction, out List<UnitModel> bucket))
            {
                bucket = new List<UnitModel>();
                m_FactionBuckets[registeredFaction] = bucket;
            }

            bucket.Add(unit);
            m_RegisteredFactions[unit.Id] = registeredFaction;
        }

        /// <summary>
        /// 把单位从其<b>登记时阵营</b>（而非实时阵营）的桶移除；桶清空后一并删除该桶，保持字典紧凑。
        /// 同时清除该单位的登记阵营缓存。即便单位运行时改变了 <see cref="UnitModel.FactionId"/>，
        /// 也能从其原始登记桶中正确移除，不留僵尸引用。
        /// </summary>
        private void RemoveFromFactionBucket(UnitModel unit)
        {
            if (!m_RegisteredFactions.TryGetValue(unit.Id, out int registeredFaction))
            {
                return;
            }

            m_RegisteredFactions.Remove(unit.Id);

            if (m_FactionBuckets.TryGetValue(registeredFaction, out List<UnitModel> bucket))
            {
                bucket.Remove(unit);
                if (bucket.Count == 0)
                {
                    m_FactionBuckets.Remove(registeredFaction);
                }
            }
        }
    }
}
