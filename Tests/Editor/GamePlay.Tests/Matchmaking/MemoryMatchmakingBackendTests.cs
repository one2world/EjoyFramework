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
    /// 针对 <see cref="MemoryMatchmakingBackend"/>（同步、确定性）的单元测试：
    /// 创建房间并令房主加入、加入成功/已满返回 null、列表过滤（私有/已满/非等待排除）、
    /// 快速匹配命中已有房间或新建。同时覆盖 <see cref="NullMatchmakingBackend"/> 的空操作语义。
    /// </summary>
    [TestFixture]
    public class MemoryMatchmakingBackendTests
    {
        [Test]
        public void CreateRoom_ReturnsRoom_WithHostJoined()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room created = null;

            backend.CreateRoom("Lobby", 4, "host", false, r => created = r);

            Assert.IsNotNull(created);
            Assert.AreEqual("host", created.HostId);
            Assert.AreEqual(1, created.PlayerCount);
            Assert.IsTrue(created.Contains("host"));
            Assert.AreEqual(1, backend.AllRooms.Count);
        }

        [Test]
        public void JoinRoom_Success_ReturnsRoom()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room created = null;
            backend.CreateRoom("Lobby", 4, "host", false, r => created = r);

            Room joined = null;
            backend.JoinRoom(created.Id, "a", r => joined = r);

            Assert.IsNotNull(joined);
            Assert.AreSame(created, joined);
            Assert.IsTrue(joined.Contains("a"));
            Assert.AreEqual(2, joined.PlayerCount);
        }

        [Test]
        public void JoinRoom_Full_ReturnsNull()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room created = null;
            backend.CreateRoom("Lobby", 1, "host", false, r => created = r); // 容量 1，房主已占满。

            Room joined = created;
            backend.JoinRoom(created.Id, "a", r => joined = r);

            Assert.IsNull(joined, "房间已满，加入应返回 null");
        }

        [Test]
        public void JoinRoom_MissingRoom_ReturnsNull()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room joined = new Room("placeholder", "x", 1);

            backend.JoinRoom("does-not-exist", "a", r => joined = r);

            Assert.IsNull(joined);
        }

        [Test]
        public void JoinRoom_AlreadyIn_IsIdempotentSuccess()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room created = null;
            backend.CreateRoom("Lobby", 4, "host", false, r => created = r);

            Room joined = null;
            backend.JoinRoom(created.Id, "host", r => joined = r);

            Assert.IsNotNull(joined);
            Assert.AreEqual(1, joined.PlayerCount);
        }

        [Test]
        public void LeaveRoom_FiresOnDone_AndRemovesPlayer()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room created = null;
            backend.CreateRoom("Lobby", 4, "host", false, r => created = r);
            backend.JoinRoom(created.Id, "a", _ => { });

            bool done = false;
            backend.LeaveRoom(created.Id, "a", () => done = true);

            Assert.IsTrue(done);
            Assert.IsFalse(created.Contains("a"));
        }

        [Test]
        public void LeaveRoom_MissingRoom_StillFiresOnDone()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            bool done = false;

            backend.LeaveRoom("nope", "a", () => done = true);

            Assert.IsTrue(done);
        }

        [Test]
        public void ListRooms_ReturnsOnlyPublicWaitingNonFull()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();

            Room open = null;
            backend.CreateRoom("Open", 4, "h1", false, r => open = r);

            Room priv = null;
            backend.CreateRoom("Private", 4, "h2", true, r => priv = r);

            Room full = null;
            backend.CreateRoom("Full", 1, "h3", false, r => full = r); // 容量 1 => 已满。

            Room ingame = null;
            backend.CreateRoom("InGame", 4, "h4", false, r => ingame = r);
            ingame.SetState(RoomState.InGame);

            IReadOnlyList<Room> rooms = null;
            backend.ListRooms(r => rooms = r);

            Assert.IsNotNull(rooms);
            Assert.AreEqual(1, rooms.Count, "仅应返回公开、未满、等待中的房间");
            Assert.AreSame(open, rooms[0]);
        }

        [Test]
        public void ListRooms_EmptyBackend_ReturnsEmpty()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            IReadOnlyList<Room> rooms = null;

            backend.ListRooms(r => rooms = r);

            Assert.IsNotNull(rooms);
            Assert.AreEqual(0, rooms.Count);
        }

        [Test]
        public void QuickMatch_JoinsExistingWaitingRoom()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            Room existing = null;
            backend.CreateRoom("Lobby", 4, "host", false, r => existing = r);

            Room matched = null;
            backend.QuickMatch("a", 4, r => matched = r);

            Assert.IsNotNull(matched);
            Assert.AreSame(existing, matched, "应加入已有的等待中房间");
            Assert.IsTrue(matched.Contains("a"));
            Assert.AreEqual(1, backend.AllRooms.Count, "不应新建房间");
        }

        [Test]
        public void QuickMatch_NoMatch_CreatesNewRoom()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();

            Room matched = null;
            backend.QuickMatch("a", 4, r => matched = r);

            Assert.IsNotNull(matched);
            Assert.AreEqual("a", matched.HostId, "新建房间时发起者应成为房主");
            Assert.AreEqual(1, backend.AllRooms.Count);
        }

        [Test]
        public void QuickMatch_CapacityMismatch_CreatesNewRoom()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            backend.CreateRoom("Lobby", 8, "host", false, _ => { }); // 容量 8。

            Room matched = null;
            backend.QuickMatch("a", 4, r => matched = r); // 需要容量 4。

            Assert.IsNotNull(matched);
            Assert.AreEqual(4, matched.MaxPlayers);
            Assert.AreEqual("a", matched.HostId);
            Assert.AreEqual(2, backend.AllRooms.Count, "容量不匹配应新建房间");
        }

        [Test]
        public void QuickMatch_SkipsPrivateRoom_CreatesNew()
        {
            MemoryMatchmakingBackend backend = new MemoryMatchmakingBackend();
            backend.CreateRoom("Private", 4, "host", true, _ => { });

            Room matched = null;
            backend.QuickMatch("a", 4, r => matched = r);

            Assert.IsNotNull(matched);
            Assert.IsFalse(matched.IsPrivate);
            Assert.AreEqual("a", matched.HostId);
            Assert.AreEqual(2, backend.AllRooms.Count);
        }

        [Test]
        public void NullBackend_AllCallbacks_FireWithNullOrEmpty()
        {
            NullMatchmakingBackend backend = new NullMatchmakingBackend();

            Room create = new Room("x", "x", 1);
            backend.CreateRoom("n", 4, "h", false, r => create = r);
            Assert.IsNull(create);

            Room join = new Room("x", "x", 1);
            backend.JoinRoom("id", "p", r => join = r);
            Assert.IsNull(join);

            bool left = false;
            backend.LeaveRoom("id", "p", () => left = true);
            Assert.IsTrue(left);

            IReadOnlyList<Room> list = null;
            backend.ListRooms(r => list = r);
            Assert.IsNotNull(list);
            Assert.AreEqual(0, list.Count);

            Room quick = new Room("x", "x", 1);
            backend.QuickMatch("p", 4, r => quick = r);
            Assert.IsNull(quick);
        }
    }
}
