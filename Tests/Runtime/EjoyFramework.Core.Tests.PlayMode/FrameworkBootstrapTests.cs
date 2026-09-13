//------------------------------------------------------------
// EjoyGame Framework Tests (PlayMode)
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EjoyFramework.Core.Coroutines;
using EjoyFramework.Core.Event;
using EjoyFramework.Core.Unity;

using EjoyFramework.Core;
namespace EjoyFramework.Tests.PlayMode
{
    /// <summary>
    /// 端到端 PlayMode 测试：验证 BaseComponent + EventComponent + CoroutineComponent
    /// 在真 Unity 生命周期中 Awake → Start → Update 流程完整工作。
    /// </summary>
    public sealed class FrameworkBootstrapTests : PlayModeTestBase
    {
        [UnityTest]
        public IEnumerator BaseComponent_Awake_MarksMainThread_AndStartsUpdating()
        {
            var go = CreateGameObject("Framework");
            var baseComp = go.AddComponent<BaseComponent>();

            // 等 Awake/Start 跑完
            yield return null;

            Assert.IsNotNull(baseComp);
            Assert.AreNotEqual(0, Framework.MainThreadId, "BaseComponent.Awake 必须调用 Framework.MarkMainThread()");

            // 再等几帧确认 Framework.Update 在被持续驱动（无异常）
            yield return WaitFrames(3);
        }

        [UnityTest]
        public IEnumerator EventComponent_OnGameObject_RegistersIntoFramework()
        {
            var go = CreateGameObject("Framework");
            go.AddComponent<BaseComponent>();
            go.AddComponent<EventComponent>();

            yield return null;   // 让 Awake/Start 跑完

            // EventManager 应该已可访问且能 Subscribe/Fire
            var em = Framework.GetModule<IEventManager>();
            Assert.IsNotNull(em);

            int handlerInvocations = 0;
            em.Subscribe(0xBEEF, (s, e) => handlerInvocations++);
            em.FireNow(this, EjoyFramework.Core.ReferencePool.Acquire<BootEvent>());

            Assert.AreEqual(1, handlerInvocations);
        }

        [UnityTest]
        public IEnumerator CoroutineManager_RunsRoutine_ThroughUnityHelper()
        {
            var go = CreateGameObject("Framework");
            go.AddComponent<BaseComponent>();
            go.AddComponent<CoroutineComponent>();

            yield return null;

            var cm = Framework.GetModule<ICoroutineManager>();
            Assert.IsNotNull(cm);
            Assert.AreEqual(0, cm.RunningCount);

            int ticks = 0;
            var handle = cm.Run(CountTicks(() => ticks++), "test", this);
            Assert.IsTrue(handle.IsValid);

            // 协程内 yield return null 5 次
            yield return WaitFrames(7);

            Assert.GreaterOrEqual(ticks, 5, "协程在 PlayMode 内应按帧推进");
        }

        private static IEnumerator CountTicks(System.Action onTick)
        {
            for (int i = 0; i < 5; i++)
            {
                onTick();
                yield return null;
            }
        }

        /// <summary>测试用事件，与生产事件 Id 冲突概率低（0xBEEF）。</summary>
        private sealed class BootEvent : GameEventArgs
        {
            public override int Id => 0xBEEF;
            public override void Clear() { }
        }
    }
}
