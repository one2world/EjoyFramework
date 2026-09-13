//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Units.Movement;
using UnityEngine;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 把引擎无关的 <see cref="IMover"/> 移动策略应用到 <see cref="Transform"/> 的薄 MonoBehaviour。
    /// 使用世界水平面 (X, Z) 作为 2D 移动平面（<c>currentX = pos.x</c>、<c>currentY = pos.z</c>），
    /// 并在写回时保留世界 Y（高度）不变。
    /// </summary>
    /// <remarks>
    /// 通过 <see cref="MoveTo(Vector3)"/> 设置直线目标，或 <see cref="FollowPath(IReadOnlyList{Vector3})"/> 设置路径。
    /// 也可直接给 <see cref="Mover"/> 赋任意 <see cref="IMover"/> 实现。抵达终点时触发一次 <see cref="OnArrived"/>。
    /// </remarks>
    public sealed class TransformMover : MonoBehaviour
    {
        private List<Waypoint> m_PathBuffer;

        /// <summary>
        /// 当前驱动本 Transform 的移动策略。为 null 或其 <see cref="IMover.HasDestination"/> 为 false 时不移动。
        /// </summary>
        public IMover Mover { get; set; }

        /// <summary>
        /// 抵达最终目的地时触发一次（在写回 Transform 之后）。
        /// </summary>
        public event Action OnArrived;

        /// <summary>
        /// 设置一个直线移动目标。会替换 <see cref="Mover"/> 为 <see cref="StraightMover"/>
        /// （沿用既有速度，否则默认 5）。取世界坐标的 (X, Z) 作为 2D 目标。
        /// </summary>
        /// <param name="worldPos">世界坐标目标点（仅使用 X、Z 分量）。</param>
        public void MoveTo(Vector3 worldPos)
        {
            StraightMover straight = Mover as StraightMover;
            if (straight == null)
            {
                straight = new StraightMover(Mover != null ? Mover.Speed : 5f);
                Mover = straight;
            }

            straight.SetDestination(worldPos.x, worldPos.z);
        }

        /// <summary>
        /// 设置一条路径。会替换 <see cref="Mover"/> 为 <see cref="PathMover"/>
        /// （沿用既有速度，否则默认 5）。每个世界点取 (X, Z) 作为 2D 路径点。
        /// </summary>
        /// <param name="worldPoints">世界坐标路径点序列（仅使用各点的 X、Z 分量）；null 或空则清空目的地。</param>
        public void FollowPath(IReadOnlyList<Vector3> worldPoints)
        {
            PathMover path = Mover as PathMover;
            if (path == null)
            {
                path = new PathMover(Mover != null ? Mover.Speed : 5f);
                Mover = path;
            }

            if (m_PathBuffer == null)
            {
                m_PathBuffer = new List<Waypoint>();
            }
            m_PathBuffer.Clear();

            if (worldPoints != null)
            {
                for (int i = 0; i < worldPoints.Count; i++)
                {
                    Vector3 p = worldPoints[i];
                    m_PathBuffer.Add(new Waypoint(p.x, p.z));
                }
            }

            path.SetPath(m_PathBuffer);
        }

        /// <summary>
        /// 每帧推进：若有目的地，则用当前世界位置的 (X, Z) 调用 <see cref="IMover.Step"/>，
        /// 并把结果写回 <see cref="Transform.position"/>（保留世界 Y）。抵达时触发 <see cref="OnArrived"/>。
        /// </summary>
        private void Update()
        {
            IMover mover = Mover;
            if (mover == null || !mover.HasDestination)
            {
                return;
            }

            Vector3 pos = transform.position;
            MoveStep step = mover.Step(pos.x, pos.z, Time.deltaTime);
            transform.position = new Vector3(step.X, pos.y, step.Y);

            if (step.Arrived)
            {
                Action handler = OnArrived;
                if (handler != null)
                {
                    handler();
                }
            }
        }
    }
}
