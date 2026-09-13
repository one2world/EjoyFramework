//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Guide
{
    /// <summary>
    /// 引导进度的可序列化快照。纯数据、无引擎依赖、无存档模块依赖，
    /// 由游戏侧的存档系统负责持久化。通过 <see cref="GuideManager.ExportProgress"/> 导出、
    /// <see cref="GuideManager.ImportProgress"/> 恢复。
    /// </summary>
    public sealed class GuideProgress
    {
        /// <summary>
        /// 构造一个空进度快照。
        /// </summary>
        public GuideProgress()
        {
            CompletedFlowIds = new List<string>();
            ActiveStepIndex = -1;
        }

        /// <summary>
        /// 已完成的流程 Id 列表。
        /// </summary>
        public List<string> CompletedFlowIds { get; set; }

        /// <summary>
        /// 当前正在进行的流程 Id（用于断点续传）；无活动流程时为 null。
        /// </summary>
        public string ActiveFlowId { get; set; }

        /// <summary>
        /// 当前活动流程的步骤索引（用于断点续传）；无活动流程时为 -1。
        /// </summary>
        public int ActiveStepIndex { get; set; }
    }
}
