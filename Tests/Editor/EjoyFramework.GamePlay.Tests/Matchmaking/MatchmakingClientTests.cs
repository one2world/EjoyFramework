//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Matchmaking;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Matchmaking
{
    /// <summary>
    /// 针对 <see cref="MatchmakingClient"/> 的单元测试：创建/加入/快速匹配设置 CurrentRoom 并抛 OnRoomJoined、
    /// 离开清空 CurrentRoom 并抛 OnRoomLeft、刷新列表抛 OnRoomList。
    /// 使用同步的 <see cref="MemoryMatchmakingBackend"/> 保证确定性。
    /// </summary>
    [TestFixture]
    public class MatchmakingClientTests
    {
        [Test]
        public void CreateRoom_SetsCurrentRoom_AndFiresJoined()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");

            Room joinedArg = null;
            int joinedCount = 0;
            client.OnRoomJoined += r =>
            {
                joinedArg = r;
                joinedCount++;
            };

            client.CreateRoom("Lobby", 4);

            Assert.IsNotNull(client.CurrentRoom);
            Assert.AreEqual("me", client.CurrentRoom.HostId);
            Assert.AreEqual(1, joinedCount);
            Assert.AreSame(client.CurrentRoom, joinedArg);
        }

        [Test]
        public void LocalPlayerId_Exposed()
        {
            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(new MemoryMatchmakingBackend());
            client.SetLocalPlayerId("me");

            Assert.AreEqual("me", client.LocalPlayerId);
            Assert.IsNull(client.CurrentRoom);
        }

        [Test]
        public void JoinRoom_Success_SetsCurrentRoom_AndFiresJoined()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room existing = null;
            backend.CreateRoom("Lobby", 4, "host", false, r => existing = r);

            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");
            int joinedCount = 0;
            client.OnRoomJoined += _ => joinedCount++;

            client.JoinRoom(existing.Id);

            Assert.IsNotNull(client.CurrentRoom);
            Assert.AreSame(existing, client.CurrentRoom);
            Assert.IsTrue(client.CurrentRoom.Contains("me"));
            Assert.AreEqual(1, joinedCount);
        }

        [Test]
        public void JoinRoom_Failure_LeavesCurrentRoomNull_AndNoEvent()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");
            int joinedCount = 0;
            client.OnRoomJoined += _ => joinedCount++;

            client.JoinRoom("does-not-exist");

            Assert.IsNull(client.CurrentRoom);
            Assert.AreEqual(0, joinedCount, "加入失败不应触发 OnRoomJoined");
        }

        [Test]
        public void QuickMatch_NoRoom_CreatesAndSetsCurrentRoom()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");
            int joinedCount = 0;
            client.OnRoomJoined += _ => joinedCount++;

            client.QuickMatch(4);

            Assert.IsNotNull(client.CurrentRoom);
            Assert.AreEqual("me", client.CurrentRoom.HostId);
            Assert.AreEqual(4, client.CurrentRoom.MaxPlayers);
            Assert.AreEqual(1, joinedCount);
        }

        [Test]
        public void QuickMatch_JoinsExistingRoom()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room existing = null;
            backend.CreateRoom("Lobby", 4, "host", false, r => existing = r);

            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");
            client.QuickMatch(4);

            Assert.AreSame(existing, client.CurrentRoom);
            Assert.IsTrue(client.CurrentRoom.Contains("me"));
        }

        [Test]
        public void Leave_ClearsCurrentRoom_AndFiresLeft()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");
            client.CreateRoom("Lobby", 4);
            Assert.IsNotNull(client.CurrentRoom);

            int leftCount = 0;
            client.OnRoomLeft += () => leftCount++;

            client.Leave();

            Assert.IsNull(client.CurrentRoom);
            Assert.AreEqual(1, leftCount);
        }

        [Test]
        public void Leave_WhenNotInRoom_IsNoOp()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");
            int leftCount = 0;
            client.OnRoomLeft += () => leftCount++;

            client.Leave();

            Assert.IsNull(client.CurrentRoom);
            Assert.AreEqual(0, leftCount, "未在房间内时离开应为空操作");
        }

        [Test]
        public void Leave_RemovesPlayerFromBackendRoom()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room existing = null;
            backend.CreateRoom("Lobby", 4, "host", false, r => existing = r);

            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");
            client.JoinRoom(existing.Id);
            Assert.IsTrue(existing.Contains("me"));

            client.Leave();

            Assert.IsFalse(existing.Contains("me"), "离开后后端房间应移除该玩家");
        }

        [Test]
        public void RefreshRoomList_FiresOnRoomList()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            backend.CreateRoom("Open", 4, "h1", false, _ => { });
            backend.CreateRoom("Private", 4, "h2", true, _ => { });

            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(backend);
            client.SetLocalPlayerId("me");
            IReadOnlyList<Room> received = null;
            client.OnRoomList += r => received = r;

            client.RefreshRoomList();

            Assert.IsNotNull(received);
            Assert.AreEqual(1, received.Count, "仅应返回公开、未满、等待中的房间");
            Assert.AreEqual("Open", received[0].Name);
        }

        [Test]
        public void SetBackend_Null_FallsBackToNullBackend()
        {
            // 注入 null 后端应退化为 NullMatchmakingBackend：操作不抛异常，且不进入任何房间。
            MatchmakingClient client = new MatchmakingClient();
            client.SetBackend(null);
            client.SetLocalPlayerId("me");
            int joinedCount = 0;
            client.OnRoomJoined += _ => joinedCount++;

            client.CreateRoom("Lobby", 4);

            Assert.IsNull(client.CurrentRoom);
            Assert.AreEqual(0, joinedCount);
        }

        [Test]
        public void Default_NoBackend_FallsBackToNullBackend()
        {
            // 无参构造默认退化为 NullMatchmakingBackend：开箱即用不抛异常，且不进入任何房间。
            MatchmakingClient client = new MatchmakingClient();
            client.SetLocalPlayerId("me");
            int joinedCount = 0;
            client.OnRoomJoined += _ => joinedCount++;

            client.CreateRoom("Lobby", 4);

            Assert.IsNull(client.CurrentRoom);
            Assert.AreEqual(0, joinedCount);
        }
    }
}
