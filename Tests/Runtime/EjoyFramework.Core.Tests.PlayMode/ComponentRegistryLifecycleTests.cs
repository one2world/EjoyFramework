//------------------------------------------------------------
// EjoyGame Framework Tests (PlayMode)
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Tests.PlayMode
{
    /// <summary>
    /// 回归测试：ComponentRegistry 在组件销毁（DontDestroyOnLoad 关闭时场景卸载、或框架重启）后
    /// 必须剔除失效实例，并允许重注册。修复前 s_ByType 残留已销毁 MonoBehaviour，
    /// GetComponent 永久返回失效旧实例、重注册因 "...is already exist." 失败。
    /// </summary>
    public sealed class ComponentRegistryLifecycleTests : PlayModeTestBase
    {
        [UnityTest]
        public IEnumerator DestroyedComponent_IsEvicted_AndGetComponentReturnsNull()
        {
            var go = CreateGameObject("Framework");
            go.AddComponent<BaseComponent>();
            var evt = go.AddComponent<EventComponent>();

            yield return null;   // 等 Awake 跑完，完成注册

            Assert.AreSame(evt, ComponentRegistry.GetComponent<EventComponent>(),
                "组件 Awake 后应可从注册表取回自身实例。");

            // 销毁组件（DestroyImmediate 同步触发 OnDestroy → UnregisterComponent）
            Object.DestroyImmediate(evt);

            Assert.IsNull(ComponentRegistry.GetComponent<EventComponent>(),
                "组件销毁后注册表必须返回 null，绝不能返回失效旧实例。");
        }

        [UnityTest]
        public IEnumerator AfterDestroy_ReRegistrationSucceeds()
        {
            var go = CreateGameObject("Framework");
            go.AddComponent<BaseComponent>();
            var first = go.AddComponent<EventComponent>();

            yield return null;

            Assert.AreSame(first, ComponentRegistry.GetComponent<EventComponent>());

            Object.DestroyImmediate(first);

            // 重注册：修复前死实例残留会让此处永久失败、GetComponent 持续返回旧实例。
            var second = go.AddComponent<EventComponent>();

            yield return null;

            var resolved = ComponentRegistry.GetComponent<EventComponent>();
            Assert.IsNotNull(resolved, "销毁后重注册的新组件必须可被取回。");
            Assert.AreSame(second, resolved, "注册表必须返回新实例，而非已销毁的旧实例。");
        }

        [UnityTest]
        public IEnumerator GetComponentByTypeName_SkipsDestroyedInstance()
        {
            var go = CreateGameObject("Framework");
            go.AddComponent<BaseComponent>();
            var evt = go.AddComponent<EventComponent>();

            yield return null;

            Assert.IsNotNull(ComponentRegistry.GetComponent(nameof(EventComponent)),
                "按类型名应能取回存活组件。");

            Object.DestroyImmediate(evt);

            Assert.IsNull(ComponentRegistry.GetComponent(nameof(EventComponent)),
                "按类型名访问已销毁组件必须返回 null。");
        }

        [UnityTest]
        public IEnumerator OptionalAndRequiredAccess_HaveExplicitMissingSemantics()
        {
            var go = CreateGameObject("Framework");
            go.AddComponent<BaseComponent>();
            var evt = go.AddComponent<EventComponent>();

            yield return null;

            Assert.IsTrue(GameEntry.TryGetComponent(out EventComponent optional));
            Assert.AreSame(evt, optional);
            Assert.AreSame(evt, GameEntry.RequireComponent<EventComponent>());

            Object.DestroyImmediate(evt);

            Assert.IsFalse(GameEntry.TryGetComponent(out optional));
            Assert.IsNull(optional);
            EjoyFramework.Core.FrameworkException exception = Assert.Throws<EjoyFramework.Core.FrameworkException>(
                () => GameEntry.RequireComponent<EventComponent>());
            StringAssert.Contains(typeof(EventComponent).FullName, exception.Message);
            StringAssert.Contains("TryGetComponent", exception.Message);
        }
    }
}
