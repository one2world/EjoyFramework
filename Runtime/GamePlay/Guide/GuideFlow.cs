//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Guide
{
    /// <summary>
    /// 一个新手引导流程，由一组有序的 <see cref="GuideStep"/> 组成。
    /// 不可变值对象，注册后不应再修改。可通过 <see cref="Create"/> 链式构建。
    /// </summary>
    public sealed class GuideFlow
    {
        private readonly string m_Id;
        private readonly bool m_AutoStart;
        private readonly IReadOnlyList<GuideStep> m_Steps;

        /// <summary>
        /// 构造一个引导流程。
        /// </summary>
        /// <param name="id">流程唯一标识，不可为空。</param>
        /// <param name="steps">步骤列表，至少包含一个步骤。</param>
        /// <param name="autoStart">注册时若尚未完成是否自动开始。</param>
        /// <exception cref="ArgumentException">当 id 为空，或步骤列表为空时抛出。</exception>
        public GuideFlow(string id, IReadOnlyList<GuideStep> steps, bool autoStart = false)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("引导流程的 id 不能为空。", nameof(id));
            }

            if (steps == null || steps.Count == 0)
            {
                throw new ArgumentException("引导流程至少需要一个步骤。", nameof(steps));
            }

            m_Id = id;
            m_Steps = steps;
            m_AutoStart = autoStart;
        }

        /// <summary>
        /// 流程唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 注册时若尚未完成（依据已导入的进度）是否自动开始。
        /// </summary>
        public bool AutoStart
        {
            get { return m_AutoStart; }
        }

        /// <summary>
        /// 有序步骤列表。
        /// </summary>
        public IReadOnlyList<GuideStep> Steps
        {
            get { return m_Steps; }
        }

        /// <summary>
        /// 创建一个引导流程构建器。
        /// </summary>
        /// <param name="id">流程唯一标识。</param>
        /// <returns>构建器实例。</returns>
        public static Builder Create(string id)
        {
            return new Builder(id);
        }

        /// <summary>
        /// 引导流程的链式构建器。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private readonly List<GuideStep> m_Steps = new List<GuideStep>();
            private bool m_AutoStart;

            /// <summary>
            /// 构造构建器。
            /// </summary>
            /// <param name="id">流程唯一标识。</param>
            public Builder(string id)
            {
                m_Id = id;
            }

            /// <summary>
            /// 标记本流程在注册时自动开始（前提：未被标记为已完成）。
            /// </summary>
            /// <returns>构建器自身。</returns>
            public Builder AutoStart()
            {
                m_AutoStart = true;
                return this;
            }

            /// <summary>
            /// 添加一个步骤。
            /// </summary>
            /// <param name="step">步骤实例。</param>
            /// <returns>构建器自身。</returns>
            /// <exception cref="ArgumentNullException">step 为 null。</exception>
            public Builder AddStep(GuideStep step)
            {
                if (step == null)
                {
                    throw new ArgumentNullException(nameof(step));
                }

                m_Steps.Add(step);
                return this;
            }

            /// <summary>
            /// 以参数形式添加一个步骤。
            /// </summary>
            /// <param name="id">步骤唯一标识。</param>
            /// <param name="targetKey">UI 锚点 Id；可为空。</param>
            /// <param name="triggerEventType">触发事件类型；为空表示仅手动推进。</param>
            /// <param name="triggerKey">触发键；为空表示匹配任意键。</param>
            /// <param name="payload">不透明的游戏侧数据。</param>
            /// <returns>构建器自身。</returns>
            public Builder AddStep(
                string id,
                string targetKey = null,
                string triggerEventType = null,
                string triggerKey = null,
                object payload = null)
            {
                m_Steps.Add(new GuideStep(id, targetKey, triggerEventType, triggerKey, payload));
                return this;
            }

            /// <summary>
            /// 构建引导流程。
            /// </summary>
            /// <returns>不可变的引导流程实例。</returns>
            /// <exception cref="InvalidOperationException">当未添加任何步骤时抛出。</exception>
            public GuideFlow Build()
            {
                if (m_Steps.Count == 0)
                {
                    throw new InvalidOperationException($"引导流程 {m_Id} 至少需要一个步骤。");
                }

                return new GuideFlow(m_Id, m_Steps.ToArray(), m_AutoStart);
            }
        }
    }
}
