//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Session;
using EjoyFramework.GamePlay.Netcode.Sync;
using EjoyFramework.GamePlay.Netcode.Transport;

namespace EjoyFramework.GamePlay.Tests.Netcode.Session
{
    /// <summary>
    /// <see cref="NetcodeServer"/> + <see cref="NetcodeClient"/> 的端到端测试。
    /// 全程跑在确定性 <see cref="LoopbackTransport"/> 上（无套接字、无线程、无真实时间）：
    /// 通过手动推进 <c>time</c> 变量并交替 Poll/Tick 来驱动收发，验证
    /// 加入握手、权威输入、和解收敛、远端插值与排除本地玩家等行为。
    /// <para>
    /// 共享的二维 mover：状态=<c>float[]{x,y}</c>，输入=<c>float[]{vx,vy}</c>，
    /// step=(s,i,dt)=> new[]{ s[0]+i[0]*dt, s[1]+i[1]*dt }。
    /// </para>
    /// </summary>
    public class NetcodeSessionEndToEndTests
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

        // 把服务器/客户端的 Poll 反复跑几遍，确保回环队列在每步内排空。
        private static void Drain(NetcodeServer server, NetcodeClient client, int rounds = 4)
        {
            for (int i = 0; i < rounds; i++)
            {
                client.Poll();
                server.Poll();
            }
        }

        // —— 1) 加入握手 ——

        [Test]
        public void JoinHandshake_AssignsLocalEntity_AndFiresEvents()
        {
            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            NetcodeServer server = new NetcodeServer(serverTransport, MoverStep, tickRateHz: 30);
            NetcodeClient client = new NetcodeClient(clientTransport, MoverStep, interpolationDelaySec: 0.1);

            int joinedConn = -1;
            server.OnClientJoined += conn => joinedConn = conn;

            bool connectedFired = false;
            client.OnConnected += () => connectedFired = true;

            server.Start(0);
            client.Connect("loopback", 0);

            // 连接事件经一次 Poll 触发：服务器应感知到加入。
            Drain(server, client);
            Assert.GreaterOrEqual(joinedConn, 0, "服务器应触发 OnClientJoined 并拿到 connectionId。");

            // 注册受控实体并分配给该连接 —— 这一步才会下发 WelcomeMessage。
            const int localEntity = 1;
            server.RegisterEntity(localEntity, new[] { 0f, 0f });
            server.AssignClientEntity(joinedConn, localEntity);

            Drain(server, client);

            Assert.IsTrue(connectedFired, "客户端应在收到 WelcomeMessage 后触发 OnConnected。");
            Assert.AreEqual(localEntity, client.LocalEntityId, "客户端 LocalEntityId 应由 WelcomeMessage 设置。");
            Assert.AreEqual(NetConnectionState.Connected, client.State);
        }

        // —— 2) 权威输入 ——

        [Test]
        public void AuthoritativeInput_ServerAdvancesEntity_AndPredictionMatches()
        {
            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            NetcodeServer server = new NetcodeServer(serverTransport, MoverStep, tickRateHz: 30);
            NetcodeClient client = new NetcodeClient(clientTransport, MoverStep, interpolationDelaySec: 0.1);

            int conn = -1;
            server.OnClientJoined += c => conn = c;

            server.Start(0);
            client.Connect("loopback", 0);
            Drain(server, client);

            const int localEntity = 1;
            server.RegisterEntity(localEntity, new[] { 0f, 0f });
            server.AssignClientEntity(conn, localEntity);
            Drain(server, client);

            // 客户端连发 5 次输入 (vx=1, vy=0)，dt=0.1 => 位移合计 x += 0.5。
            const int steps = 5;
            const float dt = 0.1f;
            for (int i = 0; i < steps; i++)
            {
                client.SendInput(new[] { 1f, 0f }, dt);
                Drain(server, client);
            }

            // 服务器权威实体应被推进到 x = 5 * 1 * 0.1 = 0.5。
            float[] authoritative = server.Entities[localEntity];
            Assert.AreEqual(0.5f, authoritative[0], Delta, "服务器应权威地累加输入位移。");
            Assert.AreEqual(0f, authoritative[1], Delta);

            // 客户端预测状态应与权威一致（同一确定性 step）。
            float[] predicted = client.PredictedLocalState;
            Assert.AreEqual(0.5f, predicted[0], Delta, "客户端预测应与服务器权威一致。");
            Assert.AreEqual(0f, predicted[1], Delta);
        }

        // —— 3) 和解收敛 ——

