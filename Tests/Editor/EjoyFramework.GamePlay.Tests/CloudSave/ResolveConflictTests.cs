//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.CloudSave;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.CloudSave
{
    /// <summary>
    /// 针对 <see cref="CloudSaveManager.ResolveConflict"/> 纯冲突解决逻辑的单元测试：
    /// 覆盖每种策略、null 处理、并列规则、字节差异检测与 Manual 解析器。
    /// </summary>
    [TestFixture]
    public class ResolveConflictTests
    {
        private static CloudSaveData Make(string key, byte[] data, long version, long ts)
        {
            return new CloudSaveData(key, data, version, ts);
        }

        [Test]
        public void ResolveConflict_BothNull_ReturnsNull()
        {
            CloudSaveData winner = CloudSaveManager.ResolveConflict(null, null, ConflictResolution.PreferLocal);
            Assert.IsNull(winner);
        }

        [Test]
        public void ResolveConflict_LocalNull_RemoteWins()
        {
            CloudSaveData remote = Make("k", new byte[] { 1 }, 1, 100);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(null, remote, ConflictResolution.PreferLocal);
            Assert.AreSame(remote, winner);
        }

        [Test]
        public void ResolveConflict_RemoteNull_LocalWins()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 1, 100);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, null, ConflictResolution.PreferRemote);
            Assert.AreSame(local, winner);
        }

        [Test]
        public void ResolveConflict_PreferLocal_ReturnsLocal()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 1, 100);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 99, 9999);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.PreferLocal);
            Assert.AreSame(local, winner);
        }

        [Test]
        public void ResolveConflict_PreferRemote_ReturnsRemote()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 99, 9999);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 1, 100);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.PreferRemote);
            Assert.AreSame(remote, winner);
        }

        [Test]
        public void ResolveConflict_PreferNewerTimestamp_PicksNewer()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 5, 100);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 1, 200);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.PreferNewerTimestamp);
            Assert.AreSame(remote, winner, "时间戳更大的远端应胜出。");
        }

        [Test]
        public void ResolveConflict_PreferNewerTimestamp_LocalNewer_PicksLocal()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 1, 300);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 9, 200);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.PreferNewerTimestamp);
            Assert.AreSame(local, winner);
        }

        [Test]
        public void ResolveConflict_PreferNewerTimestamp_Tie_PicksLocal()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 1, 500);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 2, 500);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.PreferNewerTimestamp);
            Assert.AreSame(local, winner, "时间戳并列时应保留本地。");
        }

        [Test]
        public void ResolveConflict_PreferHigherVersion_PicksHigher()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 3, 100);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 7, 100);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.PreferHigherVersion);
            Assert.AreSame(remote, winner);
        }

        [Test]
        public void ResolveConflict_PreferHigherVersion_Tie_PicksLocal()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 4, 100);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 4, 999);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.PreferHigherVersion);
            Assert.AreSame(local, winner, "版本号并列时应保留本地。");
        }

        [Test]
        public void ResolveConflict_Manual_UsesResolver()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 1, 100);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 2, 200);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.Manual,
                (l, r) => r);
            Assert.AreSame(remote, winner, "Manual 策略应采用解析器返回的胜者。");
        }

        [Test]
        public void ResolveConflict_Manual_NullResolver_FallsBackToLocal()
        {
            CloudSaveData local = Make("k", new byte[] { 1 }, 1, 100);
            CloudSaveData remote = Make("k", new byte[] { 2 }, 2, 200);
            CloudSaveData winner = CloudSaveManager.ResolveConflict(local, remote, ConflictResolution.Manual);
            Assert.AreSame(local, winner, "未提供解析器时 Manual 应回退为本地。");
        }

        [Test]
        public void Differ_SameVersionSameBytes_False()
        {
            CloudSaveData a = Make("k", new byte[] { 1, 2, 3 }, 5, 100);
            CloudSaveData b = Make("k", new byte[] { 1, 2, 3 }, 5, 999);
            Assert.IsFalse(CloudSaveManager.Differ(a, b), "版本与字节均相同则视为不相异（时间戳不参与）。");
        }

        [Test]
        public void Differ_DifferentVersion_True()
        {
            CloudSaveData a = Make("k", new byte[] { 1, 2, 3 }, 5, 100);
            CloudSaveData b = Make("k", new byte[] { 1, 2, 3 }, 6, 100);
            Assert.IsTrue(CloudSaveManager.Differ(a, b));
        }

        [Test]
        public void Differ_DifferentBytes_SameVersion_True()
        {
            CloudSaveData a = Make("k", new byte[] { 1, 2, 3 }, 5, 100);
            CloudSaveData b = Make("k", new byte[] { 1, 2, 4 }, 5, 100);
            Assert.IsTrue(CloudSaveManager.Differ(a, b), "版本相同但字节不同也应视为相异。");
        }

        [Test]
        public void Differ_DifferentLength_True()
        {
            CloudSaveData a = Make("k", new byte[] { 1, 2 }, 5, 100);
            CloudSaveData b = Make("k", new byte[] { 1, 2, 3 }, 5, 100);
            Assert.IsTrue(CloudSaveManager.Differ(a, b));
        }

        [Test]
        public void Differ_OneNull_True()
        {
            CloudSaveData a = Make("k", new byte[] { 1 }, 5, 100);
            Assert.IsTrue(CloudSaveManager.Differ(a, null));
            Assert.IsTrue(CloudSaveManager.Differ(null, a));
        }

        [Test]
        public void Differ_SameReference_False()
        {
            CloudSaveData a = Make("k", new byte[] { 1 }, 5, 100);
            Assert.IsFalse(CloudSaveManager.Differ(a, a));
        }
    }
}
