//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Units.Movement
{
    /// <summary>
    /// 直线移动策略：朝单个目的地以恒定速度直线推进，进入到达阈值或一步即可越过时吸附到终点。
    /// 引擎无关、零分配、单线程。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>语义</b>：每步以 <c>Speed * deltaTime</c> 为移动预算朝目的地推进。
    /// 当剩余距离 ≤ 预算，或剩余距离 ≤ <see cref="ArriveThreshold"/> 时，吸附到目的地、
    /// 返回 <see cref="MoveStep.Arrived"/> = true，并清除目的地（<see cref="HasDestination"/> 变为 false）。
    /// </para>
    /// <para>
    /// 当 <see cref="HasDestination"/> 为 false 时，<see cref="Step(float, float, float)"/> 原样返回当前位置、
    /// <see cref="MoveStep.Arrived"/> = false、<see cref="MoveStep.DistanceMoved"/> = 0。
    /// </para>
    /// </remarks>
    public sealed class StraightMover : IMover
    {
        private float m_Speed;
        private float m_ArriveThreshold;

        private bool m_HasDestination;
        private float m_DestX;
        private float m_DestY;

        /// <summary>
        /// 构造一个直线移动器。
        /// </summary>
        /// <param name="speed">移动速度（距离/秒）。负值会被钳制为 0。</param>
        /// <param name="arriveThreshold">到达判定阈值（距离），缺省 0.05。负值会被钳制为 0。</param>
        public StraightMover(float speed, float arriveThreshold = 0.05f)
        {
            m_Speed = speed < 0f ? 0f : speed;
            m_ArriveThreshold = arriveThreshold < 0f ? 0f : arriveThreshold;
        }

        /// <summary>
        /// 移动速度（距离/秒）。赋负值会被钳制为 0。
        /// </summary>
        public float Speed
        {
            get { return m_Speed; }
            set { m_Speed = value < 0f ? 0f : value; }
        }

        /// <summary>
        /// 到达判定阈值（距离）。当与目的地的剩余距离 ≤ 该值时视为抵达。赋负值会被钳制为 0。
        /// </summary>
        public float ArriveThreshold
        {
            get { return m_ArriveThreshold; }
            set { m_ArriveThreshold = value < 0f ? 0f : value; }
        }

        /// <summary>
        /// 是否仍有目的地。<see cref="SetDestination(float, float)"/> 后为 true；抵达或 <see cref="Stop"/> 后为 false。
        /// </summary>
        public bool HasDestination
        {
            get { return m_HasDestination; }
        }

        /// <summary>
        /// 设置目的地并激活移动。
        /// </summary>
        /// <param name="x">目的地 X 坐标。</param>
        /// <param name="y">目的地 Y 坐标。</param>
        public void SetDestination(float x, float y)
        {
            m_DestX = x;
            m_DestY = y;
            m_HasDestination = true;
        }

        /// <summary>
        /// 朝目的地推进一步。详见类型说明的语义。
        /// </summary>
        /// <param name="currentX">当前 X 坐标。</param>
        /// <param name="currentY">当前 Y 坐标。</param>
        /// <param name="deltaTime">时间增量（秒）。</param>
        /// <returns>新位置、是否抵达，以及本步实际移动距离。</returns>
        public MoveStep Step(float currentX, float currentY, float deltaTime)
        {
            if (!m_HasDestination)
            {
                return new MoveStep(currentX, currentY, false, 0f);
            }

            float dx = m_DestX - currentX;
            float dy = m_DestY - currentY;
            float distance = (float)Math.Sqrt((dx * dx) + (dy * dy));

            float budget = m_Speed * deltaTime;
            if (budget < 0f)
            {
                budget = 0f;
            }

            // 剩余距离已在到达阈值内，或一步即可越过 → 吸附到目的地并标记抵达。
            if (distance <= m_ArriveThreshold || distance <= budget)
            {
                m_HasDestination = false;
                return new MoveStep(m_DestX, m_DestY, true, distance);
            }

            // 距离为 0 的兜底（已被上面的分支覆盖，此处仅防御除零）。
            if (distance <= 0f)
            {
                m_HasDestination = false;
                return new MoveStep(m_DestX, m_DestY, true, 0f);
            }

            float inv = budget / distance;
            float newX = currentX + (dx * inv);
            float newY = currentY + (dy * inv);
            return new MoveStep(newX, newY, false, budget);
        }

        /// <summary>
        /// 停止移动：清除目的地，使 <see cref="HasDestination"/> 变为 false。
        /// </summary>
        public void Stop()
        {
            m_HasDestination = false;
        }
    }
}
