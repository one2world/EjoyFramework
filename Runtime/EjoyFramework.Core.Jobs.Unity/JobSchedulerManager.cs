//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

using EjoyFramework.Core;
namespace EjoyFramework.Core.Jobs.Unity
{
    /// <summary>
    /// Job 调度管理器。封装 Unity C# Job System 的常见样板（见 <see cref="IJobSchedulerManager"/>）。
    /// 作为 FrameworkModule 接入：每帧 <see cref="LateUpdate"/> 统一完成本帧登记的延迟句柄，实现"早调度、晚完成"。
    /// </summary>
    public sealed class JobSchedulerManager : FrameworkModule, IJobSchedulerManager
    {
        // 本帧登记、待 LateUpdate 完成的句柄。每帧末清空。
        private readonly List<JobHandle> m_Pending = new List<JobHandle>(32);

        /// <summary>构造管理器。</summary>
        public JobSchedulerManager()
        {
        }

        /// <summary>优先级保持默认 0。</summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <inheritdoc />
        public int PendingCount
        {
            get { return m_Pending.Count; }
        }

        /// <summary>无每帧主动工作；延迟完成在 LateUpdate 收口。</summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>帧末统一完成所有登记的延迟句柄。</summary>
        public override void LateUpdate(float elapseSeconds, float realElapseSeconds)
        {
            CompleteAll();
        }

        /// <summary>关闭模块：完成并清空所有登记句柄，避免遗留未完成的 Job。</summary>
        public override void Shutdown()
        {
            CompleteAll();
        }

        /// <inheritdoc />
        public void CompleteInLateUpdate(JobHandle handle)
        {
            Framework.EnsureMainThread(nameof(CompleteInLateUpdate));
            // default(JobHandle) 视为已完成，无需登记。
            if (handle.Equals(default(JobHandle)))
            {
                return;
            }
            m_Pending.Add(handle);
        }

        /// <inheritdoc />
        public void CompleteAll()
        {
            // 逐个 Complete（阻塞直至完成），再清空。异常隔离：单个 Complete 抛出不应漏掉其余。
            for (int i = 0; i < m_Pending.Count; i++)
            {
                try
                {
                    m_Pending[i].Complete();
                }
                catch (System.Exception ex)
                {
                    FrameworkLog.Error("JobSchedulerManager: JobHandle.Complete threw: {0}", ex);
                }
            }
            m_Pending.Clear();
        }

        /// <inheritdoc />
        public JobHandle Combine(JobHandle a, JobHandle b)
        {
            return JobHandle.CombineDependencies(a, b);
        }

        /// <inheritdoc />
        public JobHandle Combine(JobHandle a, JobHandle b, JobHandle c)
        {
            return JobHandle.CombineDependencies(a, b, c);
        }

        /// <inheritdoc />
        public JobHandle Combine(params JobHandle[] handles)
        {
            if (handles == null || handles.Length == 0)
            {
                return default;
            }
            if (handles.Length == 1)
            {
                return handles[0];
            }
            if (handles.Length == 2)
            {
                return JobHandle.CombineDependencies(handles[0], handles[1]);
            }

            var array = new NativeArray<JobHandle>(handles.Length, Allocator.Temp);
            try
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    array[i] = handles[i];
                }
                return JobHandle.CombineDependencies(array);
            }
            finally
            {
                array.Dispose();
            }
        }

        /// <inheritdoc />
        public IEnumerator WaitFor(JobHandle handle)
        {
            while (!handle.IsCompleted)
            {
                yield return null;
            }
            // IsCompleted 为 true 后仍需 Complete() 才能正式落定（释放安全系统句柄）。
            handle.Complete();
        }

#if UNITY_2023_1_OR_NEWER
        /// <inheritdoc />
        public async UnityEngine.Awaitable WaitForAsync(JobHandle handle, System.Threading.CancellationToken cancellationToken = default)
        {
            while (!handle.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UnityEngine.Awaitable.NextFrameAsync(cancellationToken);
            }
            handle.Complete();
        }
#endif
    }
}
