//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.GamePlay.Netcode.Session;
using EjoyFramework.GamePlay.Netcode.Sync;
using EjoyFramework.GamePlay.Netcode.Transport;

namespace EjoyFramework.GamePlay.Tests.Netcode.Session
{
    /// <summary>
    /// 验证 <see cref="NetcodeServer"/> / <see cref="NetcodeClient"/> 作为框架模块的可解析性与配置契约：
    /// <list type="bullet">
    /// <item>经 <see cref="Framework.GetModule{T}"/> 以 <see cref="INetcodeServer"/> / <see cref="INetcodeClient"/> 懒加载解析。</item>
    /// <item>无参构造 + <c>SetTransport</c> 设值注入后可跑通完整握手 / 权威输入 / 远端插值。</item>
    /// <item>未注入传输时 <see cref="FrameworkModule.IsModuleConfigured"/> 为 false，注入后为 true。</item>
    /// </list>
    /// 全程跑在确定性 <see cref="LoopbackTransport"/> 上（无套接字、无线程、无真实时间）。
    /// </summary>
    public class NetcodeModuleResolveTests
    {
        private const float Delta = 1e-3f;

        // 共享的确定性二维 mover。容忍 null/短数组以避免越界。
        private static readonly Func<float[], float[], float, float[]> MoverStep = (state, input, dt) =>
        {
            float sx = state != null && state.Length > 0 ? state[0] : 0f;
            float sy = state != null && state.Length > 1 ? state[1] : 0f;
            float ix = input != null && input.Length > 0 ? input[0] : 0f;
            float iy = input != null && input.Length > 1 ? input[1] : 0f;
            return new[] { sx + ix * dt, sy + iy * dt };
        };

        [SetUp]
        public void SetUp()
        {
            // 注册 Netcode 模块工厂：EditMode 下 [RuntimeInitializeOnLoadMethod] bootstrap 不触发，
            // 而模块解析已移除反射兜底，必须显式登记生成的工厂后 GetModule 才能解析。RegisterAll 幂等。
            EjoyFramework.GamePlay.Netcode.Generated.FrameworkModuleRegistrations.RegisterAll();
        }

        [TearDown]
        public void TearDown()
        {
            // 清空全局模块表与 per-T 缓存，避免本测试懒加载的模块泄漏到其他用例。
            Framework.Shutdown();
        }

        private static void Drain(INetcodeServer server, INetcodeClient client, int rounds = 4)
        {
            for (int i = 0; i < rounds; i++)
            {
                client.Poll();
                server.Poll();
            }
        }

        // —— 1) 模块经生成工厂（RegisterFactory）解析 ——

        [Test]
        public void GetModule_ResolvesServerAndClientImplementations()
        {
            INetcodeServer server = Framework.GetModule<INetcodeServer>();
            INetcodeClient client = Framework.GetModule<INetcodeClient>();

            Assert.IsNotNull(server, "INetcodeServer 应解析为 NetcodeServer 实例。");
            Assert.IsNotNull(client, "INetcodeClient 应解析为 NetcodeClient 实例。");
            Assert.IsInstanceOf<NetcodeServer>(server);
            Assert.IsInstanceOf<NetcodeClient>(client);

            // 同一接口连续解析返回缓存的同一实例。
            Assert.AreSame(server, Framework.GetModule<INetcodeServer>());
            Assert.AreSame(client, Framework.GetModule<INetcodeClient>());
        }

        // —— 2) 配置契约：注入前未配置，注入后已配置 ——

        [Test]
        public void Modules_RequireTransportConfiguration()
        {
            FrameworkModule server = (FrameworkModule)Framework.GetModule<INetcodeServer>();
            FrameworkModule client = (FrameworkModule)Framework.GetModule<INetcodeClient>();

            Assert.IsTrue(server.RequiresConfiguration, "NetcodeServer 应声明需要配置。");
            Assert.IsTrue(client.RequiresConfiguration, "NetcodeClient 应声明需要配置。");
            Assert.IsFalse(server.IsModuleConfigured, "未注入传输前 NetcodeServer 不应视为已配置。");
            Assert.IsFalse(client.IsModuleConfigured, "未注入传输前 NetcodeClient 不应视为已配置。");

            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            ((INetcodeServer)server).SetTransport(serverTransport);
            ((INetcodeClient)client).SetTransport(clientTransport);

            Assert.IsTrue(server.IsModuleConfigured, "注入传输后 NetcodeServer 应视为已配置。");
            Assert.IsTrue(client.IsModuleConfigured, "注入传输后 NetcodeClient 应视为已配置。");
        }

        // —— 3) 设值注入后跑通端到端 ——

        [Test]
        public void SetterInjected_Modules_RunEndToEnd()
        {
            INetcodeServer server = Framework.GetModule<INetcodeServer>();
            INetcodeClient client = Framework.GetModule<INetcodeClient>();

            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();

            server.SetTransport(serverTransport);
            server.SetSimulateStep(MoverStep);
            server.SetTickRate(30);

            client.SetTransport(clientTransport);
            client.SetSimulateStep(MoverStep);
            client.SetInterpolationDelay(0.1);

            int conn = -1;
            server.OnClientJoined += c => conn = c;
            bool connected = false;
            client.OnConnected += () => connected = true;

            server.Start(0);
            client.Connect("loopback", 0);
            Drain(server, client);

            Assert.GreaterOrEqual(conn, 0, "服务器应感知到客户端加入。");

            const int localEntity = 1;
            const int remoteEntity = 99;
            server.RegisterEntity(localEntity, new[] { 0f, 0f });
            server.RegisterEntity(remoteEntity, new[] { 0f, 0f });
            server.AssignClientEntity(conn, localEntity);
            Drain(server, client);

            Assert.IsTrue(connected, "客户端应在收到 WelcomeMessage 后触发 OnConnected。");
            Assert.AreEqual(localEntity, client.LocalEntityId);
            Assert.AreEqual(NetConnectionState.Connected, client.State);

            // 权威输入：连发 5 次 (vx=1, vy=0)，dt=0.1 => x += 0.5。
            const float dt = 0.1f;
            for (int i = 0; i < 5; i++)
            {
                client.SendInput(new[] { 1f, 0f }, dt);
                Drain(server, client);
            }

            float[] authoritative = server.Entities[localEntity];
            Assert.AreEqual(0.5f, authoritative[0], Delta, "服务器应权威地累加输入位移。");

            float[] predicted = client.PredictedLocalState;
            Assert.AreEqual(0.5f, predicted[0], Delta, "客户端预测应与服务器权威一致。");

            // 远端插值应排除本地玩家。
            client.Tick(0d);
            server.SetEntityState(remoteEntity, new[] { 0f, 0f });
            server.Tick(0d);
            Drain(server, client);

            IReadOnlyList<EntitySnapshot> remotes = client.GetInterpolatedEntities(0d);
            foreach (EntitySnapshot snap in remotes)
            {
                Assert.AreNotEqual(localEntity, snap.EntityId, "插值结果必须排除本地玩家受控实体。");
            }
        }
    }
}
