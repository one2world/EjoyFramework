//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace EjoyFramework.Core.Jobs.Unity
{
    /// <summary>
    /// 原生容器分配作用域：用 <c>using</c> 批量分配 <see cref="NativeArray{T}"/> 等，作用域结束时自动逆序释放，
    /// 免去逐个 Dispose 的样板与泄漏风险。可登记一个句柄，释放前先 <c>Complete()</c> 它（避免释放仍被 Job 使用的容器）。
    ///
    /// <code>
    /// using (var scope = new JobScope(Allocator.TempJob))
    /// {
    ///     var a = scope.Alloc&lt;float&gt;(1024);
    ///     var h = JobUtility.Fill(a, 1f);
    ///     scope.CompleteOnDispose(h);   // 作用域结束时先完成 h 再释放 a
    /// }   // a 自动释放
    /// </code>
    ///
    /// 默认 <see cref="Unity.Collections.Allocator.TempJob"/>（可在 Job 中使用、最多存活 4 帧、需释放——本作用域负责）。
    /// </summary>
    public sealed class JobScope : IDisposable
    {
        private readonly Allocator m_Allocator;
        private List<IDisposable> m_Tracked;   // 懒创建
        private JobHandle m_CompleteOnDispose;
        private bool m_HasCompleteHandle;
        private bool m_Disposed;

        /// <summary>构造作用域。</summary>
        public JobScope(Allocator allocator = Allocator.TempJob)
        {
            m_Allocator = allocator;
        }

        /// <summary>本作用域使用的分配器。</summary>
        public Allocator Allocator
        {
            get { return m_Allocator; }
        }

        /// <summary>分配并跟踪一个 <see cref="NativeArray{T}"/>，<see cref="Dispose"/> 时自动释放。</summary>
        public NativeArray<T> Alloc<T>(int length, NativeArrayOptions options = NativeArrayOptions.ClearMemory) where T : unmanaged
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(JobScope));
            var array = new NativeArray<T>(length, m_Allocator, options);
            Track(array);
            return array;
        }

        /// <summary>跟踪一个已存在的可释放原生容器（外部创建的 NativeArray/NativeList 等），随作用域一并释放。</summary>
        public T Adopt<T>(T disposable) where T : IDisposable
        {
            Track(disposable);
            return disposable;
        }

        /// <summary>登记一个句柄：<see cref="Dispose"/> 时先 <c>Complete()</c> 它，再释放跟踪的容器。链式返回。</summary>
        public JobScope CompleteOnDispose(JobHandle handle)
        {
            m_CompleteOnDispose = handle;
            m_HasCompleteHandle = true;
            return this;
        }

        private void Track(IDisposable disposable)
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(JobScope));
            (m_Tracked ?? (m_Tracked = new List<IDisposable>(8))).Add(disposable);
        }

        /// <summary>完成登记的句柄（若有）并逆序释放所有跟踪容器。可重复调用（幂等）。</summary>
        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            m_Disposed = true;

            if (m_HasCompleteHandle)
            {
                try { m_CompleteOnDispose.Complete(); }
                catch (Exception ex) { Core.FrameworkLog.Error("JobScope: complete-on-dispose threw: {0}", ex); }
            }

            if (m_Tracked != null)
            {
                for (int i = m_Tracked.Count - 1; i >= 0; i--)
                {
                    try { m_Tracked[i].Dispose(); }
                    catch (Exception ex) { Core.FrameworkLog.Error("JobScope: dispose tracked container threw: {0}", ex); }
                }
                m_Tracked.Clear();
            }
        }
    }
}
