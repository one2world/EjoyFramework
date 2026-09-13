//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using EjoyFramework.Core.Patch;
using EjoyFramework.Core.Resource;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// PatchManager + ManifestComparer 单测（Phase 12）。
    /// </summary>
    public class PatchManagerTests
    {
        private PatchManager m_PM;
        private MockDownloadHelper m_Dl;
        private string m_TmpDir;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            // 下载顺序类用例不校验完整性元数据（合成 bundle 无 Md5），故关闭强制完整性；
            // 完整性闸门本身由 DownloadAsync_RequireIntegrity_* 用例单独覆盖。
            m_PM = new PatchManager(requireIntegrity: false);
            m_Dl = new MockDownloadHelper();
            m_PM.SetDownloadHelper(m_Dl);
            m_TmpDir = Path.Combine(Path.GetTempPath(), "ejoy_patch_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            m_PM.SetReadWritePath(m_TmpDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(m_TmpDir)) Directory.Delete(m_TmpDir, recursive: true); } catch { }
        }

        [Test]
        public void Compare_NoLocal_AllRemoteAreNew()
        {
            var remote = NewManifest(1, ("a.bundle", "hash1", 100), ("b.bundle", "hash2", 200));
            var result = ManifestComparer.Compare(local: null, remote);
            Assert.IsTrue(result.HasUpdate);
            Assert.AreEqual(2, result.PendingBundles.Length);
            Assert.AreEqual(300, result.TotalSizeBytes);
        }

        [Test]
        public void Compare_SameHash_NoUpdate()
        {
            var local = NewManifest(1, ("a.bundle", "h", 100));
            var remote = NewManifest(1, ("a.bundle", "h", 100));
            var result = ManifestComparer.Compare(local, remote);
            Assert.IsFalse(result.HasUpdate);
            Assert.AreEqual(0, result.PendingBundles.Length);
        }

        [Test]
        public void Compare_ChangedHash_DownloadDelta()
        {
            var local = NewManifest(1, ("a.bundle", "h1", 100), ("b.bundle", "h2", 200));
            var remote = NewManifest(2, ("a.bundle", "h1", 100), ("b.bundle", "h2-new", 250));
            var result = ManifestComparer.Compare(local, remote);
            Assert.IsTrue(result.HasUpdate);
            Assert.AreEqual(1, result.PendingBundles.Length);
            Assert.AreEqual("b.bundle", result.PendingBundles[0].BundleName);
            Assert.AreEqual(250, result.TotalSizeBytes);
        }

        [Test]
        public void Compare_NewBundle_Added()
        {
            var local = NewManifest(1, ("a.bundle", "h", 100));
            var remote = NewManifest(2, ("a.bundle", "h", 100), ("c.bundle", "h3", 300));
            var result = ManifestComparer.Compare(local, remote);
            Assert.IsTrue(result.HasUpdate);
            Assert.AreEqual(1, result.PendingBundles.Length);
            Assert.AreEqual("c.bundle", result.PendingBundles[0].BundleName);
        }

        [Test]
        public void DownloadAsync_SerialOrder_CallsHelperPerBundle()
        {
            var bundles = new[]
            {
                new PatchBundleInfo { BundleName = "a", RelativePath = "a.bundle", SizeBytes = 100 },
                new PatchBundleInfo { BundleName = "b", RelativePath = "b.bundle", SizeBytes = 200 },
                new PatchBundleInfo { BundleName = "c", RelativePath = "c.bundle", SizeBytes = 300 },
            };
            int completeCalls = 0;
            m_PM.DownloadAsync("https://cdn/", bundles,
                onProgress: null,
                onComplete: () => completeCalls++,
                onError: err => Assert.Fail("unexpected error: " + err));

            // MockDownloadHelper 同步完成
            Assert.AreEqual(1, completeCalls);
            Assert.AreEqual(3, m_Dl.Requests.Count);
            Assert.AreEqual("https://cdn/a.bundle", m_Dl.Requests[0].Url);
            Assert.AreEqual("https://cdn/c.bundle", m_Dl.Requests[2].Url);
        }

        [Test]
        public void DownloadAsync_EmptyList_FiresCompleteImmediately()
        {
            int completeCalls = 0;
            m_PM.DownloadAsync("https://cdn/", new PatchBundleInfo[0],
                null,
                () => completeCalls++,
                err => Assert.Fail("unexpected"));
            Assert.AreEqual(1, completeCalls);
        }

        [Test]
        public void DownloadAsync_HelperError_TriggersOnError()
        {
            m_Dl.FailNext = true;
            string err = null;
            m_PM.DownloadAsync("https://cdn/", new[]
            {
                new PatchBundleInfo { BundleName = "a", RelativePath = "a.bundle", SizeBytes = 100 },
            }, null, () => Assert.Fail("should not complete"), e => err = e);
            Assert.IsNotNull(err);
            StringAssert.Contains("a", err);
        }

        // ===== 安全闸门（C1 路径穿越 / C2 完整性降级 / HTTPS 强制）=====

        [Test]
        public void DownloadAsync_RequireIntegrity_MissingMd5_TriggersOnError()
        {
            // 强制完整性时，清单条目缺少 Md5 必须判失败，绝不静默落地未校验的 bundle。
            var pm = new PatchManager(requireIntegrity: true);
            pm.SetDownloadHelper(m_Dl);
            pm.SetReadWritePath(m_TmpDir);
            string err = null;
            pm.DownloadAsync("https://cdn/", new[]
            {
                new PatchBundleInfo { BundleName = "a", RelativePath = "a.bundle", SizeBytes = 100 }, // 无 Md5
            }, null, () => Assert.Fail("缺少完整性元数据时不应完成"), e => err = e);
            Assert.IsNotNull(err);
        }

        [Test]
        public void DownloadAsync_InsecureHttpBaseUrl_RejectedBeforeDownload()
        {
            string err = null;
            m_PM.DownloadAsync("http://cdn/", new[]
            {
                new PatchBundleInfo { BundleName = "a", RelativePath = "a.bundle", SizeBytes = 100 },
            }, null, () => Assert.Fail("不应在 http 明文通道上完成"), e => err = e);
            Assert.IsNotNull(err);
            Assert.AreEqual(0, m_Dl.Requests.Count); // 未发起任何下载
        }

        [Test]
        public void DownloadAsync_PathTraversalRelativePath_RejectedBeforeDownload()
        {
            string err = null;
            m_PM.DownloadAsync("https://cdn/", new[]
            {
                new PatchBundleInfo { BundleName = "evil", RelativePath = "../evil.bundle", SizeBytes = 100 },
            }, null, () => Assert.Fail("路径穿越应被拒绝"), e => err = e);
            Assert.IsNotNull(err);
            Assert.AreEqual(0, m_Dl.Requests.Count); // 拒绝于下载之前
        }

        [Test]
        public void ReplaceFile_WhenMoveFails_PreservesExistingDestination()
        {
            string destination = Path.Combine(m_TmpDir, "manifest.json");
            string missingSource = Path.Combine(m_TmpDir, "missing.tmp");
            File.WriteAllText(destination, "old-valid-manifest");

            MethodInfo replace = typeof(PatchManager).GetMethod(
                "ReplaceFile",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(replace);

            Assert.Throws<TargetInvocationException>(
                () => replace.Invoke(null, new object[] { missingSource, destination }));

            Assert.IsTrue(File.Exists(destination),
                "替换失败时旧 manifest 必须仍然存在，不能先删后移。 ");
            Assert.AreEqual("old-valid-manifest", File.ReadAllText(destination));
        }

        // ===== helpers =====

        private static AssetManifest NewManifest(int version, params (string name, string hash, long size)[] bundles)
        {
            var m = new AssetManifest { Version = version, Bundles = new List<BundleInfo>() };
            foreach (var (n, h, sz) in bundles)
            {
                m.Bundles.Add(new BundleInfo { Name = n, RelativePath = n, Hash = h, Size = sz });
            }
            return m;
        }

        private sealed class MockDownloadHelper : IDownloadHelper
        {
            public sealed class Req { public string Url; public string Path; }
            public readonly List<Req> Requests = new List<Req>();
            public bool FailNext;

            public void DownloadAsync(string url, string filePath,
                Action<float> onProgress,
                Action<long> onComplete,
                Action<string> onError)
            {
                Requests.Add(new Req { Url = url, Path = filePath });
                if (FailNext) { FailNext = false; onError?.Invoke("simulated network error"); return; }
                onProgress?.Invoke(0.5f);
                onProgress?.Invoke(1f);
                onComplete?.Invoke(100);   // 假装下载了 100 字节
            }
        }
    }
}
