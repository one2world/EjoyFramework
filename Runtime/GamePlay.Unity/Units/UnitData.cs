//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 单位生成载荷。作为 <c>userData</c> 直接透传给框架核心
    /// <see cref="EjoyFramework.Core.Entity.IEntityManager.ShowEntity(int, string, string, int, object)"/>
    /// （核心层不会像 <c>EntityComponent</c> 那样把它包进 <c>EntityData</c>），
    /// 因此 <see cref="UnitLogic.OnShow(object)"/> 中可直接 <c>userData as UnitData</c> 取回。
    /// </summary>
    /// <remarks>
    /// 这是一个普通数据类（POCO），不含任何行为；游戏 / 注册表按需填充后即可生成单位。
    /// 坐标使用 Unity 世界坐标（<see cref="UnityEngine.Vector3"/>）；映射到引擎无关
    /// <see cref="UnitModel"/> 的 2D 战斗平面 (X, Y) 由 <see cref="UnitLogic"/> 完成（世界 XZ → 模型 X,Y）。
    /// </remarks>
    public sealed class UnitData
    {
        /// <summary>
        /// 单位唯一标识。同时用作实体编号（<see cref="EjoyFramework.Core.Entity.IEntity.Id"/>）与
        /// <see cref="UnitModel.Id"/>，因此须在世界内唯一。
        /// </summary>
        public int UnitId;

        /// <summary>
        /// 单位模板标识（指向 <see cref="UnitDefinition.TypeId"/>），用于回查数据驱动配置。
        /// </summary>
        public int TypeId;

        /// <summary>
        /// 阵营标识，用于敌我判定。
        /// </summary>
        public int FactionId;

        /// <summary>
        /// 单位大类。
        /// </summary>
        public UnitKind Kind;

        /// <summary>
        /// 生成位置（Unity 世界坐标）。
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// 生成朝向（默认 <see cref="UnityEngine.Quaternion.identity"/>）。
        /// </summary>
        public Quaternion Rotation;

        /// <summary>
        /// 最大生命值。<c>&lt;= 0</c> 表示该单位不可被伤害（不会调用
        /// <see cref="UnitModel.ConfigureHealth(float, float)"/>，保持不可摧毁）。
        /// </summary>
        public float MaxHealth;

        /// <summary>
        /// 单位等级，供数值缩放等使用。
        /// </summary>
        public int Level;

        /// <summary>
        /// 业务自定义附加载荷（任意对象），框架不解释其含义。
        /// </summary>
        public object Payload;

        /// <summary>
        /// 构造一个空的生成载荷，<see cref="Rotation"/> 默认为 <see cref="UnityEngine.Quaternion.identity"/>，
        /// <see cref="Level"/> 默认为 1。
        /// </summary>
        public UnitData()
        {
            Rotation = Quaternion.identity;
            Level = 1;
        }

        /// <summary>
        /// 构造一个生成载荷。
        /// </summary>
        /// <param name="unitId">单位唯一标识。</param>
        /// <param name="typeId">单位模板标识。</param>
        /// <param name="factionId">阵营标识。</param>
        /// <param name="kind">单位大类，缺省 <see cref="UnitKind.Other"/>。</param>
        /// <param name="position">生成位置（世界坐标），缺省原点。</param>
        /// <param name="rotation">生成朝向；传入 <c>default</c> 时按 <see cref="UnityEngine.Quaternion.identity"/> 处理。</param>
        /// <param name="maxHealth">最大生命值，<c>&lt;= 0</c> 表示不可被伤害，缺省 0。</param>
        /// <param name="level">单位等级，缺省 1。</param>
        /// <param name="payload">业务自定义附加载荷，缺省 null。</param>
        public UnitData(
            int unitId,
            int typeId,
            int factionId,
            UnitKind kind = UnitKind.Other,
            Vector3 position = default,
            Quaternion rotation = default,
            float maxHealth = 0f,
            int level = 1,
            object payload = null)
        {
            UnitId = unitId;
            TypeId = typeId;
            FactionId = factionId;
            Kind = kind;
            Position = position;
            // Quaternion 的 default 是全零（非法旋转），统一回退到 identity。
            Rotation = (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
                ? Quaternion.identity
                : rotation;
            MaxHealth = maxHealth;
            Level = level;
            Payload = payload;
        }
    }
}
