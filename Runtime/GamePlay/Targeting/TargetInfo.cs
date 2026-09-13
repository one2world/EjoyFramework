//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Targeting
{
    /// <summary>
    /// 一个候选目标的只读快照视图。选择器（<see cref="TargetSelector"/>）依据这些字段进行决策。
    /// </summary>
    /// <remarks>
    /// 该结构体是不可变的值类型，按值传递、零分配，适合在每帧/每次查询时由游戏侧临时填充一份候选列表。
    /// 字段含义同时覆盖塔防（按路径进度选首/末目标）与动作/MOBA（按仇恨、优先级、血量选目标）两类场景。
    /// </remarks>
    public readonly struct TargetInfo
    {
        private readonly int m_Id;
        private readonly float m_X;
        private readonly float m_Y;
        private readonly float m_Health;
        private readonly float m_MaxHealth;
        private readonly float m_Threat;
        private readonly int m_Priority;
        private readonly float m_Progress;

        /// <summary>
        /// 构造一个候选目标快照。
        /// </summary>
        /// <param name="id">目标的唯一标识，用于确定性的平局裁决（取较小 Id）。</param>
        /// <param name="x">目标在世界中的 X 坐标。</param>
        /// <param name="y">目标在世界中的 Y 坐标。</param>
        /// <param name="health">当前血量。</param>
        /// <param name="maxHealth">最大血量，用于计算血量百分比。</param>
        /// <param name="threat">累计仇恨值，越高越容易被选中（配合 <see cref="TargetingStrategy.HighestThreat"/>）。</param>
        /// <param name="priority">优先级，越大越重要（例如 Boss）。</param>
        /// <param name="progress">沿路径已行进的进度（塔防中用于选取首/末目标）。</param>
        public TargetInfo(int id, float x, float y, float health, float maxHealth, float threat, int priority, float progress)
        {
            m_Id = id;
            m_X = x;
            m_Y = y;
            m_Health = health;
            m_MaxHealth = maxHealth;
            m_Threat = threat;
            m_Priority = priority;
            m_Progress = progress;
        }

        /// <summary>
        /// 目标的唯一标识。
        /// </summary>
        public int Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 目标在世界中的 X 坐标。
        /// </summary>
        public float X
        {
            get { return m_X; }
        }

        /// <summary>
        /// 目标在世界中的 Y 坐标。
        /// </summary>
        public float Y
        {
            get { return m_Y; }
        }

        /// <summary>
        /// 当前血量。
        /// </summary>
        public float Health
        {
            get { return m_Health; }
        }

        /// <summary>
        /// 最大血量。用于 <see cref="TargetingStrategy.LowestHealthPercent"/> 计算血量百分比。
        /// </summary>
        public float MaxHealth
        {
            get { return m_MaxHealth; }
        }

        /// <summary>
        /// 累计仇恨值，越高越容易被选中。
        /// </summary>
        public float Threat
        {
            get { return m_Threat; }
        }

        /// <summary>
        /// 优先级，越大越重要（例如 Boss）。
        /// </summary>
        public int Priority
        {
            get { return m_Priority; }
        }

        /// <summary>
        /// 沿路径已行进的进度（塔防中用于选取首/末目标）。
        /// </summary>
        public float Progress
        {
            get { return m_Progress; }
        }
    }
}
