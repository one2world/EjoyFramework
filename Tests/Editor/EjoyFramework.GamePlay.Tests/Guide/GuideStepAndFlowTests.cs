//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Guide;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Guide
{
    /// <summary>
    /// 针对 <see cref="GuideStep"/> 触发匹配与 <see cref="GuideFlow.Builder"/> 构建约束的单元测试。
    /// </summary>
    [TestFixture]
    public class GuideStepAndFlowTests
    {
        [Test]
        public void GuideStep_EmptyId_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new GuideStep(null));
            Assert.Throws<System.ArgumentException>(() => new GuideStep(string.Empty));
        }

        [Test]
        public void MatchesTrigger_ManualOnlyStep_NeverMatches()
        {
            // 无 triggerEventType => 仅手动推进，不响应任何触发。
            GuideStep step = new GuideStep("s", "anchor");

            Assert.IsFalse(step.MatchesTrigger("click", "anything"));
            Assert.IsFalse(step.MatchesTrigger(null, null));
        }

        [Test]
        public void MatchesTrigger_NullTriggerKey_MatchesAnyKeyOfThatEventType()
        {
            GuideStep step = new GuideStep("s", "anchor", "openPanel");

            Assert.IsTrue(step.MatchesTrigger("openPanel", "panelA"));
            Assert.IsTrue(step.MatchesTrigger("openPanel", null));
            // 事件类型不同 => 不匹配。
            Assert.IsFalse(step.MatchesTrigger("click", "panelA"));
        }

        [Test]
        public void MatchesTrigger_ExactTriggerKey_MustMatch()
        {
            GuideStep step = new GuideStep("s", "anchor", "click", "startButton");

            Assert.IsTrue(step.MatchesTrigger("click", "startButton"));
            Assert.IsFalse(step.MatchesTrigger("click", "otherButton"));
            Assert.IsFalse(step.MatchesTrigger("click", null));
        }

        [Test]
        public void GuideStep_EmptyTriggerEventType_NormalizedToManualOnly()
        {
            // 空串的 triggerEventType 等价于 null => 仅手动。
            GuideStep step = new GuideStep("s", "anchor", string.Empty, "key");

            Assert.IsNull(step.TriggerEventType);
            Assert.IsFalse(step.MatchesTrigger("click", "key"));
        }

        [Test]
        public void GuideStep_PayloadAndTargetKey_RoundTrip()
        {
            object payload = new { tip = "hello" };
            GuideStep step = new GuideStep("s", "anchorX", "click", "k", payload);

            Assert.AreEqual("anchorX", step.TargetKey);
            Assert.AreSame(payload, step.Payload);
        }

        [Test]
        public void GuideStep_EmptyTargetKey_NormalizedToNull()
        {
            GuideStep step = new GuideStep("s", string.Empty);
            Assert.IsNull(step.TargetKey);
        }

        [Test]
        public void GuideFlow_Builder_NoSteps_Throws()
        {
            Assert.Throws<System.InvalidOperationException>(() => GuideFlow.Create("f").Build());
        }

        [Test]
        public void GuideFlow_Builder_AddNullStep_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => GuideFlow.Create("f").AddStep((GuideStep)null));
        }

        [Test]
        public void GuideFlow_Builder_AutoStartAndSteps_Build()
        {
            GuideFlow flow = GuideFlow.Create("f")
                .AutoStart()
                .AddStep("a", "anchorA", "click", "btnA")
                .AddStep(new GuideStep("b", "anchorB"))
                .Build();

            Assert.AreEqual("f", flow.Id);
            Assert.IsTrue(flow.AutoStart);
            Assert.AreEqual(2, flow.Steps.Count);
            Assert.AreEqual("a", flow.Steps[0].Id);
            Assert.AreEqual("b", flow.Steps[1].Id);
        }

        [Test]
        public void GuideFlow_Ctor_EmptyId_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => new GuideFlow(null, new[] { new GuideStep("s") }));
        }

        [Test]
        public void GuideFlow_Ctor_EmptySteps_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => new GuideFlow("f", new GuideStep[0]));
        }
    }
}
