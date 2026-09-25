//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using UnityEngine.TestTools.Constraints;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 零分配断言：先把同一个委托执行若干遍（Mono 下首次执行的 JIT / 泛型实例化 / 线程级池初始化会被计为 GC.Alloc），
    /// 再用 Unity 的 GC.Alloc recorder 断言。
    /// </summary>
    internal static class ZeroAlloc
    {
        public static void Assert(TestDelegate body)
        {
            for (int i = 0; i < 32; i++)
            {
                body();
            }

            NUnit.Framework.Assert.That(body, NUnit.Framework.Is.Not.AllocatingGCMemory());
        }
    }
}
