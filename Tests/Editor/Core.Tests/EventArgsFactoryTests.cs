//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Scene;
using EjoyFramework.Core.Network;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// Phase 0 修复回归：场景与网络的 EventArgs Create 工厂必须正确填充所有字段，
    /// 历史 bug 是 Manager 用 ReferencePool.Acquire 后直接 Fire，字段全为 null/0。
    /// </summary>
    public class EventArgsFactoryTests
    {
        [Test]
        public void LoadSceneSuccess_FactoryFillsAllFields()
        {
            var userData = new object();
            var args = LoadSceneSuccessEventArgs.Create("Scenes/Main", 1.5f, userData);
            Assert.AreEqual("Scenes/Main", args.SceneAssetName);
            Assert.AreEqual(1.5f, args.Duration);
            Assert.AreSame(userData, args.UserData);
            ReferencePool.Release(args);
            // 释放后字段应清零（Clear 协议）
            Assert.IsNull(args.SceneAssetName);
            Assert.AreEqual(0f, args.Duration);
            Assert.IsNull(args.UserData);
        }

        [Test]
        public void LoadSceneFailure_FactoryFillsAllFields()
        {
            var args = LoadSceneFailureEventArgs.Create("Scenes/Main", "asset missing", null);
            Assert.AreEqual("Scenes/Main", args.SceneAssetName);
            Assert.AreEqual("asset missing", args.ErrorMessage);
            Assert.IsNull(args.UserData);
            ReferencePool.Release(args);
        }

        [Test]
        public void LoadSceneUpdate_FactoryFillsAllFields()
        {
            var args = LoadSceneUpdateEventArgs.Create("Scenes/Main", 0.42f, null);
            Assert.AreEqual("Scenes/Main", args.SceneAssetName);
            Assert.AreEqual(0.42f, args.Progress);
            ReferencePool.Release(args);
        }

        [Test]
        public void UnloadScene_FactoriesFillAllFields()
        {
            var ok = UnloadSceneSuccessEventArgs.Create("Scenes/Main", null);
            Assert.AreEqual("Scenes/Main", ok.SceneAssetName);
            ReferencePool.Release(ok);

            var fail = UnloadSceneFailureEventArgs.Create("Scenes/Main", null);
            Assert.AreEqual("Scenes/Main", fail.SceneAssetName);
            ReferencePool.Release(fail);
        }

        [Test]
        public void NetworkConnected_FactoryFillsAllFields()
        {
            var userData = new object();
            var args = NetworkConnectedEventArgs.Create(null, userData);
            Assert.IsNull(args.NetworkChannel);
            Assert.AreSame(userData, args.UserData);
            ReferencePool.Release(args);
            Assert.IsNull(args.UserData);
        }

        [Test]
        public void NetworkClosed_FactoryFillsAllFields()
        {
            var args = NetworkClosedEventArgs.Create(null);
            Assert.IsNull(args.NetworkChannel);
            ReferencePool.Release(args);
        }

        [Test]
        public void NetworkError_FactoryFillsAllFields()
        {
            var args = NetworkErrorEventArgs.Create(null, 42, "boom");
            Assert.AreEqual(42, args.ErrorCode);
            Assert.AreEqual("boom", args.ErrorMessage);
            ReferencePool.Release(args);
            Assert.AreEqual(0, args.ErrorCode);
            Assert.IsNull(args.ErrorMessage);
        }
    }
}
