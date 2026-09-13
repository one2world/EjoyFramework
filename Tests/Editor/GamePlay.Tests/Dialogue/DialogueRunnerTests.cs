//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Dialogue;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Dialogue
{
    /// <summary>
    /// 针对 <see cref="DialogueRunner"/> 的线性推进、进入效果/事件、选项选择、显示条件过滤、
    /// 分支自动路由与防环终止的端到端单元测试。
    /// </summary>
    [TestFixture]
    public class DialogueRunnerTests
    {
        private static LineNode Line(string id, string text, string next)
        {
            return new LineNode(id, "NPC", text, next);
        }

        [Test]
        public void Linear_AdvanceToEnd_FinishesOnce()
        {
            DialogueGraph graph = new DialogueGraph("g", "a")
                .AddNode(Line("a", "first", "b"))
                .AddNode(Line("b", "second", null));

            DialogueRunner runner = new DialogueRunner();
            int finishedCount = 0;
            runner.OnFinished += r => finishedCount++;

            runner.Start(graph);
            Assert.AreEqual("a", runner.Current.Id);
            Assert.IsFalse(runner.IsFinished);

            runner.Advance();
            Assert.AreEqual("b", runner.Current.Id);
            Assert.IsFalse(runner.IsFinished);

            runner.Advance();
            Assert.IsTrue(runner.IsFinished);
            Assert.IsNull(runner.Current);
            Assert.AreEqual(1, finishedCount);
        }

        [Test]
        public void OnEnterEffects_Mutate_AndNodeEnteredFiresPerNode()
        {
            LineNode a = LineNode.Create("a", "hi")
                .Speaker("NPC")
                .Next("b")
                .OnEnter(DialogueEffect.SetBool("greeted", true))
                .Build();
            LineNode b = LineNode.Create("b", "bye")
                .Next(null)
                .OnEnter(DialogueEffect.AddInt("turns", 1))
                .Build();

            DialogueGraph graph = new DialogueGraph("g", "a").AddNode(a).AddNode(b);

            DialogueRunner runner = new DialogueRunner();
            List<string> entered = new List<string>();
            runner.OnNodeEntered += (r, node) => entered.Add(node.Id);

            runner.Start(graph);
            Assert.IsTrue(runner.Variables.GetBool("greeted"));
            Assert.AreEqual(0, runner.Variables.GetInt("turns"));

            runner.Advance();
            Assert.AreEqual(1, runner.Variables.GetInt("turns"));

            runner.Advance();
            CollectionAssert.AreEqual(new[] { "a", "b" }, entered);
        }

        [Test]
        public void Choice_Choose_AppliesEffectsAndJumps()
        {
            ChoiceNode choice = ChoiceNode.Create("c")
                .Prompt("How do you respond?")
                .AddChoice("Be kind", "kind", null,
                    new[] { DialogueEffect.AddFloat("affection", 1f) })
                .AddChoice("Be cold", "cold", null,
                    new[] { DialogueEffect.AddFloat("affection", -1f) })
                .Build();

            DialogueGraph graph = new DialogueGraph("g", "c")
                .AddNode(choice)
                .AddNode(Line("kind", "She smiles.", null))
                .AddNode(Line("cold", "She frowns.", null));

            DialogueRunner runner = new DialogueRunner();
            runner.Start(graph);

            Assert.IsTrue(runner.IsAwaitingChoice);
            Assert.AreEqual(2, runner.AvailableChoices.Count);

            runner.Choose(0);
            Assert.AreEqual("kind", runner.Current.Id);
            Assert.AreEqual(1f, runner.Variables.GetFloat("affection"), 1e-5f);
        }

        [Test]
        public void Choice_ShowCondition_FiltersAndIndexMapsCorrectly()
        {
            // 第一个选项需要 flag=true 才显示；初始为 false 时被隐藏。
            DialogueCondition needFlag = DialogueCondition.Bool("flag", true);

            ChoiceNode choice = ChoiceNode.Create("c")
                .AddChoice("Secret option", "secret", needFlag)
                .AddChoice("Normal option", "normal")
                .Build();

            DialogueGraph graph = new DialogueGraph("g", "c")
                .AddNode(choice)
                .AddNode(Line("secret", "secret", null))
                .AddNode(Line("normal", "normal", null));

            // 情形一：flag 未设置 => 仅“Normal option”可见，索引 0 应跳到 normal。
            DialogueRunner runner1 = new DialogueRunner();
            runner1.Start(graph);
            Assert.AreEqual(1, runner1.AvailableChoices.Count);
            Assert.AreEqual("Normal option", runner1.AvailableChoices[0].Text);
            runner1.Choose(0);
            Assert.AreEqual("normal", runner1.Current.Id);

            // 情形二：flag=true => 两项可见，索引 0 为“Secret option”。
            DialogueVariables vars = new DialogueVariables();
            vars.SetBool("flag", true);
            DialogueRunner runner2 = new DialogueRunner(vars);
            runner2.Start(graph);
            Assert.AreEqual(2, runner2.AvailableChoices.Count);
            Assert.AreEqual("Secret option", runner2.AvailableChoices[0].Text);
            runner2.Choose(0);
            Assert.AreEqual("secret", runner2.Current.Id);
        }

        [Test]
        public void BranchNode_AutoRoutes_AndNeverSurfaced()
        {
            // a(line) -> branch -> (route=="haru" ? lineHaru : lineDefault)
            BranchNode branch = BranchNode.Create("router")
                .When(DialogueCondition.String("route", "haru"), "lineHaru")
                .Default("lineDefault")
                .Build();

            DialogueGraph graph = new DialogueGraph("g", "a")
                .AddNode(Line("a", "intro", "router"))
                .AddNode(branch)
                .AddNode(Line("lineHaru", "Haru route", null))
                .AddNode(Line("lineDefault", "Default route", null));

            // 情形一：未设 route => 走默认。
            DialogueRunner runnerDefault = new DialogueRunner();
            List<string> enteredDefault = new List<string>();
            runnerDefault.OnNodeEntered += (r, node) => enteredDefault.Add(node.Id);
            runnerDefault.Start(graph);
            runnerDefault.Advance();
            Assert.AreEqual("lineDefault", runnerDefault.Current.Id);
            CollectionAssert.DoesNotContain(enteredDefault, "router");

            // 情形二：route=="haru" => 走 Haru 线。
            DialogueVariables vars = new DialogueVariables();
            vars.SetString("route", "haru");
            DialogueRunner runnerHaru = new DialogueRunner(vars);
            List<string> enteredHaru = new List<string>();
            runnerHaru.OnNodeEntered += (r, node) => enteredHaru.Add(node.Id);
            runnerHaru.Start(graph);
            runnerHaru.Advance();
            Assert.AreEqual("lineHaru", runnerHaru.Current.Id);
            CollectionAssert.DoesNotContain(enteredHaru, "router");
        }

        [Test]
        public void BranchNode_FlagSetByEffect_RoutesAccordingly()
        {
            // 进入 a 时设置 picked=true，随后分支据此路由。
            LineNode a = LineNode.Create("a", "...")
                .Next("router")
                .OnEnter(DialogueEffect.SetBool("picked", true))
                .Build();

            BranchNode branch = BranchNode.Create("router")
                .When(DialogueCondition.Bool("picked", true), "yes")
                .Default("no")
                .Build();

            DialogueGraph graph = new DialogueGraph("g", "a")
                .AddNode(a)
                .AddNode(branch)
                .AddNode(Line("yes", "yes", null))
                .AddNode(Line("no", "no", null));

            DialogueRunner runner = new DialogueRunner();
            runner.Start(graph);
            runner.Advance();
            Assert.AreEqual("yes", runner.Current.Id);
        }

        [Test]
        public void BranchLoop_TerminatesSafely()
        {
            // router_a -> router_b -> router_a ... 形成环；运行器应安全结束而非死循环。
            BranchNode a = BranchNode.Create("router_a")
                .Default("router_b")
                .Build();
            BranchNode b = BranchNode.Create("router_b")
                .Default("router_a")
                .Build();

            DialogueGraph graph = new DialogueGraph("g", "start")
                .AddNode(Line("start", "go", "router_a"))
                .AddNode(a)
                .AddNode(b);

            DialogueRunner runner = new DialogueRunner();
            int finished = 0;
            runner.OnFinished += r => finished++;

            runner.Start(graph);
            runner.Advance(); // 进入分支环

            Assert.IsTrue(runner.IsFinished);
            Assert.IsNull(runner.Current);
            Assert.AreEqual(1, finished);
        }

        [Test]
        public void Choose_OutOfRange_Throws()
        {
            ChoiceNode choice = ChoiceNode.Create("c")
                .AddChoice("Only", "end")
                .Build();
            DialogueGraph graph = new DialogueGraph("g", "c")
                .AddNode(choice)
                .AddNode(Line("end", "done", null));

            DialogueRunner runner = new DialogueRunner();
            runner.Start(graph);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => runner.Choose(5));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => runner.Choose(-1));
        }

        [Test]
        public void Advance_OnChoiceNode_Throws()
        {
            ChoiceNode choice = ChoiceNode.Create("c")
                .AddChoice("Only", "end")
                .Build();
            DialogueGraph graph = new DialogueGraph("g", "c")
                .AddNode(choice)
                .AddNode(Line("end", "done", null));

            DialogueRunner runner = new DialogueRunner();
            runner.Start(graph);

            Assert.Throws<System.InvalidOperationException>(() => runner.Advance());
        }

        [Test]
        public void ChoiceNode_NoVisibleChoices_FinishesAsDeadEnd()
        {
            // 唯一选项的显示条件不满足 => 无可见选项 => 死路结束。
            DialogueCondition never = DialogueCondition.Bool("impossible", true);
            ChoiceNode choice = ChoiceNode.Create("c")
                .AddChoice("Hidden", "target", never)
                .Build();

            DialogueGraph graph = new DialogueGraph("g", "c")
                .AddNode(choice)
                .AddNode(Line("target", "...", null));

            DialogueRunner runner = new DialogueRunner();
            int finished = 0;
            runner.OnFinished += r => finished++;
            runner.Start(graph);

            Assert.IsTrue(runner.IsFinished);
            Assert.AreEqual(1, finished);
            Assert.AreEqual(0, runner.AvailableChoices.Count);
        }

        [Test]
        public void Start_FromExplicitNode_Works()
        {
            DialogueGraph graph = new DialogueGraph("g", "a")
                .AddNode(Line("a", "skip me", "b"))
                .AddNode(Line("b", "start here", null));

            DialogueRunner runner = new DialogueRunner();
            runner.Start(graph, "b");

            Assert.AreEqual("b", runner.Current.Id);
        }

        [Test]
        public void SharedVariables_PersistAcrossStart()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetInt("seen", 1);

            DialogueGraph graph = new DialogueGraph("g", "a")
                .AddNode(Line("a", "x", null));

            DialogueRunner runner = new DialogueRunner(vars);
            runner.Start(graph);

            Assert.AreSame(vars, runner.Variables);
            Assert.AreEqual(1, runner.Variables.GetInt("seen"));
        }
    }
}
