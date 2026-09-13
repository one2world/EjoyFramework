//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using EjoyFramework.Core;
using NUnit.Framework;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 文本基建（TempText / CharBufferPool / MutableString）的多线程契约测试。
    /// 契约：三者都基于 [ThreadStatic] 线程本地池——不同线程各有独立的缓冲池与实例缓存，
    /// 同一实例不得跨线程使用；本测试验证的是"多线程各自使用时互不污染、无异常、内容正确"，
    /// 而非"跨线程共享一个实例是安全的"（那是明确禁止的用法）。
    /// </summary>
    public class TextInfraThreadingTests
    {
        private const int ThreadCount = 8;
        private const int IterationsPerThread = 5000;

        /// <summary>
        /// 在 N 个后台线程上并发执行 body，收集所有异常并等待全部结束。
        /// </summary>
        private static List<Exception> RunOnThreads(int threadCount, Action<int> body)
        {
            var errors = new List<Exception>();
            var threads = new Thread[threadCount];
            using (var startGate = new ManualResetEventSlim(false))
            {
                for (int t = 0; t < threadCount; t++)
                {
                    int threadIndex = t;
                    threads[t] = new Thread(() =>
                    {
                        startGate.Wait();
                        try
                        {
                            body(threadIndex);
                        }
                        catch (Exception ex)
                        {
                            lock (errors)
                            {
                                errors.Add(ex);
                            }
                        }
                    });
                    threads[t].IsBackground = true;
                    threads[t].Start();
                }

                // 所有线程就位后同时放行，最大化并发窗口。
                startGate.Set();
                foreach (var th in threads)
                {
                    th.Join();
                }
            }

            return errors;
        }

        [Test]
        public void TempText_ParallelThreads_NoCrossContamination()
        {
            var errors = RunOnThreads(ThreadCount, threadIndex =>
            {
                for (int i = 0; i < IterationsPerThread; i++)
                {
                    using (var t = TempText.Rent(64))
                    {
                        t.Append("T").Append(threadIndex).Append('-').Append(i).Append('/').Append(i * 3L);
                        string expected = "T" + threadIndex + "-" + i + "/" + (i * 3L);
                        string actual = t.ToString();
                        if (actual != expected)
                        {
                            throw new InvalidOperationException(
                                "cross-thread contamination: expected '" + expected + "' got '" + actual + "'");
                        }
                    }
                }
            });

            Assert.IsEmpty(errors, errors.Count > 0 ? errors[0].ToString() : null);
        }

        [Test]
        public void TempText_ParallelThreads_WithGrowth_ContentStaysCorrect()
        {
            var errors = RunOnThreads(ThreadCount, threadIndex =>
            {
                string marker = new string((char)('A' + threadIndex), 100);
                for (int i = 0; i < IterationsPerThread / 5; i++)
                {
                    // 从 64 档起步、追加 100 字符强制扩容换档，验证扩容路径下的线程隔离。
                    using (var t = TempText.Rent(64))
                    {
                        t.Append(marker).Append(i);
                        string s = t.ToString();
                        if (s.Length != 100 + i.ToString().Length || s[0] != (char)('A' + threadIndex) || s[99] != (char)('A' + threadIndex))
                        {
                            throw new InvalidOperationException("growth contamination on thread " + threadIndex + ": '" + s + "'");
                        }
                    }
                }
            });

            Assert.IsEmpty(errors, errors.Count > 0 ? errors[0].ToString() : null);
        }

        [Test]
        public void TempText_NestedRents_OnParallelThreads()
        {
            var errors = RunOnThreads(ThreadCount, threadIndex =>
            {
                for (int i = 0; i < IterationsPerThread / 10; i++)
                {
                    // 嵌套租借（超过每线程 State 缓存量）+ 内外层内容互不干扰。
                    using (var outer = TempText.Rent(64))
                    {
                        outer.Append("outer").Append(threadIndex);
                        using (var a = TempText.Rent(64))
                        using (var b = TempText.Rent(64))
                        using (var c = TempText.Rent(64))
                        {
                            a.Append(i);
                            b.Append(i + 1);
                            c.Append(i + 2);
                            if (a.ToString() != i.ToString() || b.ToString() != (i + 1).ToString() || c.ToString() != (i + 2).ToString())
                            {
                                throw new InvalidOperationException("nested rent contamination");
                            }
                        }

                        outer.Append('!');
                        if (outer.ToString() != "outer" + threadIndex + "!")
                        {
                            throw new InvalidOperationException("outer corrupted after nested rents: " + outer.ToString());
                        }
                    }
                }
            });

            Assert.IsEmpty(errors, errors.Count > 0 ? errors[0].ToString() : null);
        }

        [Test]
        public void CharBufferPool_PerThread_Isolation()
        {
            var errors = RunOnThreads(ThreadCount, threadIndex =>
            {
                for (int i = 0; i < IterationsPerThread; i++)
                {
                    char[] buf = CharBufferPool.Rent(64);
                    char stamp = (char)('a' + threadIndex);
                    buf[0] = stamp;
                    buf[63] = stamp;
                    // 让出时间片，扩大"另一线程若共享同一缓冲则必然覆写"的暴露窗口。
                    if ((i & 1023) == 0)
                    {
                        Thread.Yield();
                    }

                    if (buf[0] != stamp || buf[63] != stamp)
                    {
                        throw new InvalidOperationException("buffer shared across threads!");
                    }

                    CharBufferPool.Return(buf);
                }
            });

            Assert.IsEmpty(errors, errors.Count > 0 ? errors[0].ToString() : null);
        }

        [Test]
        public void MutableString_ParallelThreads_IndependentCaches()
        {
            var errors = RunOnThreads(ThreadCount, threadIndex =>
            {
                for (int i = 0; i < IterationsPerThread / 5; i++)
                {
                    using (var ms = MutableString.Rent(64))
                    {
                        using (var t = TempText.Rent(64))
                        {
                            t.Append("thr").Append(threadIndex).Append(':').Append(i);
                            ms.Set(t.AsSpan());
                        }

                        string expected = "thr" + threadIndex + ":" + i;
                        if (ms.Value != expected)
                        {
                            throw new InvalidOperationException(
                                "MutableString contamination: expected '" + expected + "' got '" + ms.Value + "'");
                        }

                        if (ms.Value.Length != expected.Length)
                        {
                            throw new InvalidOperationException("length field wrong on background thread");
                        }
                    }
                }
            });

            Assert.IsEmpty(errors, errors.Count > 0 ? errors[0].ToString() : null);
        }

        [Test]
        public void MutableString_BackgroundThreads_SurviveGcPressure()
        {
            // 后台线程持有借出的 MutableString（length 已被改写）期间主线程反复强制 GC，
            // 验证 unsafe 路径的"非移动 GC 假设"在真实多线程 + GC 压力下成立。
            using (var stop = new ManualResetEventSlim(false))
            {
                var errors = new List<Exception>();
                var workers = new Thread[4];
                for (int t = 0; t < workers.Length; t++)
                {
                    int threadIndex = t;
                    workers[t] = new Thread(() =>
                    {
                        try
                        {
                            int i = 0;
                            while (!stop.IsSet)
                            {
                                using (var ms = MutableString.Rent(256))
                                {
                                    ms.Set("gc-stress-" + threadIndex + "-" + i);
                                    // 在租借窗口内主动小睡，保证 GC 大概率落在窗口中。
                                    Thread.Sleep(0);
                                    if (!ms.Value.StartsWith("gc-stress-"))
                                    {
                                        throw new InvalidOperationException("corrupted under GC: " + ms.Value);
                                    }
                                }

                                i++;
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (errors)
                            {
                                errors.Add(ex);
                            }
                        }
                    });
                    workers[t].IsBackground = true;
                    workers[t].Start();
                }

                for (int g = 0; g < 20; g++)
                {
                    GC.Collect(2, GCCollectionMode.Forced, true);
                    Thread.Sleep(5);
                }

                stop.Set();
                foreach (var th in workers)
                {
                    th.Join();
                }

                Assert.IsEmpty(errors, errors.Count > 0 ? errors[0].ToString() : null);
            }
        }
    }
}
