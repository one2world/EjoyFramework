//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Guide
{
    /// <summary>
    /// 新手引导/教程流程管理器接口。纯逻辑、与引擎无关。
    /// 同一时刻至多一个流程处于运行态（<see cref="ActiveFlow"/>）。
    /// 游戏侧通过 <see cref="ReportTrigger"/> 推送触发事件以自动推进，或通过
    /// <see cref="AdvanceManually"/> 手动推进；视觉层（遮罩、高亮）由游戏侧负责。
    /// 进度以 <see cref="GuideProgress"/> 纯数据快照导出/恢复，不依赖任何存档模块。
    ///
    /// 调用顺序：先 <see cref="ImportProgress"/> 标记已完成的流程，再 <see cref="Register"/>，
    /// 这样 AutoStart 才会正确跳过已完成的流程。
    /// 通过 <c>Framework.GetModule&lt;IGuideManager&gt;()</c> 获取实例。
    /// </summary>
    public interface IGuideManager
    {
        /// <summary>
        /// 当前正在运行的流程；无流程运行时为 null。
        /// </summary>
        GuideFlow ActiveFlow { get; }

        /// <summary>
        /// 当前活动流程的当前步骤；无流程运行时为 null。
        /// </summary>
        GuideStep CurrentStep { get; }

        /// <summary>
        /// 注册一个流程。若其 AutoStart 为真、未被标记为已完成、且当前无其他流程运行，则立即开始。
        /// </summary>
        /// <param name="flow">引导流程。</param>
        /// <exception cref="ArgumentNullException">flow 为 null。</exception>
        /// <exception cref="ArgumentException">流程 Id 已注册。</exception>
        void Register(GuideFlow flow);

        /// <summary>
        /// 开始一个已注册的流程，进入第 0 步。
        /// 当流程不存在、已完成，或当前已有流程在运行时返回 false。
        /// </summary>
        /// <param name="flowId">流程 Id。</param>
        /// <returns>成功开始返回 true。</returns>
        bool Start(string flowId);

        /// <summary>
        /// 查询某流程的状态：已完成 => Completed；正在运行 => Running；其余（含未注册）=> NotStarted。
        /// </summary>
        /// <param name="flowId">流程 Id。</param>
        /// <returns>流程状态。</returns>
        GuideFlowStatus GetStatus(string flowId);

        /// <summary>
        /// 上报一个游戏侧触发事件。若存在活动流程且当前步骤匹配（事件类型 + 触发键/通配），
        /// 则完成当前步骤并推进。
        /// </summary>
        /// <param name="eventType">触发事件类型，如 "click"、"openPanel"。</param>
        /// <param name="triggerKey">触发键；可为空。</param>
        /// <returns>若因此推进了一步返回 true；否则返回 false。</returns>
        bool ReportTrigger(string eventType, string triggerKey = null);

        /// <summary>
        /// 强制推进当前步骤（如“下一步”按钮）。对仅手动推进的步骤而言这是唯一的前进方式。
        /// </summary>
        /// <returns>若存在活动流程并推进了一步返回 true；否则返回 false。</returns>
        bool AdvanceManually();

        /// <summary>
        /// 跳过整个活动流程，将其标记为已完成。
        /// </summary>
        /// <returns>若确有活动流程被跳过返回 true；否则返回 false。</returns>
        bool SkipActiveFlow();

        /// <summary>
        /// 判断某流程是否已完成。
        /// </summary>
        /// <param name="flowId">流程 Id。</param>
        /// <returns>已完成返回 true。</returns>
        bool IsFlowCompleted(string flowId);

        /// <summary>
        /// 导出当前进度快照（纯数据），交由游戏侧存档系统持久化。
        /// 包含已完成流程集合，以及活动流程的 Id 与步骤索引（用于断点续传）。
        /// </summary>
        /// <returns>进度快照。</returns>
        GuideProgress ExportProgress();

        /// <summary>
        /// 从快照恢复进度：标记其中的已完成流程，使后续 AutoStart/Start 正确跳过它们。
        /// 应在 <see cref="Register"/> 之前调用。
        /// 若快照中的活动流程已注册、未完成且当前无流程运行，则恢复其运行态（断点续传）。
        /// </summary>
        /// <param name="progress">进度快照；为 null 时不做任何处理。</param>
        void ImportProgress(GuideProgress progress);

        /// <summary>
        /// 进入某步骤时触发。参数：管理器、流程、进入的步骤。
        /// </summary>
        event Action<GuideManager, GuideFlow, GuideStep> OnStepEntered;

        /// <summary>
        /// 完成某步骤时触发（先于进入下一步或完成流程）。参数：管理器、流程、完成的步骤。
        /// </summary>
        event Action<GuideManager, GuideFlow, GuideStep> OnStepCompleted;

        /// <summary>
        /// 流程开始时触发。参数：管理器、流程。
        /// </summary>
        event Action<GuideManager, GuideFlow> OnFlowStarted;

        /// <summary>
        /// 流程完成（含正常走完或被跳过）时触发。参数：管理器、流程。
        /// </summary>
        event Action<GuideManager, GuideFlow> OnFlowCompleted;
    }
}
