//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.CloudSave;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.CloudSave
{
    /// <summary>
    /// 针对后端实现的单元测试：<see cref="MemoryCloudSaveBackend"/> 的 Seed/Upload/Download 往返，
    /// 以及 <see cref="NullCloudSaveBackend"/> 的不可用语义。
    /// </summary>
    [TestFixture]
    public class CloudSaveBackendTests
    {
        private static CloudSaveData Make(string key, byte[] data, long version, long ts)
        {
            return new CloudSaveData(key, data, version, ts);
        }

        [Test]
        public void Memory_SeedThenDownload_ReturnsSeeded()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            CloudSaveData seeded = Make("slot", new byte[] { 9 }, 3, 123);
            backend.Seed(seeded);

            CloudSaveData got = null;
            backend.Download("slot", d => got = d);

            Assert.AreSame(seeded, got);
        }

        [Test]
        public void Memory_UploadThenDownload_RoundTrip()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            CloudSaveData uploaded = Make("slot", new byte[] { 1, 2 }, 1, 50);

            bool uploadOk = false;
            backend.Upload(uploaded, ok => uploadOk = ok);
            Assert.IsTrue(uploadOk);

            CloudSaveData got = null;
            backend.Download("slot", d => got = d);
            Assert.AreSame(uploaded, got);
        }

        [Test]
        public void Memory_DownloadMissingKey_ReturnsNull()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();

            bool called = false;
            CloudSaveData got = null;
            backend.Download("nope", d => { called = true; got = d; });

            Assert.IsTrue(called, "回调应被同步触发。");
            Assert.IsNull(got);
        }

        [Test]
        public void Memory_Unavailable_UploadFails_DownloadNull()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            backend.Seed(Make("slot", new byte[] { 1 }, 1, 10));
            backend.IsAvailable = false;

            bool uploadOk = true;
            backend.Upload(Make("slot", new byte[] { 2 }, 2, 20), ok => uploadOk = ok);
            Assert.IsFalse(uploadOk, "不可用时上传应失败。");

            CloudSaveData got = Make("x", null, 0, 0);
            backend.Download("slot", d => got = d);
            Assert.IsNull(got, "不可用时下载应返回 null。");
        }

        [Test]
        public void Memory_IsAvailable_DefaultsTrue()
        {
            MemoryCloudSaveBackend backend = new MemoryCloudSaveBackend();
            Assert.IsTrue(backend.IsAvailable);
        }

        [Test]
        public void Null_IsAvailable_False()
        {
            NullCloudSaveBackend backend = new NullCloudSaveBackend();
            Assert.IsFalse(backend.IsAvailable);
        }

        [Test]
        public void Null_Upload_CallsBackFalse()
        {
            NullCloudSaveBackend backend = new NullCloudSaveBackend();
            bool ok = true;
            backend.Upload(Make("k", new byte[] { 1 }, 1, 1), result => ok = result);
            Assert.IsFalse(ok);
        }

        [Test]
        public void Null_Download_CallsBackNull()
        {
            NullCloudSaveBackend backend = new NullCloudSaveBackend();
            bool called = false;
            CloudSaveData got = Make("x", null, 0, 0);
            backend.Download("k", d => { called = true; got = d; });
            Assert.IsTrue(called);
            Assert.IsNull(got);
        }
    }
}
