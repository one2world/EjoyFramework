//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Netcode.Prediction
{
    /// <summary>
    /// 引擎无关的「客户端预测 + 服务器和解（reconciliation）」核心。
    /// 客户端在本地立即应用输入推进状态以消除手感延迟；当服务器回传权威状态时，
    /// 丢弃已被确认的输入，把状态重置到权威值，再把仍未确认的本地输入按序「重放」到权威状态之上，
    /// 从而在保持响应性的同时与服务器对齐（即经典的 rewind &amp; replay）。
    /// </summary>
    /// <typeparam name="TState">被预测的状态类型（结构体或类）。</typeparam>
    /// <typeparam name="TInput">每帧输入类型。</typeparam>
    /// <remarks>
    /// <para>
    /// <see cref="m_Step"/> 必须是<b>纯函数且确定性</b>的：给定相同的 (previousState, input, dt)
    /// 必须始终产生相同的 nextState，且不得修改入参或依赖外部可变状态。否则重放结果会与服务器发散。
    /// </para>
    /// <para>序号从 0 开始，第一条 <see cref="AddInput"/> 分配序号 1（<see cref="LastInputSequence"/> 前置自增）。</para>
    /// </remarks>
    public sealed class ClientPrediction<TState, TInput>
    {
        private readonly Func<TState, TInput, float, TState> m_Step;
        private readonly Queue<InputCommand<TInput>> m_Pending;
        private readonly List<InputCommand<TInput>> m_ReplayBuffer;

        private TState m_PredictedState;
        private uint m_LastInputSequence;

        /// <summary>
        /// 构造客户端预测器。
        /// </summary>
        /// <param name="step">纯粹的确定性模拟步进：(previousState, input, dt) =&gt; nextState。</param>
        /// <param name="initialState">初始状态（既作为当前预测状态，也是 <see cref="Reset"/> 之外的起点）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="step"/> 为 null 时抛出。</exception>
        public ClientPrediction(Func<TState, TInput, float, TState> step, TState initialState)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step), "模拟步进函数不能为 null。");
            }

            m_Step = step;
            m_Pending = new Queue<InputCommand<TInput>>();
            m_ReplayBuffer = new List<InputCommand<TInput>>();
            m_PredictedState = initialState;
            m_LastInputSequence = 0u;
        }

        /// <summary>
        /// 最新的预测状态。在 <see cref="AddInput"/> / <see cref="Reconcile"/> / <see cref="Reset"/> 后刷新。
        /// </summary>
        public TState PredictedState
        {
            get { return m_PredictedState; }
        }

        /// <summary>
        /// 最近一次发出的输入序号（即已分配的最大序号）。初始为 0，首条输入后为 1。
        /// </summary>
        public uint LastInputSequence
        {
            get { return m_LastInputSequence; }
        }

        /// <summary>
        /// 当前仍未被服务器确认、缓存待重放的输入数量。
        /// </summary>
        public int PendingCount
        {
            get { return m_Pending.Count; }
        }

        /// <summary>
        /// 应用一条新的本地输入：分配下一个序号、用 step 推进 <see cref="PredictedState"/>、
        /// 把该输入缓存进待确认队列以备重放，并返回生成的指令（可直接发送给服务器）。
        /// </summary>
        /// <param name="input">本帧输入载荷。</param>
        /// <param name="deltaTime">本帧模拟时间步长（秒）。</param>
        /// <returns>本次生成、并已计入预测的输入指令。</returns>
        public InputCommand<TInput> AddInput(TInput input, float deltaTime)
        {
            m_LastInputSequence++;
            var command = new InputCommand<TInput>(m_LastInputSequence, deltaTime, input);

            m_PredictedState = m_Step(m_PredictedState, input, deltaTime);
            m_Pending.Enqueue(command);

            return command;
        }

        /// <summary>
        /// 服务器和解：用权威状态与「服务器已处理到的最后一个输入序号」纠正本地预测。
        /// <list type="number">
        /// <item>丢弃所有 <c>Sequence &lt;= ackedSequence</c> 的已确认输入；</item>
        /// <item>把工作状态重置为权威状态 <paramref name="authoritativeState"/>；</item>
        /// <item>按序把仍未确认的本地输入逐条重放（<c>state = step(state, cmd.Input, cmd.DeltaTime)</c>）；</item>
        /// <item>以重放结果刷新 <see cref="PredictedState"/>。</item>
        /// </list>
        /// 若无未确认输入残留，则 <see cref="PredictedState"/> 等于权威状态。
        /// </summary>
        /// <param name="authoritativeState">服务器回传的权威状态（对应 <paramref name="ackedSequence"/> 这一刻）。</param>
        /// <param name="ackedSequence">服务器已处理到的最后一个输入序号。</param>
        public void Reconcile(TState authoritativeState, uint ackedSequence)
        {
            // 1) 丢弃已被服务器确认的输入（队列按序号单调递增入队，队首即最小序号）。
            while (m_Pending.Count > 0 && m_Pending.Peek().Sequence <= ackedSequence)
            {
                m_Pending.Dequeue();
            }

            // 2) 工作状态重置到权威基线。
            TState state = authoritativeState;

            // 3) 把剩余未确认输入按序重放到权威基线之上。
            //    step 约定为纯函数、不应改动队列；但为兑现「遍历安全」的承诺，
            //    先把待重放输入快照到复用缓冲再遍历——即便某次 step 间接触发了
            //    AddInput/Reset 改动 m_Pending，正在进行的遍历也不会抛
            //    InvalidOperationException（与本库 snapshot-before-iterate 惯例一致）。
            m_ReplayBuffer.Clear();
            foreach (InputCommand<TInput> command in m_Pending)
            {
                m_ReplayBuffer.Add(command);
            }

            for (int i = 0; i < m_ReplayBuffer.Count; i++)
            {
                InputCommand<TInput> command = m_ReplayBuffer[i];
                state = m_Step(state, command.Input, command.DeltaTime);
            }

            m_ReplayBuffer.Clear();

            // 4) 刷新预测状态。
            m_PredictedState = state;
        }

        /// <summary>
        /// 硬重置：清空待确认队列、把序号归零、并把预测状态设为给定状态。
        /// 用于重新出生、切关卡或断线重连等需要丢弃全部本地预测历史的场景。
        /// </summary>
        /// <param name="state">重置后的预测状态。</param>
        public void Reset(TState state)
        {
            m_Pending.Clear();
            m_LastInputSequence = 0u;
            m_PredictedState = state;
        }
    }
}
