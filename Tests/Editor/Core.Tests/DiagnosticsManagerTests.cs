//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Diagnostics;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// DiagnosticsManager 单测。
    /// 验证：Log 分发到各 Sink；MinLevel 过滤低等级；Error/Exception 转发崩溃上报器；
    /// 多 Sink 全收；RemoveSink；ReportException 构造条目；某 Sink 抛异常不破坏整体分发。
    /// </summary>
    public class DiagnosticsManagerTests
    {
        /// <summary>记录收到条目的假 Sink。</summary>
        private sealed class FakeSink : ILogSink
        {
            public readonly List<DiagEntry> Entries = new List<DiagEntry>();
            public int FlushCount;

            public void OnLog(in DiagEntry entry) { Entries.Add(entry); }
            public void Flush() { FlushCount++; }
        }

        /// <summary>OnLog 抛异常的坏 Sink。</summary>
        private sealed class ThrowingSink : ILogSink
        {
            public void OnLog(in DiagEntry entry) { throw new InvalidOperationException("boom"); }
            public void Flush() { throw new InvalidOperationException("boom"); }
        }

        /// <summary>记录上报的假崩溃上报器。</summary>
        private sealed class FakeReporter : ICrashReporter
        {
            public readonly List<string> Reports = new List<string>();
            public string LastStack;
            public string LastUserKey;
            public string LastUserValue;

            public void SetUserKey(string key, string value) { LastUserKey = key; LastUserValue = value; }
            public void Report(string message, string stackTrace) { Reports.Add(message); LastStack = stackTrace; }
        }

        private static DiagnosticsManager NewManager()
        {
            return new DiagnosticsManager();
        }

        [Test]
        public void Log_RoutesToSink()
        {
            var m = NewManager();
            var sink = new FakeSink();
            m.AddSink(sink);

            m.Log(DiagLevel.Info, "hello", "stack");

            Assert.AreEqual(1, sink.Entries.Count);
            Assert.AreEqual(DiagLevel.Info, sink.Entries[0].Level);
            Assert.AreEqual("hello", sink.Entries[0].Message);
            Assert.AreEqual("stack", sink.Entries[0].StackTrace);
            Assert.Greater(sink.Entries[0].UnixMillis, 0L, "应填充 Unix 毫秒时间戳");
        }

        [Test]
        public void MinLevel_FiltersLowerLevels()
        {
            var m = NewManager();
            var sink = new FakeSink();
            m.AddSink(sink);
            m.MinLevel = DiagLevel.Warning;

            m.Log(DiagLevel.Debug, "d");
            m.Log(DiagLevel.Info, "i");
            m.Log(DiagLevel.Warning, "w");
            m.Log(DiagLevel.Error, "e");

            Assert.AreEqual(2, sink.Entries.Count, "仅 Warning 及以上应通过");
            Assert.AreEqual(DiagLevel.Warning, sink.Entries[0].Level);
            Assert.AreEqual(DiagLevel.Error, sink.Entries[1].Level);
        }

        [Test]
        public void ErrorAndException_ForwardedToCrashReporter()
        {
            var m = NewManager();
            var reporter = new FakeReporter();
            m.SetCrashReporter(reporter);

            m.Log(DiagLevel.Info, "info");       // 不转发
            m.Log(DiagLevel.Warning, "warn");    // 不转发
            m.Log(DiagLevel.Error, "err");       // 转发
            m.Log(DiagLevel.Exception, "exc");   // 转发

            Assert.AreEqual(2, reporter.Reports.Count);
            Assert.AreEqual("err", reporter.Reports[0]);
            Assert.AreEqual("exc", reporter.Reports[1]);
        }

        [Test]
        public void MultipleSinks_AllReceive()
        {
            var m = NewManager();
            var a = new FakeSink();
            var b = new FakeSink();
            m.AddSink(a);
            m.AddSink(b);

            m.Log(DiagLevel.Error, "x");

            Assert.AreEqual(1, a.Entries.Count);
            Assert.AreEqual(1, b.Entries.Count);
        }

        [Test]
        public void RemoveSink_StopsRouting()
        {
            var m = NewManager();
            var sink = new FakeSink();
            m.AddSink(sink);

            Assert.IsTrue(m.RemoveSink(sink));
            Assert.IsFalse(m.RemoveSink(sink), "重复移除应返回 false");

            m.Log(DiagLevel.Error, "after-remove");

            Assert.AreEqual(0, sink.Entries.Count);
        }

        [Test]
        public void ReportException_BuildsEntryAndForwards()
        {
            var m = NewManager();
            var sink = new FakeSink();
            var reporter = new FakeReporter();
            m.AddSink(sink);
            m.SetCrashReporter(reporter);

            Exception ex;
            try { throw new InvalidOperationException("kaboom"); }
            catch (Exception caught) { ex = caught; }

            m.ReportException(ex, "while-loading");

            Assert.AreEqual(1, sink.Entries.Count);
            Assert.AreEqual(DiagLevel.Exception, sink.Entries[0].Level);
            StringAssert.Contains("while-loading", sink.Entries[0].Message);
            StringAssert.Contains("kaboom", sink.Entries[0].Message);
            Assert.IsNotNull(sink.Entries[0].StackTrace, "已抛出的异常应带堆栈");

            Assert.AreEqual(1, reporter.Reports.Count, "异常应转发给崩溃上报器");
        }

        [Test]
        public void SinkThrows_DoesNotBreakRouting()
        {
            var m = NewManager();
            var good1 = new FakeSink();
            var bad = new ThrowingSink();
            var good2 = new FakeSink();
            m.AddSink(good1);
            m.AddSink(bad);
            m.AddSink(good2);

            Assert.DoesNotThrow(() => m.Log(DiagLevel.Error, "resilient"));

            Assert.AreEqual(1, good1.Entries.Count);
            Assert.AreEqual(1, good2.Entries.Count, "坏 Sink 抛异常不应阻断其后的 Sink");
        }

        [Test]
        public void FlushAll_FlushesAllSinks()
        {
            var m = NewManager();
            var a = new FakeSink();
            var b = new FakeSink();
            m.AddSink(a);
            m.AddSink(b);

            m.FlushAll();

            Assert.AreEqual(1, a.FlushCount);
            Assert.AreEqual(1, b.FlushCount);
        }

        [Test]
        public void SetUserKey_ForwardsToReporter()
        {
            var m = NewManager();
            var reporter = new FakeReporter();
            m.SetCrashReporter(reporter);

            m.SetUserKey("uid", "42");

            Assert.AreEqual("uid", reporter.LastUserKey);
            Assert.AreEqual("42", reporter.LastUserValue);
        }

        [Test]
        public void SinkRegisteredDuringDispatch_DoesNotBreakCurrentDispatch()
        {
            // 在一个 Sink 的回调里注册新 Sink：当前分发走快照，不应抛异常或影响本次分发。
            var m = NewManager();
            var added = new FakeSink();
            var registrar = new RegisteringSink(m, added);
            m.AddSink(registrar);

            Assert.DoesNotThrow(() => m.Log(DiagLevel.Info, "first"));
            // 新 Sink 在本次分发后才生效。
            m.Log(DiagLevel.Info, "second");

            Assert.AreEqual(1, added.Entries.Count, "新注册的 Sink 只应收到其注册之后的日志");
        }

        /// <summary>回调内注册另一个 Sink，验证分发期间增删 Sink 的安全性。</summary>
        private sealed class RegisteringSink : ILogSink
        {
            private readonly DiagnosticsManager m_Manager;
            private readonly ILogSink m_ToAdd;
            private bool m_Done;

            public RegisteringSink(DiagnosticsManager manager, ILogSink toAdd)
            {
                m_Manager = manager;
                m_ToAdd = toAdd;
            }

            public void OnLog(in DiagEntry entry)
            {
                if (m_Done) return;
                m_Done = true;
                m_Manager.AddSink(m_ToAdd);
            }

            public void Flush() { }
        }
    }
}
