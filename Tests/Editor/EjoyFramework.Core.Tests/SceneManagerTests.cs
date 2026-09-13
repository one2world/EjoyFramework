//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.Scene;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// SceneManager 基础回归测试。重点验证事件 args 字段被正确填充（Phase 0 修复）。
    /// 完整加载流程测试见 Phase 4.2。
    /// </summary>
    public class SceneManagerTests
    {
        private sealed class FakeSceneHelper : ISceneHelper
        {
            public Action<string, int, object, Action<float>, Action<string>, Action<string, string>> OnLoad;
            public Action<string, object, Action<string>, Action<string, string>> OnUnload;

            public void LoadAsync(string sceneAssetName, int priority, object userData,
                Action<float> onProgress, Action<string> onSuccess, Action<string, string> onFailure)
            {
                if (OnLoad != null) OnLoad(sceneAssetName, priority, userData, onProgress, onSuccess, onFailure);
            }

            public void UnloadAsync(string sceneAssetName, object userData,
                Action<string> onSuccess, Action<string, string> onFailure)
            {
                if (OnUnload != null) OnUnload(sceneAssetName, userData, onSuccess, onFailure);
            }
        }

        [Test]
        public void LoadScene_SuccessEventArgsFieldsFilled()
        {
            var sm = new SceneManager();
            var helper = new FakeSceneHelper();
            sm.SetHelper(helper);

            string capturedAsset = null;
            object capturedUserData = null;
            float capturedProgress = -1f;

            sm.LoadSceneUpdate += (s, e) =>
            {
                capturedProgress = e.Progress;
                Assert.AreEqual("Scenes/Main", e.SceneAssetName, "Update args should carry asset name");
            };
            sm.LoadSceneSuccess += (s, e) =>
            {
                capturedAsset = e.SceneAssetName;
                capturedUserData = e.UserData;
                Assert.GreaterOrEqual(e.Duration, 0f, "Duration should be non-negative");
            };

            Action<float> onProgress = null;
            Action<string> onSuccess = null;
            helper.OnLoad = (asset, prio, ud, p, s, f) =>
            {
                onProgress = p;
                onSuccess = s;
            };

            var userData = new object();
            sm.LoadScene("Scenes/Main", 0, userData);
            Assert.IsTrue(sm.SceneIsLoading("Scenes/Main"));

            onProgress(0.5f);
            Assert.AreEqual(0.5f, capturedProgress);

            onSuccess("Scenes/Main");
            Assert.AreEqual("Scenes/Main", capturedAsset);
            Assert.AreSame(userData, capturedUserData);
            Assert.IsTrue(sm.SceneIsLoaded("Scenes/Main"));
            Assert.IsFalse(sm.SceneIsLoading("Scenes/Main"));
        }

        [Test]
        public void LoadScene_FailureEventArgsFieldsFilled()
        {
            var sm = new SceneManager();
            var helper = new FakeSceneHelper();
            sm.SetHelper(helper);

            string capturedAsset = null;
            string capturedError = null;

            sm.LoadSceneFailure += (s, e) =>
            {
                capturedAsset = e.SceneAssetName;
                capturedError = e.ErrorMessage;
            };

            Action<string, string> onFailure = null;
            helper.OnLoad = (asset, prio, ud, p, s, f) => { onFailure = f; };

            sm.LoadScene("Scenes/Bad", 0, null);
            onFailure("Scenes/Bad", "asset not found");

            Assert.AreEqual("Scenes/Bad", capturedAsset);
            Assert.AreEqual("asset not found", capturedError);
            Assert.IsFalse(sm.SceneIsLoading("Scenes/Bad"));
            Assert.IsFalse(sm.SceneIsLoaded("Scenes/Bad"));
        }

        [Test]
        public void UnloadScene_SuccessEventArgsFieldsFilled()
        {
            var sm = new SceneManager();
            var helper = new FakeSceneHelper();
            sm.SetHelper(helper);

            // 先把场景加载到已加载状态
            Action<string> onLoadSuccess = null;
            helper.OnLoad = (asset, prio, ud, p, s, f) => { onLoadSuccess = s; };
            sm.LoadScene("Scenes/Main", 0, null);
            onLoadSuccess("Scenes/Main");

            string capturedAsset = null;
            object capturedUserData = null;
            sm.UnloadSceneSuccess += (s, e) =>
            {
                capturedAsset = e.SceneAssetName;
                capturedUserData = e.UserData;
            };

            Action<string> onUnloadSuccess = null;
            helper.OnUnload = (asset, ud, s, f) => { onUnloadSuccess = s; };

            var userData = new object();
            sm.UnloadScene("Scenes/Main", userData);
            Assert.IsTrue(sm.SceneIsUnloading("Scenes/Main"));
            onUnloadSuccess("Scenes/Main");

            Assert.AreEqual("Scenes/Main", capturedAsset);
            Assert.AreSame(userData, capturedUserData);
            Assert.IsFalse(sm.SceneIsLoaded("Scenes/Main"));
        }

        [Test]
        public void LoadScene_DuplicateThrows()
        {
            var sm = new SceneManager();
            sm.SetHelper(new FakeSceneHelper());

            sm.LoadScene("Scenes/A", 0, null);
            Assert.Throws<FrameworkException>(() => sm.LoadScene("Scenes/A", 0, null));
        }

        [Test]
        public void LoadScene_NullNameThrows()
        {
            var sm = new SceneManager();
            sm.SetHelper(new FakeSceneHelper());
            Assert.Throws<FrameworkException>(() => sm.LoadScene("", 0, null));
            Assert.Throws<FrameworkException>(() => sm.LoadScene(null, 0, null));
        }

        [Test]
        public void LoadScene_NoHelperThrows()
        {
            var sm = new SceneManager();
            Assert.Throws<FrameworkException>(() => sm.LoadScene("Scenes/A", 0, null));
        }

        [Test]
        public void UnloadScene_NotLoadedThrows()
        {
            var sm = new SceneManager();
            sm.SetHelper(new FakeSceneHelper());
            Assert.Throws<FrameworkException>(() => sm.UnloadScene("Scenes/Missing", null));
        }
    }
}
