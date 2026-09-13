//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Prediction;

namespace EjoyFramework.GamePlay.Tests.Netcode.Prediction
{
    /// <summary>
    /// <see cref="ClientPrediction{TState,TInput}"/> 的单元测试。
    /// 使用一维确定性 mover：状态=位置(float)，输入=速度(float)，step=(s,v,dt)=>s+v*dt。
    /// </summary>
    public class ClientPredictionTests
    {
        private const float Delta = 1e-4f;

        // 确定性一维 mover：位置 += 速度 * dt。
        private static readonly Func<float, float, float, float> Mover = (s, v, dt) => s + v * dt;

        private static ClientPrediction<float, float> NewMover(float initial = 0f)
        {
            return new ClientPrediction<float, float>(Mover, initial);
        }

        [Test]
        public void Constructor_NullStep_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ClientPrediction<float, float>(null, 0f));
        }

        [Test]
        public void Constructor_InitialState_NoPendingNoSequence()
        {
            var p = NewMover(5f);
            Assert.AreEqual(5f, p.PredictedState, Delta);
            Assert.AreEqual(0u, p.LastInputSequence);
            Assert.AreEqual(0, p.PendingCount);
        }

        [Test]
        public void AddInput_AdvancesStateAndIncrementsSequence()
        {
            var p = NewMover(0f);
            var cmd = p.AddInput(2f, 0.5f); // +1.0

            Assert.AreEqual(1f, p.PredictedState, Delta);
            Assert.AreEqual(1u, p.LastInputSequence);
            Assert.AreEqual(1, p.PendingCount);

            // 返回的指令携带分配的序号、dt、输入。
            Assert.AreEqual(1u, cmd.Sequence);
            Assert.AreEqual(0.5f, cmd.DeltaTime, Delta);
            Assert.AreEqual(2f, cmd.Input, Delta);
        }

        [Test]
        public void AddInput_Multiple_AccumulatesAndCountsPending()
        {
            var p = NewMover(0f);
            p.AddInput(1f, 1f); // +1 => 1
            p.AddInput(2f, 1f); // +2 => 3
            p.AddInput(3f, 1f); // +3 => 6

            Assert.AreEqual(6f, p.PredictedState, Delta);
            Assert.AreEqual(3u, p.LastInputSequence);
            Assert.AreEqual(3, p.PendingCount);
        }

        [Test]
        public void Reconcile_DiscardsAckedInputs_PendingDrops()
        {
            var p = NewMover(0f);
            p.AddInput(1f, 1f); // seq1
            p.AddInput(1f, 1f); // seq2
            p.AddInput(1f, 1f); // seq3
            p.AddInput(1f, 1f); // seq4
            p.AddInput(1f, 1f); // seq5
            Assert.AreEqual(5, p.PendingCount);

            // 服务器确认到 seq3，权威状态恰好等于「应用前三条」的结果(=3)。
            p.Reconcile(3f, 3u);

            // 仅剩 seq4、seq5 未确认。
            Assert.AreEqual(2, p.PendingCount);
            // 权威 3 + 重放 seq4(+1) + seq5(+1) = 5。
            Assert.AreEqual(5f, p.PredictedState, Delta);
        }

        [Test]
        public void Reconcile_RewindReplay_AuthoritativeCorrectionPlusUnacked()
        {
            // 客户端添加输入 1..5（每条 +1，dt=1），预测位置应为 5。
            var p = NewMover(0f);
            for (int i = 0; i < 5; i++)
            {
                p.AddInput(1f, 1f);
            }
            Assert.AreEqual(5f, p.PredictedState, Delta);
            Assert.AreEqual(5u, p.LastInputSequence);

            // 服务器 ack seq3，但权威状态比客户端预测高 +2（服务器纠正：
            // 在前三条输入之后，权威位置不是 3 而是 5）。
            // 重放剩余 seq4(+1)、seq5(+1) 应得到 5 + 1 + 1 = 7。
            p.Reconcile(5f, 3u);

            Assert.AreEqual(2, p.PendingCount);
            Assert.AreEqual(7f, p.PredictedState, Delta);
        }

        [Test]
        public void Reconcile_AllAcked_PredictedEqualsAuthoritative()
        {
            var p = NewMover(0f);
            p.AddInput(1f, 1f);
            p.AddInput(1f, 1f);
            p.AddInput(1f, 1f);

            // ack 覆盖全部已发出序号，且权威与预测不同。
            p.Reconcile(42f, 3u);

            Assert.AreEqual(0, p.PendingCount);
            Assert.AreEqual(42f, p.PredictedState, Delta);
        }

        [Test]
        public void Reconcile_AckBeyondIssued_AllDiscarded()
        {
            var p = NewMover(0f);
            p.AddInput(1f, 1f);
            p.AddInput(1f, 1f);

            // 服务器确认序号大于客户端已发出的（极端但合法），应丢弃全部并落到权威。
            p.Reconcile(10f, 99u);

            Assert.AreEqual(0, p.PendingCount);
            Assert.AreEqual(10f, p.PredictedState, Delta);
        }

        [Test]
        public void Reconcile_NoInputsAcked_ReplaysAllOnAuthoritative()
        {
            var p = NewMover(0f);
            p.AddInput(2f, 1f); // seq1 +2
            p.AddInput(3f, 1f); // seq2 +3

            // ack=0：没有任何输入被确认，全部在权威基线上重放。
            p.Reconcile(100f, 0u);

            Assert.AreEqual(2, p.PendingCount);
            Assert.AreEqual(105f, p.PredictedState, Delta); // 100 + 2 + 3
        }

        [Test]
        public void Reconcile_PreservesSequenceCounter()
        {
            var p = NewMover(0f);
            p.AddInput(1f, 1f);
            p.AddInput(1f, 1f);
            p.AddInput(1f, 1f);

            p.Reconcile(0f, 2u);

            // reconcile 不应回退已分配的序号计数器。
            Assert.AreEqual(3u, p.LastInputSequence);

            // 后续新输入继续递增。
            var cmd = p.AddInput(1f, 1f);
            Assert.AreEqual(4u, cmd.Sequence);
            Assert.AreEqual(4u, p.LastInputSequence);
        }

        [Test]
        public void Reset_ClearsPendingSequenceAndState()
        {
            var p = NewMover(0f);
            p.AddInput(1f, 1f);
            p.AddInput(1f, 1f);

            p.Reset(99f);

            Assert.AreEqual(99f, p.PredictedState, Delta);
            Assert.AreEqual(0u, p.LastInputSequence);
            Assert.AreEqual(0, p.PendingCount);

            // 重置后序号从 1 重新开始。
            var cmd = p.AddInput(1f, 1f);
            Assert.AreEqual(1u, cmd.Sequence);
            Assert.AreEqual(100f, p.PredictedState, Delta);
        }
    }
}
