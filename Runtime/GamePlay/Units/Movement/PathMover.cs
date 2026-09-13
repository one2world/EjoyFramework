//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Units.Movement
{
    /// <summary>
    /// 路径移动策略：依次沿一组路径点（<see cref="Waypoint"/>）以恒定速度移动，
    /// 进入路径点的到达阈值时消费该点并切换到下一个，越过最后一个路径点时标记抵达并停在终点。
    /// 引擎无关、零分配（每步不产生堆分配）、单线程。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>单步跨越多个短路径点</b>：单次 <see cref="Step(float, float, float)"/> 会在本步的移动预算
    /// （<c>Speed * deltaTime</c>）内循环消费多个相邻且很近的路径点，直到预算用尽、抵达最终点，
    /// 或当前点尚不可达为止。这样即便相邻路径点间距远小于一步的移动距离，单位也不会卡在原地，
    /// 而是平滑地连续穿过它们。返回的 <see cref="MoveStep.DistanceMoved"/> 为本步累计的真实移动距离。
    /// </para>
    /// <para>
    /// <b>到达判定</b>：当与当前路径点的剩余距离 ≤ <see cref="ArriveThreshold"/>，或一步即可越过时，
    /// 消费该点（<see cref="CurrentWaypointIndex"/> 前移）。消费掉最后一个路径点时，吸附到终点、
    /// 返回 <see cref="MoveStep.Arrived"/> = true，并清除路径（<see cref="HasDestination"/> 变为 false）。
    /// </para>
    /// <para>
    /// <b>空路径</b>：<see cref="SetPath"/> 传入 null 或空集合时不设置目的地（<see cref="HasDestination"/> 保持 false）。
    /// 无目的地时 <see cref="Step(float, float, float)"/> 原样返回当前位置、Arrived = false、DistanceMoved = 0。
    /// </para>
    /// </remarks>
    public sealed class PathMover : IMover
    {
        private float m_Speed;
        private float m_ArriveThreshold;

        private Waypoint[] m_Waypoints;
        private int m_Count;
        private int m_Index;
        private bool m_HasDestination;

        /// <summary>
        /// 构造一个路径移动器。
        /// </summary>
        /// <param name="speed">移动速度（距离/秒）。负值会被钳制为 0。</param>
        /// <param name="arriveThreshold">到达判定阈值（距离），缺省 0.05。负值会被钳制为 0。</param>
        public PathMover(float speed, float arriveThreshold = 0.05f)
        {
            m_Speed = speed < 0f ? 0f : speed;
            m_ArriveThreshold = arriveThreshold < 0f ? 0f : arriveThreshold;
            m_Waypoints = Array.Empty<Waypoint>();
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
        /// 到达判定阈值（距离）。当与当前路径点的剩余距离 ≤ 该值时视为抵达该点。赋负值会被钳制为 0。
        /// </summary>
        public float ArriveThreshold
        {
            get { return m_ArriveThreshold; }
            set { m_ArriveThreshold = value < 0f ? 0f : value; }
        }

        /// <summary>
        /// 是否仍有未走完的路径。设置非空路径后为 true；抵达终点或 <see cref="Stop"/> 后为 false。
        /// </summary>
        public bool HasDestination
        {
            get { return m_HasDestination; }
        }

        /// <summary>
        /// 当前正朝向的路径点索引（从 0 开始）。无路径时为 0。
        /// </summary>
        public int CurrentWaypointIndex
        {
            get { return m_Index; }
        }

        /// <summary>
        /// 当前路径包含的路径点数量。
        /// </summary>
        public int WaypointCount
        {
            get { return m_Count; }
        }

        /// <summary>
        /// 设置要行走的路径。会把传入的路径点<b>复制</b>到内部缓冲，并重置到第一个点。
        /// 传入 null 或空集合时清空路径且不设置目的地。
        /// </summary>
        /// <param name="waypoints">路径点序列（只读）。复制后调用方可自由修改原集合。</param>
        public void SetPath(IReadOnlyList<Waypoint> waypoints)
        {
            int count = waypoints == null ? 0 : waypoints.Count;
            if (count <= 0)
            {
                m_Count = 0;
                m_Index = 0;
                m_HasDestination = false;
                return;
            }

            // 仅在容量不足时扩容；否则复用既有缓冲，避免每次设路径都分配。
            if (m_Waypoints.Length < count)
            {
                m_Waypoints = new Waypoint[count];
            }

            for (int i = 0; i < count; i++)
            {
                m_Waypoints[i] = waypoints[i];
            }

            m_Count = count;
            m_Index = 0;
            m_HasDestination = true;
        }

        /// <summary>
        /// 朝当前路径点推进一步，必要时在本步预算内连续消费多个相邻的近路径点。详见类型说明的语义。
        /// </summary>
        /// <param name="currentX">当前 X 坐标。</param>
        /// <param name="currentY">当前 Y 坐标。</param>
        /// <param name="deltaTime">时间增量（秒）。</param>
        /// <returns>新位置、是否抵达终点，以及本步累计移动距离。</returns>
        public MoveStep Step(float currentX, float currentY, float deltaTime)
        {
            if (!m_HasDestination)
            {
                return new MoveStep(currentX, currentY, false, 0f);
            }

            float budget = m_Speed * deltaTime;
            if (budget < 0f)
            {
                budget = 0f;
            }

            float x = currentX;
            float y = currentY;
            float moved = 0f;

            // 在本步预算内尽可能多地消费近路径点。
            while (m_Index < m_Count)
            {
                Waypoint target = m_Waypoints[m_Index];
                float dx = target.X - x;
                float dy = target.Y - y;
                float distance = (float)Math.Sqrt((dx * dx) + (dy * dy));

                // 已处于当前点的到达阈值内：消费该点，不消耗预算。
                if (distance <= m_ArriveThreshold)
                {
                    if (ConsumeWaypoint(ref x, ref y, target))
                    {
                        return new MoveStep(x, y, true, moved);
                    }
                    continue;
                }

                // 剩余预算足以越过当前点：吸附到该点，扣除预算并消费它，继续看下一个点。
                if (distance <= budget)
                {
                    x = target.X;
                    y = target.Y;
                    moved += distance;
                    budget -= distance;

                    if (ConsumeWaypoint(ref x, ref y, target))
                    {
                        return new MoveStep(x, y, true, moved);
                    }
                    continue;
                }

                // 预算不足以抵达当前点：朝它推进剩余预算后结束本步。
                if (budget > 0f && distance > 0f)
                {
                    float inv = budget / distance;
                    x += dx * inv;
                    y += dy * inv;
                    moved += budget;
                }
                break;
            }

            return new MoveStep(x, y, false, moved);
        }

        /// <summary>
        /// 消费当前路径点（索引前移）。若消费的是最后一个点，则清除路径并返回 true 表示已抵达终点。
        /// </summary>
        /// <param name="x">引用：当前 X（保持为终点坐标）。</param>
        /// <param name="y">引用：当前 Y（保持为终点坐标）。</param>
        /// <param name="finalPoint">被消费的路径点（用于在抵达终点时吸附坐标）。</param>
        /// <returns>是否已抵达整条路径的终点。</returns>
        private bool ConsumeWaypoint(ref float x, ref float y, Waypoint finalPoint)
        {
            m_Index++;
            if (m_Index >= m_Count)
            {
                x = finalPoint.X;
                y = finalPoint.Y;
                m_HasDestination = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 停止移动：清空路径状态，使 <see cref="HasDestination"/> 变为 false。
        /// </summary>
        public void Stop()
        {
            m_Count = 0;
            m_Index = 0;
            m_HasDestination = false;
        }
    }
}
