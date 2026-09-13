//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Guide
{
    /// <summary>
    /// 新手引导的单个步骤。纯逻辑、与引擎无关。
    /// 当一个匹配的触发事件被上报（<see cref="MatchesTrigger"/>）或被手动推进时，本步骤完成。
    /// 视觉层（遮罩、高亮、箭头）由游戏侧负责，本类仅描述步骤所需的不透明数据。
    /// 不可变值对象，注册后不应再修改。
    /// </summary>
    public sealed class GuideStep
    {
        private readonly string m_Id;
        private readonly string m_TargetKey;
        private readonly string m_TriggerEventType;
        private readonly string m_TriggerKey;
        private readonly object m_Payload;

        /// <summary>
        /// 构造一个引导步骤。
        /// </summary>
        /// <param name="id">步骤唯一标识（流程内唯一），不可为空。</param>
        /// <param name="targetKey">游戏侧用于定位高亮/遮罩的不透明 UI 锚点 Id；可为空。</param>
        /// <param name="triggerEventType">完成本步骤的触发事件类型，如 "click"、"openPanel"；为空表示仅能手动推进。</param>
        /// <param name="triggerKey">触发键；为空表示匹配该事件类型下的任意键。</param>
        /// <param name="payload">不透明的游戏侧数据（提示文本 Id、箭头方向等），原样回传。</param>
        /// <exception cref="ArgumentException">当 id 为空时抛出。</exception>
        public GuideStep(
            string id,
            string targetKey = null,
            string triggerEventType = null,
            string triggerKey = null,
            object payload = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("引导步骤的 id 不能为空。", nameof(id));
            }

            m_Id = id;
            // 归一化：空串统一记为 null，便于内部判空。
            m_TargetKey = string.IsNullOrEmpty(targetKey) ? null : targetKey;
            m_TriggerEventType = string.IsNullOrEmpty(triggerEventType) ? null : triggerEventType;
            m_TriggerKey = string.IsNullOrEmpty(triggerKey) ? null : triggerKey;
            m_Payload = payload;
        }

        /// <summary>
        /// 步骤唯一标识（流程内唯一）。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 游戏侧用于定位高亮/遮罩的不透明 UI 锚点 Id；可为 null。
        /// </summary>
        public string TargetKey
        {
            get { return m_TargetKey; }
        }

        /// <summary>
        /// 完成本步骤的触发事件类型；为 null 表示仅能手动推进（无自动触发）。
        /// </summary>
        public string TriggerEventType
        {
            get { return m_TriggerEventType; }
        }

        /// <summary>
        /// 触发键；为 null 表示匹配该事件类型下的任意键。
        /// </summary>
        public string TriggerKey
        {
            get { return m_TriggerKey; }
        }

        /// <summary>
        /// 不透明的游戏侧数据（提示文本 Id、箭头方向等），原样回传。
        /// </summary>
        public object Payload
        {
            get { return m_Payload; }
        }

        /// <summary>
        /// 判断本步骤是否会被给定触发事件完成。
        /// 当本步骤无 <see cref="TriggerEventType"/>（仅手动）时永不匹配；
        /// 否则 EventType 必须相等；当本步骤 TriggerKey 为空时匹配任意键，否则要求精确相等。
        /// </summary>
        /// <param name="eventType">触发事件类型。</param>
        /// <param name="triggerKey">触发事件键。</param>
        /// <returns>匹配返回 true。</returns>
        public bool MatchesTrigger(string eventType, string triggerKey)
        {
            // 仅手动推进的步骤不响应任何触发事件。
            if (m_TriggerEventType == null)
            {
                return false;
            }

            if (!string.Equals(m_TriggerEventType, eventType, StringComparison.Ordinal))
            {
                return false;
            }

            // TriggerKey 为空 => 通配，匹配任意键。
            if (m_TriggerKey == null)
            {
                return true;
            }

            return string.Equals(m_TriggerKey, triggerKey, StringComparison.Ordinal);
        }
    }
}
