//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 随机源抽象。所有抽奖/掉落逻辑均通过该接口取随机数，
    /// 从而把随机性从业务逻辑中解耦，使结果可被脚本化、可重放、可单元测试。
    /// </summary>
    /// <remarks>
    /// 该接口故意不依赖任何引擎类型（无 UnityEngine.Random），实现可以包装
    /// <see cref="System.Random"/>，也可以包装一个固定序列以便在测试中精确断言结果。
    /// </remarks>
    public interface IRandomSource
    {
        /// <summary>
        /// 返回区间 <c>[0, maxExclusive)</c> 内的非负整数。
        /// </summary>
        /// <param name="maxExclusive">上界（不含）。约定取值应为正数。</param>
        /// <returns>落在 <c>[0, maxExclusive)</c> 的整数。</returns>
        int NextInt(int maxExclusive);

        /// <summary>
        /// 返回区间 <c>[0.0, 1.0)</c> 内的双精度浮点数。
        /// </summary>
        /// <returns>落在 <c>[0.0, 1.0)</c> 的浮点数。</returns>
        double NextDouble();
    }
}
