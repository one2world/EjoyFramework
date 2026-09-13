//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 投射物的运动方式。
    /// </summary>
    public enum ProjectileMotion
    {
        /// <summary>
        /// 直线飞行：发射时一次性确定方向，之后沿固定方向匀速前进。
        /// </summary>
        Straight,

        /// <summary>
        /// 追踪飞行：每帧朝最近一次设置的目标位置前进（制导）。
        /// </summary>
        Homing
    }

    /// <summary>
    /// 投射物的引擎无关逻辑内核。负责运动（直线 / 追踪）、生命周期（寿命倒计时 + 强制销毁）
    /// 与穿透（命中去重 + 剩余穿透次数）。不引用任何 UnityEngine 类型，可独立单元测试。
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>
    /// 坐标使用 2D 战斗平面 (X, Y)；Unity 层负责把世界水平面 (XZ) 映射到 (X, Y)。
    /// </item>
    /// <item>
    /// <b>运动</b>：<see cref="ProjectileMotion.Straight"/> 由 <see cref="SetDirection(float, float)"/> 一次性设定
    /// 归一化方向，<see cref="Tick(float)"/> 沿该方向按 <c>Speed*dt</c> 前进；
    /// <see cref="ProjectileMotion.Homing"/> 在每次 <see cref="Tick(float)"/> 前由
    /// <see cref="SetTargetPosition(float, float)"/> 更新目标位置，<see cref="Tick(float)"/> 朝目标按 <c>Speed*dt</c> 逼近。
    /// 若 Homing 未设置过任何目标，则退化为静止（无方向，不移动）。
    /// </item>
    /// <item>
    /// <b>生命周期</b>：每次 <see cref="Tick(float)"/> 递减 <see cref="Lifetime"/>；当其穿越 ≤0（或调用
    /// <see cref="Kill"/>）时进入过期态，<see cref="OnExpired"/> 恰好触发一次。
    /// </item>
    /// <item>
    /// <b>穿透</b>：<see cref="RegisterHit(int)"/> 对同一目标去重（<see cref="HasHit(int)"/>），
    /// 命中新目标时若 <see cref="Pierce"/>&gt;0 则递减并继续飞行，否则返回应销毁。
    /// </item>
    /// <item>
    /// <b>单线程</b>：本类型不是线程安全的。热路径（<see cref="Tick(float)"/>）不产生堆分配。
    /// </item>
    /// </list>
    /// </remarks>
    public sealed class ProjectileModel
    {
        private float m_X;
        private float m_Y;

        private float m_DirX;
        private float m_DirY;
        private bool m_HasDirection;

        private float m_TargetX;
        private float m_TargetY;
        private bool m_HasTarget;

        private float m_Lifetime;
        private bool m_Killed;
        private bool m_ExpiredDispatched;

        // 命中去重集合：记录已命中过的目标 Id，避免对同一目标重复扣穿透 / 重复结算。
        private readonly HashSet<int> m_HitIds = new HashSet<int>();

        /// <summary>
        /// 构造一个投射物。
        /// </summary>
        /// <param name="x">初始 X 坐标（战斗平面）。</param>
        /// <param name="y">初始 Y 坐标（战斗平面）。</param>
        /// <param name="speed">飞行速度（单位 / 秒）。</param>
        /// <param name="motion">运动方式。</param>
        /// <param name="lifetime">初始寿命（秒），缺省 5f。</param>
        public ProjectileModel(float x, float y, float speed, ProjectileMotion motion, float lifetime = 5f)
        {
            m_X = x;
            m_Y = y;
            Speed = speed;
            Motion = motion;
            m_Lifetime = lifetime;
        }

        // ---------------------------------------------------------------
        // 空间与运动参数
        // ---------------------------------------------------------------

        /// <summary>
        /// 当前 X 坐标（战斗平面）。
        /// </summary>
        public float X
        {
            get { return m_X; }
        }

        /// <summary>
        /// 当前 Y 坐标（战斗平面）。
        /// </summary>
        public float Y
        {
            get { return m_Y; }
        }

        /// <summary>
        /// 飞行速度（单位 / 秒）。可运行时调整（如加速 / 减速）。
        /// </summary>
        public float Speed { get; set; }

        /// <summary>
        /// 运动方式（构造后不可变）。
        /// </summary>
        public ProjectileMotion Motion { get; }

        /// <summary>
        /// 命中半径：当投射物与目标的距离 ≤ 本值时判定为重叠命中。
        /// </summary>
        public float HitRadius { get; set; }

        /// <summary>
        /// 剩余可额外穿透的目标数（0 = 命中首个目标即销毁）。
        /// </summary>
        public int Pierce { get; set; }

        /// <summary>
        /// 剩余寿命（秒）。<see cref="Tick(float)"/> 会递减；亦可由外部直接续命 / 削减。
        /// </summary>
        public float Lifetime
        {
            get { return m_Lifetime; }
            set { m_Lifetime = value; }
        }

        /// <summary>
        /// 是否已过期：寿命 ≤ 0 或已被 <see cref="Kill"/>。过期后应被回收。
        /// </summary>
        public bool IsExpired
        {
            get { return m_Killed || m_Lifetime <= 0f; }
        }

        /// <summary>
        /// 投射物过期事件，恰好触发一次（无论是寿命耗尽还是 <see cref="Kill"/>）。
        /// </summary>
        public event Action<ProjectileModel> OnExpired;

        // ---------------------------------------------------------------
        // 方向 / 目标设置
        // ---------------------------------------------------------------

        /// <summary>
        /// 设置直线飞行方向（内部归一化）。用于 <see cref="ProjectileMotion.Straight"/>。
        /// 传入零向量时视为无方向（保持静止），不会产生 NaN。
        /// </summary>
        /// <param name="dx">方向 X 分量。</param>
        /// <param name="dy">方向 Y 分量。</param>
        public void SetDirection(float dx, float dy)
        {
            float sqr = dx * dx + dy * dy;
            if (sqr <= 0f)
            {
                m_HasDirection = false;
                m_DirX = 0f;
                m_DirY = 0f;
                return;
            }

            float inv = 1f / (float)Math.Sqrt(sqr);
            m_DirX = dx * inv;
            m_DirY = dy * inv;
            m_HasDirection = true;
        }

        /// <summary>
        /// 设置追踪目标位置。用于 <see cref="ProjectileMotion.Homing"/>，应在每次 <see cref="Tick(float)"/> 之前更新。
        /// </summary>
        /// <param name="x">目标 X 坐标。</param>
        /// <param name="y">目标 Y 坐标。</param>
        public void SetTargetPosition(float x, float y)
        {
            m_TargetX = x;
            m_TargetY = y;
            m_HasTarget = true;
        }

        // ---------------------------------------------------------------
        // 每帧推进
        // ---------------------------------------------------------------

        /// <summary>
        /// 推进一帧：按运动方式移动位置，并递减寿命；当寿命穿越 ≤0（或已 <see cref="Kill"/>）时
        /// 进入过期态并触发 <see cref="OnExpired"/> 恰好一次。已过期后再次调用为安全空操作（不重复移动 / 不重复触发）。
        /// </summary>
        /// <param name="deltaTime">本帧时间增量（秒）。</param>
        public void Tick(float deltaTime)
        {
            // 已过期则只确保事件已派发，不再移动。
            if (m_Killed || m_Lifetime <= 0f)
            {
                DispatchExpiredIfNeeded();
                return;
            }

            float step = Speed * deltaTime;
            if (step != 0f)
            {
                if (Motion == ProjectileMotion.Straight)
                {
                    if (m_HasDirection)
                    {
                        m_X += m_DirX * step;
                        m_Y += m_DirY * step;
                    }
                }
                else // Homing
                {
                    if (m_HasTarget)
                    {
                        float toX = m_TargetX - m_X;
                        float toY = m_TargetY - m_Y;
                        float sqr = toX * toX + toY * toY;
                        if (sqr > 0f)
                        {
                            float dist = (float)Math.Sqrt(sqr);
                            if (step >= dist)
                            {
                                // 不会越过目标：直接抵达。
                                m_X = m_TargetX;
                                m_Y = m_TargetY;
                            }
                            else
                            {
                                float inv = step / dist;
                                m_X += toX * inv;
                                m_Y += toY * inv;
                            }
                        }
                    }
                }
            }

            m_Lifetime -= deltaTime;
            if (m_Lifetime <= 0f)
            {
                m_Lifetime = 0f;
                DispatchExpiredIfNeeded();
            }
        }

        // ---------------------------------------------------------------
        // 命中检测
        // ---------------------------------------------------------------

        /// <summary>
        /// 返回到给定点的平方距离（避免开方，用于命中 / 排序的廉价比较）。
        /// </summary>
        /// <param name="x">目标 X 坐标。</param>
        /// <param name="y">目标 Y 坐标。</param>
        /// <returns>平方距离。</returns>
        public float SqrDistanceTo(float x, float y)
        {
            float dx = x - m_X;
            float dy = y - m_Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// 是否与给定点重叠（<see cref="SqrDistanceTo(float, float)"/> ≤ <see cref="HitRadius"/> 的平方）。
        /// </summary>
        /// <param name="x">目标 X 坐标。</param>
        /// <param name="y">目标 Y 坐标。</param>
        /// <returns>是否重叠。</returns>
        public bool Overlaps(float x, float y)
        {
            return SqrDistanceTo(x, y) <= HitRadius * HitRadius;
        }

        // ---------------------------------------------------------------
        // 穿透 / 命中登记
        // ---------------------------------------------------------------

        /// <summary>
        /// 是否已命中过给定目标（命中去重查询）。
        /// </summary>
        /// <param name="targetId">目标单位 Id。</param>
        /// <returns>之前是否已登记过该目标。</returns>
        public bool HasHit(int targetId)
        {
            return m_HitIds.Contains(targetId);
        }

        /// <summary>
        /// 登记一次对目标的命中并返回投射物是否应当销毁（穿透耗尽）。
        /// </summary>
        /// <remarks>
        /// 语义：
        /// <list type="bullet">
        /// <item>若已命中过该目标（<see cref="HasHit(int)"/>）：返回 <c>false</c>，不消耗穿透、不重复结算。</item>
        /// <item>否则记录该目标 Id；若 <see cref="Pierce"/>&gt;0 则递减并返回 <c>false</c>（继续飞行）；</item>
        /// <item>若 <see cref="Pierce"/>==0 则返回 <c>true</c>（应销毁）。</item>
        /// </list>
        /// </remarks>
        /// <param name="targetId">被命中的目标单位 Id。</param>
        /// <returns>是否应当销毁该投射物。</returns>
        public bool RegisterHit(int targetId)
        {
            if (m_HitIds.Contains(targetId))
            {
                return false;
            }

            m_HitIds.Add(targetId);

            if (Pierce > 0)
            {
                Pierce--;
                return false;
            }

            return true;
        }

        // ---------------------------------------------------------------
        // 强制过期
        // ---------------------------------------------------------------

        /// <summary>
        /// 强制立即过期（如命中实心障碍、被反制）。<see cref="OnExpired"/> 恰好触发一次。
        /// </summary>
        public void Kill()
        {
            if (m_Killed)
            {
                return;
            }

            m_Killed = true;
            m_Lifetime = 0f;
            DispatchExpiredIfNeeded();
        }

        // ---------------------------------------------------------------
        // 内部
        // ---------------------------------------------------------------

        /// <summary>
        /// 若尚未派发，则触发 <see cref="OnExpired"/> 恰好一次。
        /// </summary>
        private void DispatchExpiredIfNeeded()
        {
            if (m_ExpiredDispatched)
            {
                return;
            }

            m_ExpiredDispatched = true;
            Action<ProjectileModel> handler = OnExpired;
            if (handler != null)
            {
                handler(this);
            }
        }
    }
}
