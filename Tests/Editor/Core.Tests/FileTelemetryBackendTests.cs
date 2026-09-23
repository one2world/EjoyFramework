//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using EjoyFramework.Core.Telemetry;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Tests
{
    /// <summary>WS4-M1：文件遥测后端——写出内容与批次一致、回调在线程池、超出 MaxFiles 删最旧。</summary>
    public sealed class FileTelemetryBackendTests
    {
        private string m_Dir;

        [SetUp]
        public void SetUp()
        {
            m_Dir = Path.Combine(TestTempPaths.Root, "telemetry_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Dir)) Directory.Delete(m_Dir, true);
        }

        private static bool SendAndWait(FileTelemetryBackend backend, TelemetryBatch batch)
        {
            bool result = false;
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                backend.Send(batch, (b, ok) => { result = ok; done.Set(); });
                Assert.IsTrue(done.WaitOne(5000), "回调超时。");
            }

            return result;
        }

        private static TelemetryBatch MakeBatch(int id, int length)
        {
            TelemetryBatch batch = new TelemetryBatch();
            batch.BatchId = id;
            batch.SessionId = "sess";
            batch.Payload = new byte[length + 16];   // 故意比 Length 长，验证只写 [0, Length)
            for (int i = 0; i < batch.Payload.Length; i++) batch.Payload[i] = (byte)(i + id);
            batch.Length = length;
            return batch;
        }

        [Test]
        public void Send_WritesExactPayloadRange()
        {
            FileTelemetryBackend backend = new FileTelemetryBackend(m_Dir);
            TelemetryBatch batch = MakeBatch(7, 100);
            Assert.IsTrue(SendAndWait(backend, batch));

            string path = Path.Combine(m_Dir, "sess-000007.ejtm");
            Assert.IsTrue(File.Exists(path));
            byte[] bytes = File.ReadAllBytes(path);
            Assert.AreEqual(100, bytes.Length);
            for (int i = 0; i < 100; i++) Assert.AreEqual(batch.Payload[i], bytes[i]);
        }

        [Test]
        public void MaxFiles_TrimsOldest()
        {
            FileTelemetryBackend backend = new FileTelemetryBackend(m_Dir);
            backend.MaxFiles = 3;
            for (int i = 1; i <= 5; i++)
            {
                Assert.IsTrue(SendAndWait(backend, MakeBatch(i, 8)));
                // 创建时间分辨率在部分文件系统上较粗，显式错开，保证"最旧"确定
                File.SetCreationTimeUtc(Path.Combine(m_Dir, "sess-" + i.ToString("D6") + ".ejtm"), new DateTime(2020, 1, 1).AddMinutes(i));
            }

            string[] files = Directory.GetFiles(m_Dir, "*.ejtm");
            Assert.AreEqual(3, files.Length);
            Assert.IsFalse(File.Exists(Path.Combine(m_Dir, "sess-000001.ejtm")));
            Assert.IsFalse(File.Exists(Path.Combine(m_Dir, "sess-000002.ejtm")));
            Assert.IsTrue(File.Exists(Path.Combine(m_Dir, "sess-000005.ejtm")));
        }

        [Test]
        public void EmptyDirectory_Throws()
        {
            Assert.Throws<EjoyFramework.Core.FrameworkException>(() => new FileTelemetryBackend(""));
        }
    }
}
