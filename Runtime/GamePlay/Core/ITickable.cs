//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay
{
    /// <summary>
    /// 由拥有者按帧驱动的可推进逻辑单元（owner-driven pump）。
    ///
    /// 设计意图：作为 GamePlay.Core 层的最小契约，让上层子系统（如 Combat 的技能系统 <c>AbilitySystem</c>）
    /// 以<b>可插拔组合</b>的方式接入基础单位（<c>UnitModel</c>）的逐帧推进，而无需让 Units 直接依赖 Combat。
    /// 一个单位可挂接零个或一个 <see cref="ITickable"/> 行为；挂接者由各自所属程序集提供并实现本接口。
    ///
    /// 非全局框架模块，不经 <c>Framework.GetModule</c> 获取；由拥有者持有并显式调用 <see cref="Tick(float)"/>。
    /// </summary>
    public interface ITickable
    {
        /// <summary>
        /// 推进一次逻辑时间。
        /// </summary>
        /// <param name="deltaTime">本次时间增量（秒）。</param>
        void Tick(float deltaTime);
    }
}
