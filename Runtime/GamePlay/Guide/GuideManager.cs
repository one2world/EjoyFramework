//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Guide
{
    /// <summary>
    /// 新手引导/教程流程管理器。纯逻辑、与引擎无关，可独立单元测试。
    /// 同一时刻至多一个流程处于运行态（<see cref="ActiveFlow"/>）。
    /// 游戏侧通过 <see cref="ReportTrigger"/> 推送触发事件以自动推进，或通过
    /// <see cref="AdvanceManually"/> 手动推进；视觉层（遮罩、高亮）由游戏侧负责。
    /// 进度以 <see cref="GuideProgress"/> 纯数据快照导出/恢复，不依赖任何存档模块。
    ///
    /// 调用顺序：先 <see cref="ImportProgress"/> 标记已完成的流程，再 <see cref="Register"/>，
    /// 这样 AutoStart 才会正确跳过已完成的流程。
    /// 作为框架模块注册，通过 <c>Framework.GetModule&lt;IGuideManager&gt;()</c> 访问。
    /// </summary>
    public sealed class GuideManager : FrameworkModule, IGuideManager
    {
        // 已注册流程，键为流程 Id，保持插入顺序由 m_Order 维护。
        private readonly Dictionary<string, GuideFlow> m_Flows;

        // 注册顺序，用于稳定枚举。
        private readonly List<GuideFlow> m_Order;

        // 已完成流程 Id 集合（含正常完成、跳过、以及 ImportProgress 导入的）。
        private readonly HashSet<string> m_Completed;

        // 当前活动流程；null 表示无流程在运行。
        private GuideFlow m_ActiveFlow;

        // 当前活动流程的步骤索引；无活动流程时为 -1。
        private int m_ActiveStepIndex;

        /// <summary>
        /// 构造引导管理器。
        /// </summary>
        public GuideManager()
        {
            m_Flows = new Dictionary<string, GuideFlow>();
            m_Order = new List<GuideFlow>();
            m_Completed = new HashSet<string>();
            m_ActiveFlow = null;
            m_ActiveStepIndex = -1;
        }

        /// <summary>
        /// 模块优先级。引导编排无时序耦合，使用默认优先级 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 框架模块轮询。引导编排为事件驱动（<see cref="ReportTrigger"/> / <see cref="AdvanceManually"/>），
        /// 无每帧工作，故此处为空实现。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒）。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理模块：清空已注册流程、注册顺序、已完成集合与活动态。
        /// </summary>
        public override void Shutdown()
        {
            m_Flows.Clear();
            m_Order.Clear();
            m_Completed.Clear();
            m_ActiveFlow = null;
            m_ActiveStepIndex = -1;

            OnStepEntered = null;
            OnStepCompleted = null;
            OnFlowStarted = null;
            OnFlowCompleted = null;
        }

        /// <summary>
        /// 当前正在运行的流程；无流程运行时为 null。
        /// </summary>
        public GuideFlow ActiveFlow
        {
            get { return m_ActiveFlow; }
        }

        /// <summary>
        /// 当前活动流程的当前步骤；无流程运行时为 null。
        /// </summary>
        public GuideStep CurrentStep
        {
            get
            {
                if (m_ActiveFlow == null)
                {
                    return null;
                }

                return m_ActiveFlow.Steps[m_ActiveStepIndex];
            }
        }

        /// <summary>
        /// 注册一个流程。若其 AutoStart 为真、未被标记为已完成、且当前无其他流程运行，则立即开始。
        /// </summary>
        /// <param name="flow">引导流程。</param>
        /// <exception cref="ArgumentNullException">flow 为 null。</exception>
        /// <exception cref="ArgumentException">流程 Id 已注册。</exception>
        public void Register(GuideFlow flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            if (m_Flows.ContainsKey(flow.Id))
            {
                throw new ArgumentException($"引导流程 Id 已存在：{flow.Id}", nameof(flow));
            }

            m_Flows.Add(flow.Id, flow);
            m_Order.Add(flow);

            // AutoStart：仅在未完成且当前无流程运行时开始。
            if (flow.AutoStart && !m_Completed.Contains(flow.Id) && m_ActiveFlow == null)
            {
                StartInternal(flow);
            }
        }

        /// <summary>
        /// 开始一个已注册的流程，进入第 0 步。
        /// 当流程不存在、已完成，或当前已有流程在运行时返回 false。
        /// </summary>
        /// <param name="flowId">流程 Id。</param>
        /// <returns>成功开始返回 true。</returns>
        public bool Start(string flowId)
        {
            if (string.IsNullOrEmpty(flowId))
            {
                return false;
            }

            if (!m_Flows.TryGetValue(flowId, out GuideFlow flow))
            {
                return false;
            }

            if (m_Completed.Contains(flowId))
            {
                return false;
            }

            // 已有流程在运行（包含尝试重复开始同一流程）时拒绝。
            if (m_ActiveFlow != null)
            {
                return false;
            }

            StartInternal(flow);
            return true;
        }

        /// <summary>
        /// 查询某流程的状态：已完成 => Completed；正在运行 => Running；其余（含未注册）=> NotStarted。
        /// </summary>
        /// <param name="flowId">流程 Id。</param>
        /// <returns>流程状态。</returns>
        public GuideFlowStatus GetStatus(string flowId)
        {
            if (string.IsNullOrEmpty(flowId))
            {
                return GuideFlowStatus.NotStarted;
            }

            if (m_Completed.Contains(flowId))
            {
                return GuideFlowStatus.Completed;
            }

            if (m_ActiveFlow != null && string.Equals(m_ActiveFlow.Id, flowId, StringComparison.Ordinal))
            {
                return GuideFlowStatus.Running;
            }

            return GuideFlowStatus.NotStarted;
        }

        /// <summary>
        /// 上报一个游戏侧触发事件。若存在活动流程且当前步骤匹配（事件类型 + 触发键/通配），
        /// 则完成当前步骤并推进。
        /// </summary>
        /// <param name="eventType">触发事件类型，如 "click"、"openPanel"。</param>
        /// <param name="triggerKey">触发键；可为空。</param>
        /// <returns>若因此推进了一步返回 true；否则返回 false。</returns>
        public bool ReportTrigger(string eventType, string triggerKey = null)
        {
            if (m_ActiveFlow == null || string.IsNullOrEmpty(eventType))
            {
                return false;
            }

            GuideStep current = CurrentStep;
            if (current == null || !current.MatchesTrigger(eventType, triggerKey))
            {
                return false;
            }

            Advance();
            return true;
        }

        /// <summary>
        /// 强制推进当前步骤（如“下一步”按钮）。对仅手动推进的步骤而言这是唯一的前进方式。
        /// </summary>
        /// <returns>若存在活动流程并推进了一步返回 true；否则返回 false。</returns>
        public bool AdvanceManually()
        {
            if (m_ActiveFlow == null)
            {
                return false;
            }

            Advance();
            return true;
        }

        /// <summary>
        /// 跳过整个活动流程，将其标记为已完成。
        /// </summary>
        /// <returns>若确有活动流程被跳过返回 true；否则返回 false。</returns>
        public bool SkipActiveFlow()
        {
            if (m_ActiveFlow == null)
            {
                return false;
            }

            CompleteActiveFlow();
            return true;
        }

        /// <summary>
        /// 判断某流程是否已完成。
        /// </summary>
        /// <param name="flowId">流程 Id。</param>
        /// <returns>已完成返回 true。</returns>
        public bool IsFlowCompleted(string flowId)
        {
            return !string.IsNullOrEmpty(flowId) && m_Completed.Contains(flowId);
        }

        /// <summary>
        /// 导出当前进度快照（纯数据），交由游戏侧存档系统持久化。
        /// 包含已完成流程集合，以及活动流程的 Id 与步骤索引（用于断点续传）。
        /// </summary>
        /// <returns>进度快照。</returns>
        public GuideProgress ExportProgress()
        {
            GuideProgress progress = new GuideProgress
            {
                CompletedFlowIds = new List<string>(m_Completed),
                ActiveFlowId = m_ActiveFlow != null ? m_ActiveFlow.Id : null,
                ActiveStepIndex = m_ActiveFlow != null ? m_ActiveStepIndex : -1,
            };

            return progress;
        }

        /// <summary>
        /// 从快照恢复进度：标记其中的已完成流程，使后续 AutoStart/Start 正确跳过它们。
        /// 应在 <see cref="Register"/> 之前调用。
        /// 若快照中的活动流程已注册、未完成且当前无流程运行，则恢复其运行态（断点续传）。
        /// </summary>
        /// <param name="progress">进度快照；为 null 时不做任何处理。</param>
        public void ImportProgress(GuideProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            if (progress.CompletedFlowIds != null)
            {
                for (int i = 0; i < progress.CompletedFlowIds.Count; i++)
                {
                    string id = progress.CompletedFlowIds[i];
                    if (!string.IsNullOrEmpty(id))
                    {
                        m_Completed.Add(id);
                    }
                }
            }

            // 断点续传：仅在活动流程已注册、未完成、索引合法且当前空闲时恢复。
            string activeId = progress.ActiveFlowId;
            if (!string.IsNullOrEmpty(activeId)
                && !m_Completed.Contains(activeId)
                && m_ActiveFlow == null
                && m_Flows.TryGetValue(activeId, out GuideFlow flow)
                && progress.ActiveStepIndex >= 0
                && progress.ActiveStepIndex < flow.Steps.Count)
            {
                m_ActiveFlow = flow;
                m_ActiveStepIndex = progress.ActiveStepIndex;
            }
        }

        /// <summary>
        /// 开始一个流程：置为活动流程，进入第 0 步，触发 OnFlowStarted 与 OnStepEntered。
        /// </summary>
        private void StartInternal(GuideFlow flow)
        {
            m_ActiveFlow = flow;
            m_ActiveStepIndex = 0;

            OnFlowStarted?.Invoke(this, flow);
            OnStepEntered?.Invoke(this, flow, flow.Steps[0]);
        }

        /// <summary>
        /// 推进当前步骤：触发 OnStepCompleted；若仍有后续步骤则进入下一步（触发 OnStepEntered），
        /// 否则完成整个流程。
        /// </summary>
        private void Advance()
        {
            GuideFlow flow = m_ActiveFlow;
            GuideStep completedStep = flow.Steps[m_ActiveStepIndex];

            OnStepCompleted?.Invoke(this, flow, completedStep);

            int nextIndex = m_ActiveStepIndex + 1;
            if (nextIndex < flow.Steps.Count)
            {
                m_ActiveStepIndex = nextIndex;
                OnStepEntered?.Invoke(this, flow, flow.Steps[nextIndex]);
                return;
            }

            // 走完最后一步 => 完成整个流程。
            CompleteActiveFlow();
        }

        /// <summary>
        /// 将当前活动流程标记为完成：清空活动态并触发 OnFlowCompleted。
        /// </summary>
        private void CompleteActiveFlow()
        {
            GuideFlow flow = m_ActiveFlow;

            // 先清状态再回调，确保事件处理器内读取到的 ActiveFlow 已为 null。
            m_ActiveFlow = null;
            m_ActiveStepIndex = -1;
            m_Completed.Add(flow.Id);

            OnFlowCompleted?.Invoke(this, flow);
        }

        /// <summary>
        /// 进入某步骤时触发。参数：管理器、流程、进入的步骤。
        /// </summary>
        public event Action<GuideManager, GuideFlow, GuideStep> OnStepEntered;

        /// <summary>
        /// 完成某步骤时触发（先于进入下一步或完成流程）。参数：管理器、流程、完成的步骤。
        /// </summary>
        public event Action<GuideManager, GuideFlow, GuideStep> OnStepCompleted;

        /// <summary>
        /// 流程开始时触发。参数：管理器、流程。
        /// </summary>
        public event Action<GuideManager, GuideFlow> OnFlowStarted;

        /// <summary>
        /// 流程完成（含正常走完或被跳过）时触发。参数：管理器、流程。
        /// </summary>
        public event Action<GuideManager, GuideFlow> OnFlowCompleted;
    }
}
