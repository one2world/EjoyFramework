//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Factions
{
    /// <summary>
    /// 基于整型阵营 Id 的成对（pairwise）队伍关系注册表，关系是对称的。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 关系按 (a, b) 这一对来存储，并归一化为 (min, max)，因此 (a, b) 与 (b, a) 共享同一条记录，
    /// 保证 <see cref="SetRelation"/> 与 <see cref="GetRelation"/> 天然对称。
    /// </para>
    /// <para>
    /// 关系解析顺序（<see cref="GetRelation"/>）：
    /// <list type="number">
    /// <item>若该（归一化后的）对被显式 <see cref="SetRelation"/> 过，则返回显式设置的关系；</item>
    /// <item>否则若 <c>a == b</c>（同一阵营），默认返回 <see cref="FactionRelation.Ally"/>（阵营默认与自身结盟）；</item>
    /// <item>否则返回 <see cref="DefaultBetweenDifferent"/>（不同阵营之间的默认关系）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 显式覆盖对同 Id 对同样生效，因此“大混战（FFA）”阵营可通过 <c>SetRelation(x, x, Enemy)</c>
    /// 让自身成员互相敌对。
    /// </para>
    /// </remarks>
    public sealed class FactionRelations : FrameworkModule, IFactionRelations
    {
        /// <summary>
        /// 归一化后的阵营对（min, max），作为字典键。值类型，避免装箱。
        /// </summary>
        private readonly struct Pair : System.IEquatable<Pair>
        {
            public readonly int Low;
            public readonly int High;

            public Pair(int a, int b)
            {
                if (a <= b)
                {
                    Low = a;
                    High = b;
                }
                else
                {
                    Low = b;
                    High = a;
                }
            }

            public bool Equals(Pair other)
            {
                return Low == other.Low && High == other.High;
            }

            public override bool Equals(object obj)
            {
                return obj is Pair other && Equals(other);
            }

            public override int GetHashCode()
            {
                // 经典的成对哈希混合，足够均匀且无分配。
                unchecked
                {
                    return (Low * 397) ^ High;
                }
            }
        }

        private readonly Dictionary<Pair, FactionRelation> m_Overrides;
        private FactionRelation m_DefaultBetweenDifferent;

        /// <summary>
        /// 构造一个阵营关系注册表，不同阵营之间默认敌对。
        /// </summary>
        /// <remarks>
        /// 框架解析器（<see cref="Framework.GetModule{T}"/>）经 <c>Activator.CreateInstance</c>
        /// 需要一个真正的无参构造函数；如需自定义不同阵营默认关系，构造后调用
        /// <see cref="SetDefaultBetweenDifferent"/> 或设置 <see cref="DefaultBetweenDifferent"/>。
        /// </remarks>
        public FactionRelations()
        {
            m_DefaultBetweenDifferent = FactionRelation.Enemy;
            m_Overrides = new Dictionary<Pair, FactionRelation>();
        }

        /// <summary>
        /// 以指定的“不同阵营默认关系”构造一个阵营关系注册表（程序集内便捷构造，供测试与本地构建使用）。
        /// </summary>
        /// <param name="defaultBetweenDifferent">不同阵营之间在未显式设置时的默认关系。</param>
        internal FactionRelations(FactionRelation defaultBetweenDifferent)
        {
            m_DefaultBetweenDifferent = defaultBetweenDifferent;
            m_Overrides = new Dictionary<Pair, FactionRelation>();
        }

        /// <summary>
        /// 注入“不同阵营之间未显式设置时的默认关系”。与核心模块 SetHelper/SetLoader 的注入风格一致，
        /// 便于 <see cref="Framework.GetModule{T}"/> 拿到实例后再行配置。
        /// </summary>
        /// <param name="defaultBetweenDifferent">不同阵营之间的默认关系。</param>
        public void SetDefaultBetweenDifferent(FactionRelation defaultBetweenDifferent)
        {
            m_DefaultBetweenDifferent = defaultBetweenDifferent;
        }

        /// <summary>
        /// 不同阵营之间在未显式设置时的默认关系。修改该值不会影响已显式设置的对。
        /// </summary>
        public FactionRelation DefaultBetweenDifferent
        {
            get { return m_DefaultBetweenDifferent; }
            set { m_DefaultBetweenDifferent = value; }
        }

        /// <summary>
        /// 显式设置一对阵营之间的关系（对称：<c>(a, b)</c> 与 <c>(b, a)</c> 等价）。
        /// </summary>
        /// <param name="factionA">阵营 A 的 Id。</param>
        /// <param name="factionB">阵营 B 的 Id。</param>
        /// <param name="relation">要设置的关系。</param>
        public void SetRelation(int factionA, int factionB, FactionRelation relation)
        {
            m_Overrides[new Pair(factionA, factionB)] = relation;
        }

        /// <summary>
        /// 查询一对阵营之间的关系，按“显式覆盖 → 同 Id 默认友方 → 不同 Id 默认值”的顺序解析。
        /// </summary>
        /// <param name="factionA">阵营 A 的 Id。</param>
        /// <param name="factionB">阵营 B 的 Id。</param>
        /// <returns>解析得到的关系。</returns>
        public FactionRelation GetRelation(int factionA, int factionB)
        {
            if (m_Overrides.TryGetValue(new Pair(factionA, factionB), out FactionRelation relation))
            {
                return relation;
            }

            if (factionA == factionB)
            {
                return FactionRelation.Ally;
            }

            return m_DefaultBetweenDifferent;
        }

        /// <summary>
        /// 判断两个阵营是否敌对。
        /// </summary>
        /// <param name="a">阵营 A 的 Id。</param>
        /// <param name="b">阵营 B 的 Id。</param>
        /// <returns>关系为 <see cref="FactionRelation.Enemy"/> 时返回 <c>true</c>。</returns>
        public bool AreEnemies(int a, int b)
        {
            return GetRelation(a, b) == FactionRelation.Enemy;
        }

        /// <summary>
        /// 判断两个阵营是否友方。
        /// </summary>
        /// <param name="a">阵营 A 的 Id。</param>
        /// <param name="b">阵营 B 的 Id。</param>
        /// <returns>关系为 <see cref="FactionRelation.Ally"/> 时返回 <c>true</c>。</returns>
        public bool AreAllies(int a, int b)
        {
            return GetRelation(a, b) == FactionRelation.Ally;
        }

        /// <summary>
        /// 判断两个阵营是否中立。
        /// </summary>
        /// <param name="a">阵营 A 的 Id。</param>
        /// <param name="b">阵营 B 的 Id。</param>
        /// <returns>关系为 <see cref="FactionRelation.Neutral"/> 时返回 <c>true</c>。</returns>
        public bool AreNeutral(int a, int b)
        {
            return GetRelation(a, b) == FactionRelation.Neutral;
        }

        /// <summary>
        /// 移除一对阵营的显式覆盖，使其回落到默认解析规则。
        /// </summary>
        /// <param name="a">阵营 A 的 Id。</param>
        /// <param name="b">阵营 B 的 Id。</param>
        /// <returns>存在并成功移除一条显式覆盖时返回 <c>true</c>；本就没有覆盖则返回 <c>false</c>。</returns>
        public bool ClearRelation(int a, int b)
        {
            return m_Overrides.Remove(new Pair(a, b));
        }

        /// <summary>
        /// 清空所有显式覆盖（不影响 <see cref="DefaultBetweenDifferent"/> 与同 Id 默认友方规则）。
        /// </summary>
        public void Clear()
        {
            m_Overrides.Clear();
        }

        /// <summary>
        /// 获取游戏框架模块优先级。阵营关系为纯注册表，无顺序敏感性，取默认 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 游戏框架模块轮询。阵营关系注册表无逐帧逻辑，空实现。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒）。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理模块：移除全部显式覆盖，并将不同阵营默认关系复位为 <see cref="FactionRelation.Enemy"/>。
        /// </summary>
        public override void Shutdown()
        {
            m_Overrides.Clear();
            m_DefaultBetweenDifferent = FactionRelation.Enemy;
        }
    }
}