        [Test]
        public void Reconciliation_PredictedConvergesToAuthoritative_PendingDrops()
        {
            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            NetcodeServer server = new NetcodeServer(serverTransport, MoverStep, tickRateHz: 30);
            NetcodeClient client = new NetcodeClient(clientTransport, MoverStep, interpolationDelaySec: 0.1);

            int conn = -1;
            server.OnClientJoined += c => conn = c;

            server.Start(0);
            client.Connect("loopback", 0);
            Drain(server, client);

            const int localEntity = 1;
            server.RegisterEntity(localEntity, new[] { 0f, 0f });
            server.AssignClientEntity(conn, localEntity);
            Drain(server, client);

            const float dt = 0.1f;
            double time = 0d;

            // 模拟若干帧：客户端发输入 -> 排空 -> 服务器 Tick 回发快照 -> 排空（触发和解）。
            for (int frame = 0; frame < 10; frame++)
            {
                client.Tick(time);
                client.SendInput(new[] { 2f, 1f }, dt); // x += 0.2/帧, y += 0.1/帧
                Drain(server, client);

                server.Tick(time);
                Drain(server, client);

                time += dt;
            }

            // 全部输入已被服务器处理并 ack：客户端预测应等于服务器权威状态。
            float[] authoritative = server.Entities[localEntity];
            float[] predicted = client.PredictedLocalState;

            Assert.AreEqual(authoritative[0], predicted[0], Delta, "和解后预测 x 应收敛到权威。");
            Assert.AreEqual(authoritative[1], predicted[1], Delta, "和解后预测 y 应收敛到权威。");

            // 期望累积：10 帧 * (vx=2,vy=1) * dt=0.1 => x=2.0, y=1.0。
            Assert.AreEqual(2.0f, authoritative[0], Delta);
            Assert.AreEqual(1.0f, authoritative[1], Delta);
        }

        [Test]
        public void Reconciliation_AuthoritativeOverridesDivergentPrediction()
        {
            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            NetcodeServer server = new NetcodeServer(serverTransport, MoverStep, tickRateHz: 30);
            NetcodeClient client = new NetcodeClient(clientTransport, MoverStep, interpolationDelaySec: 0.1);

            int conn = -1;
            server.OnClientJoined += c => conn = c;

            server.Start(0);
            client.Connect("loopback", 0);
            Drain(server, client);

            const int localEntity = 1;
            // 服务器实体从一个「非零」基线出生：客户端预测以零起步，必须靠和解校正到该基线。
            server.RegisterEntity(localEntity, new[] { 100f, 50f });
            server.AssignClientEntity(conn, localEntity);
            Drain(server, client);

            // 先发一帧快照让客户端把权威基线纳入（无输入 -> 纯权威覆盖）。
            server.Tick(0d);
            Drain(server, client);

            float[] predicted = client.PredictedLocalState;
            Assert.AreEqual(100f, predicted[0], Delta, "客户端应通过和解采纳服务器的非零权威基线 x。");
            Assert.AreEqual(50f, predicted[1], Delta, "客户端应通过和解采纳服务器的非零权威基线 y。");
        }

        // —— 4) 远端插值（排除本地玩家） ——

        [Test]
        public void RemoteInterpolation_SamplesMidpoint_AndExcludesLocalPlayer()
        {
            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            NetcodeServer server = new NetcodeServer(serverTransport, MoverStep, tickRateHz: 30);

            const double interpDelay = 0.1;
            NetcodeClient client = new NetcodeClient(clientTransport, MoverStep, interpolationDelaySec: interpDelay);

            int conn = -1;
            server.OnClientJoined += c => conn = c;

            server.Start(0);
            client.Connect("loopback", 0);
            Drain(server, client);

            const int localEntity = 1;
            const int remoteEntity = 99;
            server.RegisterEntity(localEntity, new[] { 0f, 0f });
            server.RegisterEntity(remoteEntity, new[] { 0f, 0f });
            server.AssignClientEntity(conn, localEntity);
            Drain(server, client);

            // 让本地时钟与服务器时间对齐（offset≈0）：每次快照时 localTime == serverTime。
            // 快照 A：远端 x=0 @ serverTime=0。
            client.Tick(0d);
            server.SetEntityState(remoteEntity, new[] { 0f, 0f });
            server.Tick(0d);
            Drain(server, client);

            // 快照 B：远端 x=10 @ serverTime=1。
            client.Tick(1d);
            server.SetEntityState(remoteEntity, new[] { 10f, 0f });
            server.Tick(1d);
            Drain(server, client);

            // 选取本地时间使得「渲染时间 = 估计服务器时间 - 插值延迟」落在两快照中点(serverTime=0.5)。
            // offset≈0 => 估计服务器时间≈localTime；故 localTime = 0.5 + interpDelay。
            double sampleLocalTime = 0.5 + interpDelay;
            IReadOnlyList<EntitySnapshot> remotes = client.GetInterpolatedEntities(sampleLocalTime);

            // 必须排除本地玩家实体。
            foreach (EntitySnapshot snap in remotes)
            {
                Assert.AreNotEqual(localEntity, snap.EntityId, "插值结果必须排除本地玩家受控实体。");
            }

            // 找到远端实体并校验其 x≈中点 5。
            bool found = false;
            foreach (EntitySnapshot snap in remotes)
            {
                if (snap.EntityId == remoteEntity)
                {
                    found = true;
                    Assert.AreEqual(5f, snap.Values[0], 0.5f, "远端实体应被插值到两快照中点附近(x≈5)。");
                }
            }

            Assert.IsTrue(found, "插值结果应包含远端实体。");
        }

