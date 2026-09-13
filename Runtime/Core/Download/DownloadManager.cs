//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Download
{
    /// <summary>
    /// 下载管理器（FrameworkModule 实现）。引擎无关、可独立单元测试。
    ///
    /// 职责：
    ///   1) 维护一组下载代理（<see cref="DownloadAgent"/>，每个包一个由 Unity 层注入的 <see cref="IDownloadAgentHelper"/>）；
    ///   2) 接收下载任务并入队（<see cref="AddDownload"/>），按 <see cref="MaxConcurrentDownloads"/> 与可用代理数把等待任务分配给空闲代理；
    ///   3) 每帧 <see cref="Update"/> 驱动工作中代理的 helper 轮询，并在其完成 / 失败时收口；
    ///   4) helper 报错时按 <see cref="RetryCount"/> <b>续传重试</b>（从已累计字节处续传），重试耗尽才触发 <see cref="DownloadFailure"/>。
    ///
    /// 线程：所有 API 与回调都约定在主线程（与 HTTP 模块一致）。
    /// </summary>
    public sealed class DownloadManager : FrameworkModule, IDownloadManager
    {
        /// <summary>默认最大并发下载数。</summary>
        private const int DefaultMaxConcurrentDownloads = 3;

        /// <summary>默认单任务最大重试次数。</summary>
        private const int DefaultRetryCount = 2;

        private readonly List<DownloadAgent> m_Agents;
        private readonly Queue<DownloadTask> m_WaitingTasks;
        private int m_MaxConcurrentDownloads = DefaultMaxConcurrentDownloads;
        private int m_RetryCount = DefaultRetryCount;
        private int m_SerialId;

        /// <summary>构造下载管理器。</summary>
        public DownloadManager()
        {
            m_Agents = new List<DownloadAgent>();
            m_WaitingTasks = new Queue<DownloadTask>();
            m_SerialId = 0;
        }

        /// <summary>模块轮询优先级，保持默认 0。</summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>该模块需要外部注入下载代理辅助器（<see cref="AddAgentHelper"/>）才能工作。</summary>
        public override bool RequiresConfiguration
        {
            get { return true; }
        }

        /// <summary>是否已完成配置：至少注入过一个代理。</summary>
        public override bool IsModuleConfigured
        {
            get { return m_Agents.Count > 0; }
        }

        /// <summary>未配置时的修复提示。</summary>
        public override string ConfigurationHint
        {
            get { return "Call AddAgentHelper(...) once per concurrent slot before adding downloads."; }
        }

        /// <inheritdoc />
        public int MaxConcurrentDownloads
        {
            get { return m_MaxConcurrentDownloads; }
            set { m_MaxConcurrentDownloads = value < 1 ? 1 : value; }
        }

        /// <inheritdoc />
        public int RetryCount
        {
            get { return m_RetryCount; }
            set { m_RetryCount = value < 0 ? 0 : value; }
        }

        /// <inheritdoc />
        public int TotalAgentCount
        {
            get { return m_Agents.Count; }
        }

        /// <inheritdoc />
        public int FreeAgentCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < m_Agents.Count; i++)
                {
                    if (!m_Agents[i].IsWorking) n++;
                }
                return n;
            }
        }

        /// <inheritdoc />
        public int WorkingAgentCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < m_Agents.Count; i++)
                {
                    if (m_Agents[i].IsWorking) n++;
                }
                return n;
            }
        }

        /// <inheritdoc />
        public int WaitingTaskCount
        {
            get { return m_WaitingTasks.Count; }
        }

        /// <summary>
        /// 模块轮询：驱动每个工作中代理的 helper.Update（其内部会回调 Complete / Error，进而由本模块收口）。
        /// 收口可能腾出代理，因此随后再尝试分配等待任务。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            bool anyFreed = false;
            for (int i = 0; i < m_Agents.Count; i++)
            {
                DownloadAgent agent = m_Agents[i];
                if (agent.IsWorking)
                {
                    bool wasWorking = agent.IsWorking;
                    agent.Update(elapseSeconds, realElapseSeconds);
                    // helper 在 Update 内同步回调使代理被释放
                    if (wasWorking && !agent.IsWorking) anyFreed = true;
                }
            }

            if (anyFreed)
            {
                AssignWaitingTasks();
            }
        }

        /// <summary>关闭模块：重置所有代理、清空等待队列与事件订阅由订阅方负责释放。</summary>
        public override void Shutdown()
        {
            for (int i = 0; i < m_Agents.Count; i++)
            {
                m_Agents[i].Shutdown();
            }
            m_Agents.Clear();
            m_WaitingTasks.Clear();
        }

        /// <inheritdoc />
        public void AddAgentHelper(IDownloadAgentHelper helper)
        {
            Framework.EnsureMainThread(nameof(AddAgentHelper));
            if (helper == null)
            {
                throw new ArgumentNullException(nameof(helper));
            }

            DownloadAgent agent = new DownloadAgent(this, helper);
            m_Agents.Add(agent);
            // 新代理空闲，尝试拉起等待任务。
            AssignWaitingTasks();
        }

        /// <inheritdoc />
        public int AddDownload(string downloadUri, string savePath, object userData = null)
        {
            Framework.EnsureMainThread(nameof(AddDownload));
            if (string.IsNullOrEmpty(downloadUri))
            {
                throw new ArgumentException("Download uri is invalid.", nameof(downloadUri));
            }
            if (string.IsNullOrEmpty(savePath))
            {
                throw new ArgumentException("Save path is invalid.", nameof(savePath));
            }

            int serialId = ++m_SerialId;
            DownloadTask task = new DownloadTask(serialId, downloadUri, savePath, userData);
            m_WaitingTasks.Enqueue(task);
            AssignWaitingTasks();
            return serialId;
        }

        /// <inheritdoc />
        public bool RemoveDownload(int serialId)
        {
            Framework.EnsureMainThread(nameof(RemoveDownload));

            // 1) 正在下载的：中止其代理并释放槽，再补位。
            for (int i = 0; i < m_Agents.Count; i++)
            {
                DownloadAgent agent = m_Agents[i];
                if (agent.IsWorking && agent.CurrentTask.SerialId == serialId)
                {
                    agent.Reset();
                    AssignWaitingTasks();
                    return true;
                }
            }

            // 2) 等待队列中的：重建队列，剔除目标任务（Queue 无随机删除，整体重排）。
            int before = m_WaitingTasks.Count;
            if (before == 0)
            {
                return false;
            }

            bool removed = false;
            for (int i = 0; i < before; i++)
            {
                DownloadTask task = m_WaitingTasks.Dequeue();
                if (!removed && task.SerialId == serialId)
                {
                    removed = true;
                    continue;
                }
                m_WaitingTasks.Enqueue(task);
            }
            return removed;
        }

        /// <inheritdoc />
        public void RemoveAllDownloads()
        {
            Framework.EnsureMainThread(nameof(RemoveAllDownloads));
            m_WaitingTasks.Clear();
            for (int i = 0; i < m_Agents.Count; i++)
            {
                if (m_Agents[i].IsWorking)
                {
                    m_Agents[i].Reset();
                }
            }
        }

        /// <inheritdoc />
        public event Action<DownloadStartEventArgs> DownloadStart;

        /// <inheritdoc />
        public event Action<DownloadUpdateEventArgs> DownloadUpdate;

        /// <inheritdoc />
        public event Action<DownloadSuccessEventArgs> DownloadSuccess;

        /// <inheritdoc />
        public event Action<DownloadFailureEventArgs> DownloadFailure;

        // ===== 内部：分配 =====

        // 当前允许同时下载的代理数（受并发上限与代理总数双重约束）。
        private int EffectiveConcurrency
        {
            get { return m_MaxConcurrentDownloads < m_Agents.Count ? m_MaxConcurrentDownloads : m_Agents.Count; }
        }

        // 由代理在成功 / 失败收口后调用：腾出的代理立即承接下一个等待任务。
        private void NotifyAgentFreed()
        {
            AssignWaitingTasks();
        }

        // 把等待任务尽可能分配给空闲代理：受 EffectiveConcurrency（已工作数）封顶。
        private void AssignWaitingTasks()
        {
            if (m_WaitingTasks.Count == 0)
            {
                return;
            }

            int limit = EffectiveConcurrency;
            int agentIndex = 0;
            while (m_WaitingTasks.Count > 0 && WorkingAgentCount < limit)
            {
                DownloadAgent free = NextFreeAgentFrom(ref agentIndex);
                if (free == null)
                {
                    break;
                }
                DownloadTask task = m_WaitingTasks.Dequeue();
                free.Start(task);
            }
        }

        // 从给定位置起找下一个空闲代理；推进游标避免重复扫描已检查过的代理。
        private DownloadAgent NextFreeAgentFrom(ref int startIndex)
        {
            for (int i = startIndex; i < m_Agents.Count; i++)
            {
                if (!m_Agents[i].IsWorking)
                {
                    startIndex = i + 1;
                    return m_Agents[i];
                }
            }
            startIndex = m_Agents.Count;
            return null;
        }

        // ===== 内部：事件触发 =====

        private void RaiseStart(DownloadTask task)
        {
            DownloadStart?.Invoke(new DownloadStartEventArgs
            {
                SerialId = task.SerialId,
                DownloadUri = task.DownloadUri,
                SavePath = task.SavePath,
                UserData = task.UserData,
            });
        }

        private void RaiseUpdate(DownloadTask task, long currentLength)
        {
            DownloadUpdate?.Invoke(new DownloadUpdateEventArgs
            {
                SerialId = task.SerialId,
                DownloadUri = task.DownloadUri,
                SavePath = task.SavePath,
                UserData = task.UserData,
                CurrentLength = currentLength,
            });
        }

        private void RaiseSuccess(DownloadTask task)
        {
            DownloadSuccess?.Invoke(new DownloadSuccessEventArgs
            {
                SerialId = task.SerialId,
                DownloadUri = task.DownloadUri,
                SavePath = task.SavePath,
                UserData = task.UserData,
            });
        }

        private void RaiseFailure(DownloadTask task, string errorMessage)
        {
            DownloadFailure?.Invoke(new DownloadFailureEventArgs
            {
                SerialId = task.SerialId,
                DownloadUri = task.DownloadUri,
                SavePath = task.SavePath,
                UserData = task.UserData,
                ErrorMessage = errorMessage,
            });
        }

        // ===== 内部：任务 =====

        /// <summary>一个排队 / 在途的下载任务的不可变描述。</summary>
        private sealed class DownloadTask
        {
            public readonly int SerialId;
            public readonly string DownloadUri;
            public readonly string SavePath;
            public readonly object UserData;

            public DownloadTask(int serialId, string downloadUri, string savePath, object userData)
            {
                SerialId = serialId;
                DownloadUri = downloadUri;
                SavePath = savePath;
                UserData = userData;
            }
        }

        // ===== 内部：代理 =====

        /// <summary>
        /// 下载代理：包一个 <see cref="IDownloadAgentHelper"/>，订阅其 3 个事件，跟踪当前任务、累计字节与重试计数。
        /// 一个代理同一时刻只处理一个任务（<see cref="IsWorking"/> 标识占用）。
        /// </summary>
        private sealed class DownloadAgent
        {
            private readonly DownloadManager m_Owner;
            private readonly IDownloadAgentHelper m_Helper;
            private DownloadTask m_Task;
            private bool m_Working;
            private long m_DownloadedBytes;
            private int m_RetriedCount;

            public DownloadAgent(DownloadManager owner, IDownloadAgentHelper helper)
            {
                m_Owner = owner;
                m_Helper = helper;
                m_Helper.DownloadAgentHelperUpdate += OnHelperUpdate;
                m_Helper.DownloadAgentHelperComplete += OnHelperComplete;
                m_Helper.DownloadAgentHelperError += OnHelperError;
            }

            public bool IsWorking
            {
                get { return m_Working; }
            }

            public DownloadTask CurrentTask
            {
                get { return m_Task; }
            }

            /// <summary>开始处理一个任务（从头下载）。</summary>
            public void Start(DownloadTask task)
            {
                m_Task = task;
                m_Working = true;
                m_DownloadedBytes = 0L;
                m_RetriedCount = 0;
                m_Owner.RaiseStart(task);
                m_Helper.Download(task.DownloadUri, m_DownloadedBytes, task.SavePath);
            }

            /// <summary>逐帧推进 helper（其内部回调驱动收口）。</summary>
            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                if (!m_Working)
                {
                    return;
                }
                m_Helper.Update(elapseSeconds, realElapseSeconds);
            }

            /// <summary>中止当前任务并释放代理（不触发成功 / 失败事件）。</summary>
            public void Reset()
            {
                m_Helper.Reset();
                m_Task = null;
                m_Working = false;
                m_DownloadedBytes = 0L;
                m_RetriedCount = 0;
            }

            /// <summary>关闭代理：退订事件并重置 helper。</summary>
            public void Shutdown()
            {
                m_Helper.DownloadAgentHelperUpdate -= OnHelperUpdate;
                m_Helper.DownloadAgentHelperComplete -= OnHelperComplete;
                m_Helper.DownloadAgentHelperError -= OnHelperError;
                m_Helper.Reset();
                m_Task = null;
                m_Working = false;
            }

            private void OnHelperUpdate(IDownloadAgentHelper helper, long deltaBytes)
            {
                if (!m_Working || m_Task == null)
                {
                    return;
                }
                if (deltaBytes > 0L)
                {
                    m_DownloadedBytes += deltaBytes;
                }
                m_Owner.RaiseUpdate(m_Task, m_DownloadedBytes);
            }

            private void OnHelperComplete(IDownloadAgentHelper helper, long length)
            {
                if (!m_Working || m_Task == null)
                {
                    return;
                }
                // length 为本次传输累计写入字节，校正最终累计值。
                if (length > 0L)
                {
                    m_DownloadedBytes = length;
                }
                DownloadTask finished = m_Task;
                FreeSelf();
                m_Owner.RaiseSuccess(finished);
                // 腾出代理后立即拉起下一个等待任务（不必等到下一帧 Update）。
                m_Owner.NotifyAgentFreed();
            }

            private void OnHelperError(IDownloadAgentHelper helper, string errorMessage)
            {
                if (!m_Working || m_Task == null)
                {
                    return;
                }

                if (m_RetriedCount < m_Owner.RetryCount)
                {
                    // 续传重试：从已累计字节处继续，保留同一任务与代理占用。
                    m_RetriedCount++;
                    FrameworkLog.Warning(
                        "Download retry {0}/{1} for serial {2} '{3}' (resume from {4} bytes): {5}",
                        m_RetriedCount, m_Owner.RetryCount, m_Task.SerialId, m_Task.DownloadUri,
                        m_DownloadedBytes, errorMessage);
                    m_Helper.Reset();
                    m_Helper.Download(m_Task.DownloadUri, m_DownloadedBytes, m_Task.SavePath);
                    return;
                }

                DownloadTask failed = m_Task;
                FreeSelf();
                m_Owner.RaiseFailure(failed, errorMessage);
                // 腾出代理后立即拉起下一个等待任务（不必等到下一帧 Update）。
                m_Owner.NotifyAgentFreed();
            }

            // 收口：清空在途状态、释放 helper 资源、标记空闲（不触发任何业务事件）。
            private void FreeSelf()
            {
                m_Helper.Reset();
                m_Task = null;
                m_Working = false;
                m_DownloadedBytes = 0L;
                m_RetriedCount = 0;
            }
        }
    }
}
