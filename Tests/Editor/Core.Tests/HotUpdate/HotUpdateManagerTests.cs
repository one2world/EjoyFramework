//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.HotUpdate;

namespace EjoyFramework.Core.Tests.HotUpdate
{
    /// <summary>
    /// HotUpdateManager（iFix 补丁模型）单测。
    /// 验证：应用成功的记录/事件/幂等；loader 缺失与 loader 失败走 OnError；空字节失败；AppliedPatches 枚举；Shutdown 清账本。
    /// 全程用 FakePatchLoader 替身，不依赖 Unity / iFix。
    /// </summary>
    public class HotUpdateManagerTests
    {
        /// <summary>可编程的假补丁加载器：可设定成功/失败，并记录调用次数。</summary>
        private sealed class FakePatchLoader : IPatchLoader
        {
            public bool ShouldSucceed = true;
            public string Error = "patch-boom";
            public int CallCount;

            public bool ApplyPatch(Stream patchStream, out string error)
            {
                CallCount++;
                if (ShouldSucceed)
                {
                    error = string.Empty;
                    return true;
                }
                error = Error;
                return false;
            }
        }

        private static readonly byte[] DummyBytes = new byte[] { 1, 2, 3, 4 };

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void ApplyPatch_Success_TracksAndFiresEvent()
        {
            var mgr = new HotUpdateManager();
            mgr.SetLoader(new FakePatchLoader());

            string fired = null;
            mgr.OnPatchApplied += (m, id) => fired = id;

            bool ok = mgr.ApplyPatch("p1", DummyBytes);

            Assert.IsTrue(ok);
            Assert.AreEqual("p1", fired);
            Assert.IsTrue(mgr.IsPatchApplied("p1"));
            Assert.AreEqual(1, mgr.AppliedPatchCount);
        }

        [Test]
        public void ApplyPatch_Idempotent_SecondCallSucceedsWithoutReloading()
        {
            var loader = new FakePatchLoader();
            var mgr = new HotUpdateManager();
            mgr.SetLoader(loader);

            int eventCount = 0;
            mgr.OnPatchApplied += (m, id) => eventCount++;

            Assert.IsTrue(mgr.ApplyPatch("p1", DummyBytes));
            Assert.IsTrue(mgr.ApplyPatch("p1", DummyBytes), "同 id 重复应用应直接成功");

            Assert.AreEqual(1, loader.CallCount, "幂等：loader 只应被调用一次");
            Assert.AreEqual(1, eventCount, "幂等：事件只应触发一次");
            Assert.AreEqual(1, mgr.AppliedPatchCount);
        }

        [Test]
        public void ApplyPatch_NoLoader_FiresOnErrorAndReturnsFalse()
        {
            var mgr = new HotUpdateManager();

            string errId = null, errMsg = null;
            mgr.OnError += (m, id, msg) => { errId = id; errMsg = msg; };

            bool ok = mgr.ApplyPatch("p1", DummyBytes);

            Assert.IsFalse(ok);
            Assert.AreEqual("p1", errId);
            Assert.IsNotNull(errMsg);
            Assert.IsFalse(mgr.IsPatchApplied("p1"));
        }

        [Test]
        public void ApplyPatch_LoaderFailure_FiresOnErrorAndReturnsFalse()
        {
            var mgr = new HotUpdateManager();
            mgr.SetLoader(new FakePatchLoader { ShouldSucceed = false, Error = "bad patch" });

            string errMsg = null;
            mgr.OnError += (m, id, msg) => errMsg = msg;

            bool ok = mgr.ApplyPatch("p1", DummyBytes);

            Assert.IsFalse(ok);
            Assert.AreEqual("bad patch", errMsg, "OnError 应透传 loader 的错误信息");
            Assert.IsFalse(mgr.IsPatchApplied("p1"));
        }

        [Test]
        public void ApplyPatch_EmptyBytes_FiresOnErrorAndReturnsFalse()
        {
            var mgr = new HotUpdateManager();
            mgr.SetLoader(new FakePatchLoader());

            bool fired = false;
            mgr.OnError += (m, id, msg) => fired = true;

            Assert.IsFalse(mgr.ApplyPatch("p1", new byte[0]));
            Assert.IsTrue(fired);
        }

        [Test]
        public void ApplyPatch_Stream_Overload_Works()
        {
            var mgr = new HotUpdateManager();
            mgr.SetLoader(new FakePatchLoader());

            using (var stream = new MemoryStream(DummyBytes))
            {
                Assert.IsTrue(mgr.ApplyPatch("p-stream", stream));
            }
            Assert.IsTrue(mgr.IsPatchApplied("p-stream"));
        }

        [Test]
        public void AppliedPatches_EnumeratesInOrder()
        {
            var mgr = new HotUpdateManager();
            mgr.SetLoader(new FakePatchLoader());

            mgr.ApplyPatch("a", DummyBytes);
            mgr.ApplyPatch("b", DummyBytes);

            var ids = new List<string>(mgr.AppliedPatches);
            Assert.AreEqual(2, ids.Count);
            Assert.AreEqual("a", ids[0]);
            Assert.AreEqual("b", ids[1]);
        }

        [Test]
        public void Shutdown_ClearsAppliedLedger()
        {
            var mgr = new HotUpdateManager();
            mgr.SetLoader(new FakePatchLoader());
            mgr.ApplyPatch("a", DummyBytes);

            mgr.Shutdown();

            Assert.IsFalse(mgr.IsPatchApplied("a"));
            Assert.AreEqual(0, mgr.AppliedPatchCount);
        }
    }
}
