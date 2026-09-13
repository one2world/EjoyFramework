//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Matchmaking;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Matchmaking
{
    /// <summary>
    /// 针对 <see cref="Room"/> 座位模型、加入/离开/踢人/就绪规则、房主自动指派与迁移、
    /// 状态机以及 <see cref="Room.OnChanged"/> 仅在实际变化时触发的单元测试。
    /// </summary>
    [TestFixture]
    public class RoomTests
    {
        [Test]
        public void Constructor_NoHost_StartsEmptyWaiting()
        {
            Room room = new Room("r1", "Lobby", 4);

            Assert.AreEqual("r1", room.Id);
            Assert.AreEqual("Lobby", room.Name);
            Assert.AreEqual(4, room.MaxPlayers);
            Assert.AreEqual(RoomState.Waiting, room.State);
            Assert.AreEqual(0, room.PlayerCount);
            Assert.IsNull(room.HostId);
            Assert.IsFalse(room.IsFull);
            Assert.AreEqual(4, room.Slots.Count);
        }

        [Test]
        public void Constructor_WithHost_JoinsHostIntoFirstSlot()
        {
            Room room = new Room("r1", "Lobby", 4, "host");

            Assert.AreEqual("host", room.HostId);
            Assert.AreEqual(1, room.PlayerCount);
            Assert.IsTrue(room.Contains("host"));
            Assert.AreEqual("host", room.Slots[0].PlayerId);
        }

        [Test]
        public void Join_FirstJoiner_BecomesHost()
        {
            Room room = new Room("r1", "Lobby", 4);

            bool ok = room.Join("a");

            Assert.IsTrue(ok);
            Assert.AreEqual("a", room.HostId);
            Assert.AreEqual(1, room.PlayerCount);
        }

        [Test]
        public void Join_SecondJoiner_DoesNotChangeHost()
        {
            Room room = new Room("r1", "Lobby", 4);
            room.Join("a");

            room.Join("b");

            Assert.AreEqual("a", room.HostId);
            Assert.AreEqual(2, room.PlayerCount);
            Assert.IsTrue(room.Contains("b"));
        }

        [Test]
        public void Join_AlreadyInRoom_Rejected()
        {
            Room room = new Room("r1", "Lobby", 4);
            room.Join("a");

            bool again = room.Join("a");

            Assert.IsFalse(again);
            Assert.AreEqual(1, room.PlayerCount);
        }

        [Test]
        public void Join_NullOrEmpty_Rejected()
        {
            Room room = new Room("r1", "Lobby", 4);

            Assert.IsFalse(room.Join(null));
            Assert.IsFalse(room.Join(string.Empty));
            Assert.AreEqual(0, room.PlayerCount);
        }

        [Test]
        public void Join_FullRoom_Rejected()
        {
            Room room = new Room("r1", "Lobby", 2);
            room.Join("a");
            room.Join("b");

            bool rejected = room.Join("c");

            Assert.IsFalse(rejected);
            Assert.IsTrue(room.IsFull);
            Assert.AreEqual(2, room.PlayerCount);
            Assert.IsFalse(room.Contains("c"));
        }

        [Test]
        public void Join_NonWaitingRoom_Rejected()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.SetState(RoomState.InGame);

            bool rejected = room.Join("b");

            Assert.IsFalse(rejected);
            Assert.AreEqual(1, room.PlayerCount);
        }

        [Test]
        public void Leave_NotInRoom_ReturnsFalse()
        {
            Room room = new Room("r1", "Lobby", 4, "host");

            bool ok = room.Leave("ghost");

            Assert.IsFalse(ok);
            Assert.AreEqual(1, room.PlayerCount);
        }

        [Test]
        public void Leave_HostLeaves_MigratesToNextOccupant()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.Join("a");
            room.Join("b");
            Assert.AreEqual("host", room.HostId);

            bool ok = room.Leave("host");

            Assert.IsTrue(ok);
            Assert.AreEqual("a", room.HostId, "房主离开后应迁移给下一个占位玩家");
            Assert.AreEqual(2, room.PlayerCount);
            Assert.IsFalse(room.Contains("host"));
        }

        [Test]
        public void Leave_NonHostLeaves_HostUnchanged()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.Join("a");

            room.Leave("a");

            Assert.AreEqual("host", room.HostId);
            Assert.AreEqual(1, room.PlayerCount);
        }

        [Test]
        public void Leave_LastPlayer_RoomBecomesClosed()
        {
            Room room = new Room("r1", "Lobby", 4, "host");

            room.Leave("host");

            Assert.AreEqual(0, room.PlayerCount);
            Assert.IsNull(room.HostId);
            Assert.AreEqual(RoomState.Closed, room.State);
        }

        [Test]
        public void Kick_ByHost_RemovesTarget()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.Join("a");

            bool ok = room.Kick("host", "a");

            Assert.IsTrue(ok);
            Assert.IsFalse(room.Contains("a"));
            Assert.AreEqual(1, room.PlayerCount);
        }

        [Test]
        public void Kick_ByNonHost_Rejected()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.Join("a");
            room.Join("b");

            bool rejected = room.Kick("a", "b");

            Assert.IsFalse(rejected);
            Assert.IsTrue(room.Contains("b"));
            Assert.AreEqual(3, room.PlayerCount);
        }

        [Test]
        public void Kick_Self_Rejected()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.Join("a");

            bool rejected = room.Kick("host", "host");

            Assert.IsFalse(rejected);
            Assert.IsTrue(room.Contains("host"));
            Assert.AreEqual(2, room.PlayerCount);
        }

        [Test]
        public void Kick_MissingTarget_Rejected()
        {
            Room room = new Room("r1", "Lobby", 4, "host");

            bool rejected = room.Kick("host", "ghost");

            Assert.IsFalse(rejected);
        }

        [Test]
        public void SetReady_TogglesAndReflectsInSlot()
        {
            Room room = new Room("r1", "Lobby", 4, "host");

            bool changed = room.SetReady("host", true);

            Assert.IsTrue(changed);
            Assert.IsTrue(room.Slots[0].IsReady);
        }

        [Test]
        public void SetReady_SameValue_ReturnsFalse()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.SetReady("host", true);

            bool noChange = room.SetReady("host", true);

            Assert.IsFalse(noChange, "状态未变化时应返回 false");
        }

        [Test]
        public void SetReady_PlayerNotInRoom_ReturnsFalse()
        {
            Room room = new Room("r1", "Lobby", 4, "host");

            bool ok = room.SetReady("ghost", true);

            Assert.IsFalse(ok);
        }

        [Test]
        public void AllReady_EveryOccupiedSlotReady_True()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.Join("a");
            room.SetReady("host", true);
            room.SetReady("a", true);

            Assert.IsTrue(room.AllReady);
        }

        [Test]
        public void AllReady_OneNotReady_False()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.Join("a");
            room.SetReady("host", true);

            Assert.IsFalse(room.AllReady);
        }

        [Test]
        public void AllReady_EmptyRoom_False()
        {
            Room room = new Room("r1", "Lobby", 4);

            Assert.IsFalse(room.AllReady, "空房间不应视为全员就绪");
        }

        [Test]
        public void MigrateHost_PicksNextOccupant()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.Join("a");

            bool ok = room.MigrateHost();

            Assert.IsTrue(ok);
            Assert.AreEqual("a", room.HostId);
        }

        [Test]
        public void MigrateHost_NoOtherOccupant_ReturnsFalse()
        {
            Room room = new Room("r1", "Lobby", 4, "host");

            bool ok = room.MigrateHost();

            Assert.IsFalse(ok, "只有房主一人时无可迁移对象");
            Assert.AreEqual("host", room.HostId);
        }

        [Test]
        public void MigrateHost_EmptyRoom_ReturnsFalse()
        {
            Room room = new Room("r1", "Lobby", 4);

            bool ok = room.MigrateHost();

            Assert.IsFalse(ok);
            Assert.IsNull(room.HostId);
        }

        [Test]
        public void Slots_ReflectOccupancyAndReady()
        {
            Room room = new Room("r1", "Lobby", 3, "host");
            room.Join("a");
            room.SetReady("a", true);

            Assert.AreEqual("host", room.Slots[0].PlayerId);
            Assert.IsFalse(room.Slots[0].IsReady);
            Assert.AreEqual("a", room.Slots[1].PlayerId);
            Assert.IsTrue(room.Slots[1].IsReady);
            Assert.IsTrue(room.Slots[2].IsEmpty);
            Assert.IsNull(room.Slots[2].PlayerId);
        }

        [Test]
        public void SetState_NoChange_DoesNotFireOnChanged()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            int fired = 0;
            room.OnChanged += r => fired++;

            room.SetState(RoomState.Waiting); // 与当前相同。

            Assert.AreEqual(0, fired);
        }

        [Test]
        public void OnChanged_FiresOnJoin()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            int fired = 0;
            Room arg = null;
            room.OnChanged += r =>
            {
                fired++;
                arg = r;
            };

            room.Join("a");

            Assert.AreEqual(1, fired);
            Assert.AreSame(room, arg);
        }

        [Test]
        public void OnChanged_NotFired_OnRejectedJoin()
        {
            Room room = new Room("r1", "Lobby", 1, "host"); // 已满。
            int fired = 0;
            room.OnChanged += r => fired++;

            bool rejected = room.Join("a");

            Assert.IsFalse(rejected);
            Assert.AreEqual(0, fired, "被拒绝的加入不应触发 OnChanged");
        }

        [Test]
        public void OnChanged_NotFired_OnRedundantSetReady()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            room.SetReady("host", true);
            int fired = 0;
            room.OnChanged += r => fired++;

            room.SetReady("host", true); // 重复设置。

            Assert.AreEqual(0, fired);
        }

        [Test]
        public void OnChanged_FiresOnStateChange()
        {
            Room room = new Room("r1", "Lobby", 4, "host");
            int fired = 0;
            room.OnChanged += r => fired++;

            room.SetState(RoomState.Starting);

            Assert.AreEqual(1, fired);
            Assert.AreEqual(RoomState.Starting, room.State);
        }
    }
}
