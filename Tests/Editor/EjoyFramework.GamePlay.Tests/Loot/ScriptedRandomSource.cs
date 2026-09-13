//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Loot;

namespace EjoyFramework.GamePlay.Tests.Loot
{
    /// <summary>
    /// 测试用的脚本化随机源：按入队顺序返回预先排好的 double / int 序列，
    /// 使所有掉落/抽卡结果可被精确断言。队列耗尽后返回 0，便于测试中安全兜底。
    /// </summary>
    internal sealed class ScriptedRandomSource : IRandomSource
    {
        private readonly Queue<double> m_Doubles;
        private readonly Queue<int> m_Ints;

        /// <summary>
        /// 构造一个脚本化随机源。
        /// </summary>
        /// <param name="doubles"><see cref="NextDouble"/> 的返回序列。</param>
        /// <param name="ints"><see cref="NextInt"/> 的返回序列。</param>
        public ScriptedRandomSource(IEnumerable<double> doubles = null, IEnumerable<int> ints = null)
        {
            m_Doubles = doubles != null ? new Queue<double>(doubles) : new Queue<double>();
            m_Ints = ints != null ? new Queue<int>(ints) : new Queue<int>();
        }

        /// <summary>
        /// 入队一个 <see cref="NextDouble"/> 将返回的值，便于链式构造。
        /// </summary>
        public ScriptedRandomSource EnqueueDouble(double value)
        {
            m_Doubles.Enqueue(value);
            return this;
        }

        /// <summary>
        /// 入队一个 <see cref="NextInt"/> 将返回的值，便于链式构造。
        /// </summary>
        public ScriptedRandomSource EnqueueInt(int value)
        {
            m_Ints.Enqueue(value);
            return this;
        }

        /// <summary>
        /// 返回脚本化整数；队列耗尽时返回 0。
        /// </summary>
        public int NextInt(int maxExclusive)
        {
            if (m_Ints.Count == 0)
            {
                return 0;
            }

            return m_Ints.Dequeue();
        }

        /// <summary>
        /// 返回脚本化浮点数；队列耗尽时返回 0.0。
        /// </summary>
        public double NextDouble()
        {
            if (m_Doubles.Count == 0)
            {
                return 0d;
            }

            return m_Doubles.Dequeue();
        }
    }
}
