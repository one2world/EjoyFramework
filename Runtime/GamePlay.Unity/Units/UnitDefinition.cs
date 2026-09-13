//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 数据驱动的单位模板。由游戏 / 注册表从数据表构建，
    /// 作为“类型”层描述一类单位的静态属性（资源、所属实体组、基础生命等），
    /// 并通过 <see cref="CreateData(int, Vector3, Quaternion, int)"/> 产出每次生成所需的 <see cref="UnitData"/>。
    /// </summary>
    /// <remarks>
    /// 刻意保持为简单数据持有者 + 一个工厂便捷方法，作为后续数据绑定（数据表 → 模板）的接缝；
    /// 不在此承载运行时行为。
    /// </remarks>
    public sealed class UnitDefinition
    {
        /// <summary>
        /// 模板标识，<see cref="UnitData.TypeId"/> 指向它。
        /// </summary>
        public int TypeId;

        /// <summary>
        /// 此模板生成单位的大类。
        /// </summary>
        public UnitKind Kind;

        /// <summary>
        /// 默认阵营标识；当 <see cref="CreateData"/> 未传入阵营覆盖时使用。
        /// </summary>
        public int DefaultFactionId;

        /// <summary>
        /// 实体资源名称（prefab 资源路径 / 资源键），传给 ShowEntity 作为 assetName。
        /// </summary>
        public string AssetName;

        /// <summary>
        /// 生成所属的实体组名称（须已通过 EntityComponent 注册）。
        /// </summary>
        public string EntityGroup;

        /// <summary>
        /// 基础最大生命值。<c>&lt;= 0</c> 表示该类单位默认不可被伤害（物件 / 机关）。
        /// </summary>
        public float BaseMaxHealth;

        /// <summary>
        /// 默认等级。
        /// </summary>
        public int DefaultLevel;

        /// <summary>
        /// 构造一个空模板，<see cref="DefaultLevel"/> 默认为 1。
        /// </summary>
        public UnitDefinition()
        {
            DefaultLevel = 1;
        }

        /// <summary>
        /// 构造一个单位模板。
        /// </summary>
        /// <param name="typeId">模板标识。</param>
        /// <param name="kind">单位大类。</param>
        /// <param name="assetName">实体资源名称。</param>
        /// <param name="entityGroup">实体组名称。</param>
        /// <param name="defaultFactionId">默认阵营标识，缺省 0。</param>
        /// <param name="baseMaxHealth">基础最大生命值，<c>&lt;= 0</c> 表示不可被伤害，缺省 0。</param>
        /// <param name="defaultLevel">默认等级，缺省 1。</param>
        public UnitDefinition(
            int typeId,
            UnitKind kind,
            string assetName,
            string entityGroup,
            int defaultFactionId = 0,
            float baseMaxHealth = 0f,
            int defaultLevel = 1)
        {
            TypeId = typeId;
            Kind = kind;
            AssetName = assetName;
            EntityGroup = entityGroup;
            DefaultFactionId = defaultFactionId;
            BaseMaxHealth = baseMaxHealth;
            DefaultLevel = defaultLevel;
        }

        /// <summary>
        /// 依据本模板产出一个 <see cref="UnitData"/> 生成载荷。
        /// </summary>
        /// <param name="unitId">本次生成的单位唯一标识。</param>
        /// <param name="position">生成位置（世界坐标）。</param>
        /// <param name="rotation">生成朝向；传入 <c>default</c> 时按 <see cref="UnityEngine.Quaternion.identity"/> 处理。</param>
        /// <param name="factionOverride">阵营覆盖；<c>&gt;= 0</c> 时覆盖 <see cref="DefaultFactionId"/>，
        /// 缺省 <c>-1</c> 表示沿用模板默认阵营。</param>
        /// <returns>填充完毕、可直接传给生成接口的载荷。</returns>
        public UnitData CreateData(int unitId, Vector3 position, Quaternion rotation = default, int factionOverride = -1)
        {
            int faction = factionOverride >= 0 ? factionOverride : DefaultFactionId;
            return new UnitData(
                unitId,
                TypeId,
                faction,
                Kind,
                position,
                rotation,
                BaseMaxHealth,
                DefaultLevel,
                null);
        }
    }
}
