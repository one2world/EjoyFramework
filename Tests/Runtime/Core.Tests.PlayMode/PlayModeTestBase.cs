//------------------------------------------------------------
// EjoyGame Framework Tests (PlayMode)
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

using EjoyFramework.Core;
namespace EjoyFramework.Tests.PlayMode
{
    /// <summary>
    /// PlayMode 测试基类（Phase 17 Part 3）。
    /// 提供公共 Setup/Teardown：
    ///   - 创建临时场景 + 一个 GameObject 持 BaseComponent
    ///   - 每个测试自动重置 Framework 静态状态（避免测试间污染）
    ///   - 提供 helper：CreateGameObject / WaitFor
    ///
    /// 子类示例：
    /// <code>
    /// public sealed class MyTests : PlayModeTestBase
    /// {
    ///     [UnityTest]
    ///     public IEnumerator MyTest()
    ///     {
    ///         var go = CreateGameObject("MyComp");
    ///         var comp = go.AddComponent<MyComponent>();
    ///         yield return null;     // 等一帧让 Awake/Start 跑完
    ///         Assert.IsTrue(comp.IsReady);
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class PlayModeTestBase
    {
        protected UnityScene m_TestScene;
        private GameObject m_Root;

        [NUnit.Framework.SetUp]
        public virtual void SetUp()
        {
            // Framework 静态状态可能被上一个测试污染，重置干净
            Framework.ResetForEnterPlayMode();

            // 用代码创建场景，避免依赖 .unity 资产
            m_TestScene = SceneManager.CreateScene("EjoyFramework_PlayMode_Test_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            SceneManager.SetActiveScene(m_TestScene);

            m_Root = new GameObject("[PlayModeTestRoot]");
            SceneManager.MoveGameObjectToScene(m_Root, m_TestScene);
        }

        [NUnit.Framework.TearDown]
        public virtual void TearDown()
        {
            // 卸载临时场景；framework 模块顺势 Shutdown
            try { Framework.Shutdown(); } catch { }

            if (m_TestScene.IsValid())
            {
                var op = SceneManager.UnloadSceneAsync(m_TestScene);
                // 仅 best-effort；NUnit TearDown 不支持 IEnumerator
            }
            m_Root = null;
        }

        /// <summary>在测试 root 下创建子 GameObject。</summary>
        protected GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_Root.transform, /*worldPositionStays*/ false);
            return go;
        }

        /// <summary>等 N 帧（PlayMode 常用 helper）。</summary>
        protected static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        /// <summary>等到条件成立或超时（默认 5 秒）。</summary>
        protected static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds = 5f)
        {
            float elapsed = 0f;
            while (!condition())
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                if (elapsed > timeoutSeconds)
                {
                    throw new System.TimeoutException("WaitUntil timed out after " + timeoutSeconds + "s");
                }
            }
        }
    }
}
