//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.CloudSave;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.CloudSave
{
    /// <summary>
    /// 针对 <see cref="CloudSaveManager.Sync"/> 的单元测试：无远端上传本地、远端冲突触发
    /// <see cref="CloudSaveManager.OnConflict"/> 并按策略解析、胜者回调与远端回写。
    /// 使用同步内存后端，保证回调确定地完成。
    /// </summary>
    [TestFixture]
    public class CloudSaveManagerSyncTests
    {
        private static CloudSaveData Make(string key, byte[] data, long version, long ts)
        {
            return new CloudSaveData(key, data, version, ts);
        }

        [Test]
        public void Sync_NoRemote_UploadsLocal_ResolvesToLocal()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            CloudSaveData local = Make("slot", new byte[] { 7 }, 1, 100);

            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.AreSame(local, resolved, "无远端时胜者应为本地。");

            // 本地应已被上传：再次下载应取回它。
            CloudSaveData remoteNow = null;
            backend.Download("slot", d => remoteNow = d);
            Assert.AreSame(local, remoteNow, "本地应已被上传到后端。");
        }

        [Test]
        public void Sync_NoRemote_DoesNotFireOnConflict()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            bool conflictFired = false;
            manager.OnConflict += (l, r) => conflictFired = true;

            manager.Sync(Make("slot", new byte[] { 1 }, 1, 100), w => { });

            Assert.IsFalse(conflictFired);
        }

        [Test]
        public void Sync_ConflictingRemote_FiresOnConflict_AndResolvesPerPolicy()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            // 远端时间戳更新 => PreferNewerTimestamp 下远端胜出。
            CloudSaveData remote = Make("slot", new byte[] { 2 }, 1, 300);
            backend.Seed(remote);

            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            manager.DefaultPolicy = ConflictResolution.PreferNewerTimestamp;
            CloudSaveData local = Make("slot", new byte[] { 1 }, 2, 100);

            CloudSaveData conflictLocal = null;
            CloudSaveData conflictRemote = null;
            manager.OnConflict += (l, r) => { conflictLocal = l; conflictRemote = r; };

            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.AreSame(local, conflictLocal, "OnConflict 的 local 参数应为传入的本地存档。");
            Assert.AreSame(remote, conflictRemote, "OnConflict 的 remote 参数应为下载到的远端存档。");
            Assert.AreSame(remote, resolved, "PreferNewerTimestamp 下时间戳更新的远端应胜出。");
        }

        [Test]
        public void Sync_PreferNewerTimestamp_PicksNewerLocal_AndUploadsIt()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            CloudSaveData remote = Make("slot", new byte[] { 2 }, 5, 100);
            backend.Seed(remote);

            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            manager.DefaultPolicy = ConflictResolution.PreferNewerTimestamp;
            CloudSaveData local = Make("slot", new byte[] { 1 }, 1, 500); // 本地时间戳更新

            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.AreSame(local, resolved, "本地时间戳更新，应胜出。");

            // 胜者不同于远端 => 应回写本地到后端。
            CloudSaveData remoteNow = null;
            backend.Download("slot", d => remoteNow = d);
            Assert.AreSame(local, remoteNow, "胜者（本地）应被回写到后端。");
        }

        [Test]
        public void Sync_PreferLocal_UploadsLocal_OverwritesRemote()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            backend.Seed(Make("slot", new byte[] { 9 }, 99, 9999));

            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            manager.DefaultPolicy = ConflictResolution.PreferLocal;
            CloudSaveData local = Make("slot", new byte[] { 1 }, 1, 1);

            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.AreSame(local, resolved);

            CloudSaveData remoteNow = null;
            backend.Download("slot", d => remoteNow = d);
            Assert.AreSame(local, remoteNow, "PreferLocal 胜者应覆盖远端。");
        }

        [Test]
        public void Sync_RemoteWins_DoesNotUploadBack()
        {
            // 远端胜出且与远端相同 => 不应触发上传（不改变远端内容）。
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            CloudSaveData remote = Make("slot", new byte[] { 2 }, 1, 300);
            backend.Seed(remote);

            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            manager.DefaultPolicy = ConflictResolution.PreferRemote;
            CloudSaveData local = Make("slot", new byte[] { 1 }, 2, 100);

            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.AreSame(remote, resolved);

            CloudSaveData remoteNow = null;
            backend.Download("slot", d => remoteNow = d);
            Assert.AreSame(remote, remoteNow, "远端胜出时远端内容应保持不变。");
        }

        [Test]
        public void Sync_IdenticalRemote_NoConflictFired()
        {
            // 远端与本地版本+字节相同 => 不相异，不触发 OnConflict。
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            backend.Seed(Make("slot", new byte[] { 1, 2, 3 }, 5, 100));

            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            bool conflictFired = false;
            manager.OnConflict += (l, r) => conflictFired = true;

            CloudSaveData local = Make("slot", new byte[] { 1, 2, 3 }, 5, 999); // 仅时间戳不同
            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.IsFalse(conflictFired, "版本与字节相同则不应视为冲突。");
            Assert.IsNotNull(resolved);
        }

        [Test]
        public void Sync_ManualPolicy_UsesManualResolver()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            CloudSaveData remote = Make("slot", new byte[] { 2 }, 1, 300);
            backend.Seed(remote);

            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            manager.DefaultPolicy = ConflictResolution.Manual;
            manager.ManualResolver = (l, r) => r; // 总是选远端

            CloudSaveData local = Make("slot", new byte[] { 1 }, 2, 100);
            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.AreSame(remote, resolved, "Manual 策略应采用解析器选择的远端。");
        }

        [Test]
        public void Sync_NullBackend_ResolvesToLocal()
        {
            // NullCloudSaveBackend：下载恒 null => 胜者为本地。
            NullCloudSaveBackend backend = new NullCloudSaveBackend();
            Assert.IsFalse(backend.IsAvailable);

            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            CloudSaveData local = Make("slot", new byte[] { 1 }, 1, 100);

            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.AreSame(local, resolved, "空后端无远端，胜者应为本地。");
        }

        [Test]
        public void Sync_PassesLocalKeyToDownload()
        {
            // 验证 Download 使用 local.Key 定位远端：不同键的预置数据不应被取到。
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            backend.Seed(Make("other", new byte[] { 9 }, 9, 9));

            CloudSaveManager manager = new CloudSaveManager();
            manager.SetBackend(backend);
            CloudSaveData local = Make("slot", new byte[] { 1 }, 1, 100);

            CloudSaveData resolved = null;
            manager.Sync(local, w => resolved = w);

            Assert.AreSame(local, resolved, "应按 local.Key='slot' 查询，命中不到 'other'，故胜者为本地。");
        }
    }
}
