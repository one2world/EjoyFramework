//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Units.Movement
{
    /// <summary>
    /// 引擎无关的 2D 移动策略抽象。给定当前位置与时间增量，按 <see cref="Speed"/> 向目标推进一步，
    /// 返回新位置与是否抵达。实现不引用任何 UnityEngine 类型，可单元测试、可在服务端模拟。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>坐标约定</b>：使用 2D 平面 (X, Y)。Unity 层（<c>TransformMover</c>）负责把世界水平面 (XZ)
    /// 映射到 (X, Y)，即 <c>currentX = pos.x</c>、<c>currentY = pos.z</c>，并保留世界 Y（高度）不变。
    /// </para>
    /// <para>
    /// <b>单线程</b>：实现不保证线程安全；所有访问应发生在同一逻辑线程。
    /// 热路径（<see cref="Step(float, float, float)"/>）不产生堆分配，也不使用 LINQ。
    /// </para>
    /// </remarks>
    public interface IMover
    {
        /// <summary>
        /// 是否仍有未抵达的目的地 / 路径。<see cref="Stop"/> 之后或抵达终点后为 false。
        /// </summary>
        bool HasDestination { get; }

        /// <summary>
        /// 移动速度（单位：距离/秒）。可在运行时改写。
        /// </summary>
        float Speed { get; set; }

        /// <summary>
        /// 从 (currentX, currentY) 出发，按 <c>Speed * deltaTime</c> 的预算朝目的地推进一步。
        /// </summary>
        /// <param name="currentX">当前 X 坐标。</param>
        /// <param name="currentY">当前 Y 坐标。</param>
        /// <param name="deltaTime">本次时间增量（秒）。</param>
        /// <returns>新位置、是否抵达，以及本步实际移动距离。</returns>
        MoveStep Step(float currentX, float currentY, float deltaTime);

        /// <summary>
        /// 停止移动：清除目的地 / 路径，使 <see cref="HasDestination"/> 变为 false。
        /// </summary>
        void Stop();
    }
}
