//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Guide;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Guide
{
    /// <summary>
    /// 针对 <see cref="GuideManager"/> 新手引导流程注册、触发推进、手动推进、跳过、完成判定与进度持久化的单元测试。
    /// </summary>
    [TestFixture]
    public class GuideManagerTests
    {
        // 一个三步流程：
        //  step0：点击 startButton 推进
        //  step1：打开任意面板推进（triggerKey 为空 => 通配）
        //  step2：仅手动推进（无 triggerEventType）
        private static GuideFlow ThreeStepFlow(string id = "tutorial", bool autoStart = true)
        {
            GuideFlow.Builder builder = GuideFlow.Create(id);
            if (autoStart)
            {
                builder.AutoStart();
            }

            return builder
                .AddStep("step0", "startButton", "click", "startButton")
                .AddStep("step1", "panelAnchor", "openPanel")
                .AddStep(new GuideStep("step2", "finishAnchor"))
                .Build();
        }

        [Test]
        public void Register_AutoStart_RunsAtStepZeroAndFiresStartedAndEntered()
        {
            GuideManager manager = new GuideManager();

            int flowStarted = 0;
            int stepEntered = 0;
            GuideStep enteredStep = null;
            manager.OnFlowStarted += (m, f) => flowStarted++;
            manager.OnStepEntered += (m, f, s) =>
            {
                stepEntered++;
                enteredStep = s;
            };

            manager.Register(ThreeStepFlow());

            Assert.AreEqual(GuideFlowStatus.Running, manager.GetStatus("tutorial"));
            Assert.IsNotNull(manager.ActiveFlow);
            Assert.AreEqual("tutorial", manager.ActiveFlow.Id);
            Assert.AreEqual("step0", manager.CurrentStep.Id);
            Assert.AreEqual(1, flowStarted);
            Assert.AreEqual(1, stepEntered);
            Assert.AreEqual("step0", enteredStep.Id);
        }

        [Test]
        public void ReportTrigger_Matching_Advances_NonMatching_NoOp()
        {
            GuideManager manager = new GuideManager();
            manager.Register(ThreeStepFlow());

            // 非匹配触发：错误事件类型 => 无推进。
            Assert.IsFalse(manager.ReportTrigger("click", "wrongKey"));
            Assert.AreEqual("step0", manager.CurrentStep.Id);

            // 匹配触发 => 推进到 step1。
            Assert.IsTrue(manager.ReportTrigger("click", "startButton"));
            Assert.AreEqual("step1", manager.CurrentStep.Id);
        }

        [Test]
        public void ReportTrigger_NullTriggerKey_MatchesAnyKey_ExactKeyMustMatch()
        {
            GuideManager manager = new GuideManager();
            manager.Register(ThreeStepFlow());

            // step0 的 triggerKey 为精确值 "startButton"：任意键不可通配。
            Assert.IsFalse(manager.ReportTrigger("click", "other"));
            Assert.IsTrue(manager.ReportTrigger("click", "startButton"));

            // step1 的 triggerKey 为空 => 通配：任意 openPanel 键均可推进。
            Assert.AreEqual("step1", manager.CurrentStep.Id);
            Assert.IsTrue(manager.ReportTrigger("openPanel", "anyPanelKeyWorks"));
            Assert.AreEqual("step2", manager.CurrentStep.Id);
        }

        [Test]
        public void ManualOnlyStep_AdvancesViaManual_NotViaReportTrigger()
        {
            GuideManager manager = new GuideManager();
            manager.Register(ThreeStepFlow());

            // 前进到 step2（仅手动）。
            Assert.IsTrue(manager.ReportTrigger("click", "startButton"));
            Assert.IsTrue(manager.ReportTrigger("openPanel"));
            Assert.AreEqual("step2", manager.CurrentStep.Id);

            // 仅手动步骤不响应任何触发事件。
            Assert.IsFalse(manager.ReportTrigger("click", "anything"));
            Assert.IsFalse(manager.ReportTrigger("openPanel"));
            Assert.AreEqual("step2", manager.CurrentStep.Id);

            // 手动推进 => 完成最后一步 => 流程完成。
            Assert.IsTrue(manager.AdvanceManually());
            Assert.AreEqual(GuideFlowStatus.Completed, manager.GetStatus("tutorial"));
        }

        [Test]
        public void CompletingLastStep_FlowCompleted_FiresEvent_ActiveNull_IsCompletedTrue()
        {
            GuideManager manager = new GuideManager();

            int flowCompleted = 0;
            GuideFlow completedFlow = null;
            manager.OnFlowCompleted += (m, f) =>
            {
                flowCompleted++;
                completedFlow = f;
            };

            manager.Register(ThreeStepFlow());

            Assert.IsTrue(manager.ReportTrigger("click", "startButton"));
            Assert.IsTrue(manager.ReportTrigger("openPanel"));
            Assert.IsTrue(manager.AdvanceManually());

            Assert.AreEqual(1, flowCompleted);
            Assert.AreEqual("tutorial", completedFlow.Id);
            Assert.IsNull(manager.ActiveFlow);
            Assert.IsNull(manager.CurrentStep);
            Assert.AreEqual(GuideFlowStatus.Completed, manager.GetStatus("tutorial"));
            Assert.IsTrue(manager.IsFlowCompleted("tutorial"));
        }

        [Test]
        public void SkipActiveFlow_MarksCompleted()
        {
            GuideManager manager = new GuideManager();
            manager.Register(ThreeStepFlow());

            int flowCompleted = 0;
            manager.OnFlowCompleted += (m, f) => flowCompleted++;

            Assert.IsTrue(manager.SkipActiveFlow());
            Assert.AreEqual(1, flowCompleted);
            Assert.IsNull(manager.ActiveFlow);
            Assert.AreEqual(GuideFlowStatus.Completed, manager.GetStatus("tutorial"));
            Assert.IsTrue(manager.IsFlowCompleted("tutorial"));

            // 无活动流程时再跳过返回 false。
            Assert.IsFalse(manager.SkipActiveFlow());
        }

        [Test]
        public void Start_OnAlreadyCompletedFlow_ReturnsFalse()
        {
            GuideManager manager = new GuideManager();
            // 非 AutoStart，手动控制。
            manager.Register(ThreeStepFlow(autoStart: false));

            Assert.IsTrue(manager.Start("tutorial"));
            Assert.IsTrue(manager.SkipActiveFlow());
            Assert.AreEqual(GuideFlowStatus.Completed, manager.GetStatus("tutorial"));

            // 已完成 => 不能再开始。
            Assert.IsFalse(manager.Start("tutorial"));
        }

        [Test]
        public void Start_WhileAnotherRunning_ReturnsFalse()
        {
            GuideManager manager = new GuideManager();
            manager.Register(ThreeStepFlow("flowA", autoStart: false));
            manager.Register(ThreeStepFlow("flowB", autoStart: false));

            Assert.IsTrue(manager.Start("flowA"));
            // 已有 flowA 在运行 => flowB 被拒绝。
            Assert.IsFalse(manager.Start("flowB"));
            Assert.AreEqual("flowA", manager.ActiveFlow.Id);
        }

        [Test]
        public void Start_MissingFlow_ReturnsFalse()
        {
            GuideManager manager = new GuideManager();
            Assert.IsFalse(manager.Start("nope"));
        }

        [Test]
        public void Register_DuplicateId_Throws()
        {
            GuideManager manager = new GuideManager();
            manager.Register(ThreeStepFlow("dup", autoStart: false));

            Assert.Throws<System.ArgumentException>(
                () => manager.Register(ThreeStepFlow("dup", autoStart: false)));
        }

        [Test]
        public void ImportProgress_BeforeRegister_AutoStartFlow_DoesNotAutoStart_StatusCompleted()
        {
            GuideManager manager = new GuideManager();

            GuideProgress progress = new GuideProgress
            {
                CompletedFlowIds = new List<string> { "tutorial" },
            };

            // 先 Import，再 Register（按 API 约定顺序）。
            manager.ImportProgress(progress);

            int flowStarted = 0;
            manager.OnFlowStarted += (m, f) => flowStarted++;
            manager.Register(ThreeStepFlow()); // AutoStart

            Assert.AreEqual(0, flowStarted);
            Assert.IsNull(manager.ActiveFlow);
            Assert.AreEqual(GuideFlowStatus.Completed, manager.GetStatus("tutorial"));
            Assert.IsTrue(manager.IsFlowCompleted("tutorial"));

            // 已完成 => Start 也被拒绝。
            Assert.IsFalse(manager.Start("tutorial"));
        }

        [Test]
        public void ExportProgress_RoundTrips_CompletedSetPreserved()
        {
            // 在 manager1 中完成 flowA 并跳过 flowB。
            GuideManager manager1 = new GuideManager();
            manager1.Register(ThreeStepFlow("flowA", autoStart: false));
            manager1.Register(ThreeStepFlow("flowB", autoStart: false));

            Assert.IsTrue(manager1.Start("flowA"));
            Assert.IsTrue(manager1.ReportTrigger("click", "startButton"));
            Assert.IsTrue(manager1.ReportTrigger("openPanel"));
            Assert.IsTrue(manager1.AdvanceManually()); // flowA 完成

            Assert.IsTrue(manager1.Start("flowB"));
            Assert.IsTrue(manager1.SkipActiveFlow()); // flowB 跳过完成

            GuideProgress exported = manager1.ExportProgress();
            Assert.Contains("flowA", exported.CompletedFlowIds);
            Assert.Contains("flowB", exported.CompletedFlowIds);
            Assert.IsNull(exported.ActiveFlowId);

            // 导入到全新管理器，完成集合应被保留。
            GuideManager manager2 = new GuideManager();
            manager2.ImportProgress(exported);
            manager2.Register(ThreeStepFlow("flowA"));     // AutoStart 但已完成
            manager2.Register(ThreeStepFlow("flowB", autoStart: false));

            Assert.IsTrue(manager2.IsFlowCompleted("flowA"));
            Assert.IsTrue(manager2.IsFlowCompleted("flowB"));
            Assert.AreEqual(GuideFlowStatus.Completed, manager2.GetStatus("flowA"));
            Assert.AreEqual(GuideFlowStatus.Completed, manager2.GetStatus("flowB"));
            Assert.IsNull(manager2.ActiveFlow); // flowA 已完成，AutoStart 不触发
        }

        [Test]
        public void ExportImport_ResumesActiveFlowAtSameStep()
        {
            GuideManager manager1 = new GuideManager();
            manager1.Register(ThreeStepFlow("tutorial", autoStart: false));
            Assert.IsTrue(manager1.Start("tutorial"));
            Assert.IsTrue(manager1.ReportTrigger("click", "startButton")); // 现处于 step1

            GuideProgress exported = manager1.ExportProgress();
            Assert.AreEqual("tutorial", exported.ActiveFlowId);
            Assert.AreEqual(1, exported.ActiveStepIndex);

            // 恢复到新管理器：先 Import，再 Register（续传发生在 Import 时）。
            GuideManager manager2 = new GuideManager();
            manager2.Register(ThreeStepFlow("tutorial", autoStart: false));
            manager2.ImportProgress(exported);

            Assert.AreEqual(GuideFlowStatus.Running, manager2.GetStatus("tutorial"));
            Assert.IsNotNull(manager2.ActiveFlow);
            Assert.AreEqual("step1", manager2.CurrentStep.Id);
        }

        [Test]
        public void StepCompletedAndEntered_FireInOrderAndCounts()
        {
            GuideManager manager = new GuideManager();

            List<string> log = new List<string>();
            manager.OnStepEntered += (m, f, s) => log.Add("enter:" + s.Id);
            manager.OnStepCompleted += (m, f, s) => log.Add("complete:" + s.Id);
            manager.OnFlowStarted += (m, f) => log.Add("flowStart:" + f.Id);
            manager.OnFlowCompleted += (m, f) => log.Add("flowComplete:" + f.Id);

            manager.Register(ThreeStepFlow());                  // flowStart, enter step0
            manager.ReportTrigger("click", "startButton");      // complete step0, enter step1
            manager.ReportTrigger("openPanel");                 // complete step1, enter step2
            manager.AdvanceManually();                          // complete step2, flowComplete

            CollectionAssert.AreEqual(
                new List<string>
                {
                    "flowStart:tutorial",
                    "enter:step0",
                    "complete:step0",
                    "enter:step1",
                    "complete:step1",
                    "enter:step2",
                    "complete:step2",
                    "flowComplete:tutorial",
                },
                log);
        }

        [Test]
        public void ReportTrigger_WithNoActiveFlow_ReturnsFalse()
        {
            GuideManager manager = new GuideManager();
            manager.Register(ThreeStepFlow(autoStart: false));

            // 未开始 => 无活动流程。
            Assert.IsFalse(manager.ReportTrigger("click", "startButton"));
            Assert.IsFalse(manager.AdvanceManually());
        }
    }
}