        [Test]
        public void RemoteInterpolation_ClampsToEndpoints()
        {
            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            NetcodeServer server = new NetcodeServer(serverTransport, MoverStep, tickRateHz: 30);

            const double interpDelay = 0.1;
            NetcodeClient client = new NetcodeClient(clientTransport, MoverStep, interpolationDelaySec: interpDelay);

            int conn = -1;
            server.OnClientJoined += c => conn = c;

            server.Start(0);
            client.Connect("loopback", 0);
            Drain(server, client);

            const int localEntity = 1;
            const int remoteEntity = 99;
            server.RegisterEntity(localEntity, new[] { 0f, 0f });
            server.RegisterEntity(remoteEntity, new[] { 7f, 0f });
            server.AssignClientEntity(conn, localEntity);
            Drain(server, client);

            client.Tick(0d);
            server.SetEntityState(remoteEntity, new[] { 7f, 0f });
            server.Tick(0d);
            Drain(server, client);

            // 渲染时间早于最旧快照 -> 钳制到最旧端点 x=7。
            IReadOnlyList<EntitySnapshot> remotes = client.GetInterpolatedEntities(0d);
            bool found = false;
            foreach (EntitySnapshot snap in remotes)
            {
                if (snap.EntityId == remoteEntity)
                {
                    found = true;
                    Assert.AreEqual(7f, snap.Values[0], Delta);
                }
            }

            Assert.IsTrue(found, "应在端点钳制下返回远端实体。");
        }

        // —— 5) 断开通知 ——

        [Test]
        public void Disconnect_NotifiesBothSides()
        {
            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            NetcodeServer server = new NetcodeServer(serverTransport, MoverStep, tickRateHz: 30);
            NetcodeClient client = new NetcodeClient(clientTransport, MoverStep, interpolationDelaySec: 0.1);

            int conn = -1;
            server.OnClientJoined += c => conn = c;

            int leftConn = -1;
            int leftEntity = -2;
            server.OnClientLeft += (c, e) => { leftConn = c; leftEntity = e; };

            bool clientDisconnected = false;
            client.OnDisconnected += () => clientDisconnected = true;

            server.Start(0);
            client.Connect("loopback", 0);
            Drain(server, client);

            const int localEntity = 1;
            server.RegisterEntity(localEntity, new[] { 0f, 0f });
            server.AssignClientEntity(conn, localEntity);
            Drain(server, client);

            clientTransport.Disconnect();
            Drain(server, client);

            Assert.AreEqual(conn, leftConn, "服务器应在断开时触发 OnClientLeft 并带正确 connectionId。");
            Assert.AreEqual(localEntity, leftEntity, "OnClientLeft 应回传该连接的受控实体 id（供上层处理 / 重生）。");
            Assert.IsTrue(clientDisconnected, "客户端应触发 OnDisconnected。");
        }

        [Test]
        public void Update_BroadcastsSnapshotsAtConfiguredTickRate_NotRenderFrameRate()
        {
            var (serverTransport, clientTransport) = LoopbackTransport.CreatePair();
            NetcodeServer server = new NetcodeServer(serverTransport, MoverStep, tickRateHz: 10);

            int receivedSnapshots = 0;
            clientTransport.OnData += (connectionId, payload) => receivedSnapshots++;

            server.Start(0);
            clientTransport.Connect("loopback", 0);

            // Let the server observe the connection, then discard any setup traffic.
            server.Update(0f, 0f);
            clientTransport.Poll();
            receivedSnapshots = 0;

            // Nine 100-fps render frames are only 90 ms: a 10 Hz server must not tick yet.
            for (int i = 0; i < 9; i++)
            {
                server.Update(0.01f, 0.01f);
                clientTransport.Poll();
            }

            Assert.AreEqual(0, receivedSnapshots);

            // Crossing 100 ms produces exactly one snapshot, not one per render frame.
            server.Update(0.02f, 0.02f);
            clientTransport.Poll();
            Assert.AreEqual(1, receivedSnapshots);
        }
    }
}
