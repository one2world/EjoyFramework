//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Quest;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Quest
{
    /// <summary>
    /// 针对 <see cref="QuestTracker"/> 任务/成就注册、激活、进度推进、完成、领取与前置级联的单元测试。
    /// </summary>
    [TestFixture]
    public class QuestTrackerTests
    {
        // 一个简单的“击杀 3 只哥布林”任务，按需配置。
        private static QuestDefinition KillGoblins(string id, int count = 3, bool autoActivate = false)
        {
            QuestDefinition.Builder builder = QuestDefinition.Create(id)
                .AddObjective("kill_goblin", "kill", "goblin", count);
            if (autoActivate)
            {
                builder.AutoActivate();
            }

            return builder.Build();
        }

        [Test]
        public void Define_NoPrerequisites_BecomesAvailable()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState state = tracker.Define(KillGoblins("q1"));

            Assert.AreEqual(QuestStatus.Available, state.Status);
        }

        [Test]
        public void Define_AutoActivate_BecomesActive()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState state = tracker.Define(KillGoblins("q1", autoActivate: true));

            Assert.AreEqual(QuestStatus.Active, state.Status);
        }

        [Test]
        public void Define_WithUnmetPrerequisites_BecomesLocked()
        {
            QuestTracker tracker = new QuestTracker();
            tracker.Define(KillGoblins("prereq"));

            QuestDefinition dependent = QuestDefinition.Create("dependent")
                .AddObjective("kill_goblin", "kill", "goblin", 1)
                .RequirePrerequisite("prereq")
                .Build();
            QuestState state = tracker.Define(dependent);

            Assert.AreEqual(QuestStatus.Locked, state.Status);
        }

        [Test]
        public void Define_DuplicateId_Throws()
        {
            QuestTracker tracker = new QuestTracker();
            tracker.Define(KillGoblins("q1"));

            Assert.Throws<System.ArgumentException>(() => tracker.Define(KillGoblins("q1")));
        }

        [Test]
        public void Activate_AvailableToActive()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState state = tracker.Define(KillGoblins("q1"));

            Assert.IsTrue(tracker.Activate("q1"));
            Assert.AreEqual(QuestStatus.Active, state.Status);
        }

        [Test]
        public void Activate_OnLocked_ReturnsFalse()
        {
            QuestTracker tracker = new QuestTracker();
            tracker.Define(KillGoblins("prereq"));
            QuestDefinition dependent = QuestDefinition.Create("dependent")
                .AddObjective("kill_goblin", "kill", "goblin", 1)
                .RequirePrerequisite("prereq")
                .Build();
            QuestState state = tracker.Define(dependent);

            Assert.IsFalse(tracker.Activate("dependent"));
            Assert.AreEqual(QuestStatus.Locked, state.Status);
        }

        [Test]
        public void Activate_MissingQuest_ReturnsFalse()
        {
            QuestTracker tracker = new QuestTracker();
            Assert.IsFalse(tracker.Activate("nope"));
        }

        [Test]
        public void ReportProgress_OnlyAdvancesActiveQuests()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState available = tracker.Define(KillGoblins("available", count: 5));
            QuestState active = tracker.Define(KillGoblins("active", count: 5, autoActivate: true));

            int changed = tracker.ReportProgress("kill", "goblin", 2);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(0, available.GetProgress("kill_goblin"));
            Assert.AreEqual(2, active.GetProgress("kill_goblin"));
        }

        [Test]
        public void ReportProgress_MatchesByEventTypeAndTargetKey()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState state = tracker.Define(KillGoblins("q1", count: 5, autoActivate: true));

            // 错误的 targetKey 不匹配。
            Assert.AreEqual(0, tracker.ReportProgress("kill", "orc", 3));
            Assert.AreEqual(0, state.GetProgress("kill_goblin"));

            // 错误的 eventType 不匹配。
            Assert.AreEqual(0, tracker.ReportProgress("collect", "goblin", 3));
            Assert.AreEqual(0, state.GetProgress("kill_goblin"));

            // 完全匹配。
            Assert.AreEqual(1, tracker.ReportProgress("kill", "goblin", 3));
            Assert.AreEqual(3, state.GetProgress("kill_goblin"));
        }

        [Test]
        public void ReportProgress_NullTargetKey_MatchesAnyKey()
        {
            QuestTracker tracker = new QuestTracker();
            // TargetKey 为 null => 匹配该事件类型下的任意键。
            QuestDefinition def = QuestDefinition.Create("anykill")
                .AddObjective("kill_any", "kill", null, 4)
                .AutoActivate()
                .Build();
            QuestState state = tracker.Define(def);

            tracker.ReportProgress("kill", "goblin", 1);
            tracker.ReportProgress("kill", "orc", 1);
            tracker.ReportProgress("kill", "dragon", 1);

            Assert.AreEqual(3, state.GetProgress("kill_any"));
        }

        [Test]
        public void MultiObjective_CompletesOnlyWhenAllDone_CompletedFiresOnce()
        {
            QuestTracker tracker = new QuestTracker();
            QuestDefinition def = QuestDefinition.Create("hunt")
                .AddObjective("kill_goblin", "kill", "goblin", 2)
                .AddObjective("collect_gold", "collect", "gold", 10)
                .AutoActivate()
                .Build();
            QuestState state = tracker.Define(def);

            int completedCount = 0;
            tracker.OnQuestCompleted += (t, s) => completedCount++;

            // 仅完成第一个目标 —— 不应完成整体。
            tracker.ReportProgress("kill", "goblin", 2);
            Assert.AreEqual(QuestStatus.Active, state.Status);
            Assert.AreEqual(0, completedCount);

            // 完成第二个目标 —— 整体完成，事件触发一次。
            tracker.ReportProgress("collect", "gold", 10);
            Assert.AreEqual(QuestStatus.Completed, state.Status);
            Assert.AreEqual(1, completedCount);

            // 已完成后再推进不应再次触发完成事件。
            tracker.ReportProgress("kill", "goblin", 5);
            Assert.AreEqual(1, completedCount);
        }

        [Test]
        public void ReportProgress_ClampsAtRequiredCount_ObjectiveProgressFiresPerAdvance()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState state = tracker.Define(KillGoblins("q1", count: 3, autoActivate: true));

            int progressEvents = 0;
            tracker.OnObjectiveProgress += (t, s, o) => progressEvents++;

            tracker.ReportProgress("kill", "goblin", 1); // 1
            tracker.ReportProgress("kill", "goblin", 1); // 2
            tracker.ReportProgress("kill", "goblin", 5); // 截断到 3

            // 计数在 RequiredCount 处截断。
            Assert.AreEqual(3, state.GetProgress("kill_goblin"));
            Assert.IsTrue(state.IsObjectiveComplete("kill_goblin"));

            // 每次有效推进各触发一次（共三次有效推进）。
            Assert.AreEqual(3, progressEvents);

            // 完成后继续上报不再推进，也不再触发进度事件。
            tracker.ReportProgress("kill", "goblin", 1);
            Assert.AreEqual(3, progressEvents);
        }

        [Test]
        public void Claim_FromCompleted_BecomesClaimed_ReturnsReward_FiresEvent()
        {
            QuestTracker tracker = new QuestTracker();
            object reward = new[] { "gold:100" };
            QuestDefinition def = QuestDefinition.Create("q1")
                .AddObjective("kill_goblin", "kill", "goblin", 1)
                .AutoActivate()
                .WithReward(reward)
                .Build();
            QuestState state = tracker.Define(def);

            QuestState claimedState = null;
            tracker.OnQuestClaimed += (t, s) => claimedState = s;

            tracker.ReportProgress("kill", "goblin", 1);
            Assert.AreEqual(QuestStatus.Completed, state.Status);

            Assert.IsTrue(tracker.Claim("q1"));
            Assert.AreEqual(QuestStatus.Claimed, state.Status);
            Assert.AreSame(state, claimedState);
            // 奖励通过 state.Definition.Reward 原样回传。
            Assert.AreSame(reward, claimedState.Definition.Reward);
        }

        [Test]
        public void Claim_FromNonCompleted_ReturnsFalse()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState state = tracker.Define(KillGoblins("q1", autoActivate: true));

            // 仍在 Active，未完成。
            Assert.IsFalse(tracker.Claim("q1"));
            Assert.AreEqual(QuestStatus.Active, state.Status);

            // 不存在的任务。
            Assert.IsFalse(tracker.Claim("nope"));
        }

        [Test]
        public void PrerequisiteCascade_ClaimingUnlocksDependent_AutoActivates()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState prereq = tracker.Define(KillGoblins("prereq", count: 1, autoActivate: true));

            QuestDefinition dependentDef = QuestDefinition.Create("dependent")
                .AddObjective("collect_gold", "collect", "gold", 1)
                .RequirePrerequisite("prereq")
                .AutoActivate()
                .Build();
            QuestState dependent = tracker.Define(dependentDef);
            Assert.AreEqual(QuestStatus.Locked, dependent.Status);

            QuestState availableEventState = null;
            tracker.OnQuestAvailable += (t, s) => availableEventState = s;

            // 完成并领取前置。
            tracker.ReportProgress("kill", "goblin", 1);
            Assert.AreEqual(QuestStatus.Completed, prereq.Status);
            Assert.IsTrue(tracker.Claim("prereq"));

            // 依赖任务被解锁（触发 OnQuestAvailable），且因 AutoActivate 立即 Active。
            Assert.AreSame(dependent, availableEventState);
            Assert.AreEqual(QuestStatus.Active, dependent.Status);
        }

        [Test]
        public void PrerequisiteCascade_MultiLevelChain_UnlocksTransitively()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState a = tracker.Define(KillGoblins("a", count: 1, autoActivate: true));

            QuestState b = tracker.Define(QuestDefinition.Create("b")
                .AddObjective("o", "kill", "goblin", 1)
                .RequirePrerequisite("a")
                .AutoActivate()
                .Build());

            QuestState c = tracker.Define(QuestDefinition.Create("c")
                .AddObjective("o", "kill", "goblin", 1)
                .RequirePrerequisite("b")
                .Build());

            Assert.AreEqual(QuestStatus.Locked, b.Status);
            Assert.AreEqual(QuestStatus.Locked, c.Status);

            // 完成并领取 a -> 解锁并自动激活 b。
            tracker.ReportProgress("kill", "goblin", 1);
            tracker.Claim("a");
            Assert.AreEqual(QuestStatus.Active, b.Status);
            Assert.AreEqual(QuestStatus.Locked, c.Status);

            // 完成并领取 b -> 解锁 c（非自动激活，停在 Available）。
            tracker.ReportProgress("kill", "goblin", 1);
            tracker.Claim("b");
            Assert.AreEqual(QuestStatus.Available, c.Status);
        }

        [Test]
        public void Achievement_TracksProgressFromDefine_AndCompletes()
        {
            QuestTracker tracker = new QuestTracker();
            QuestDefinition achievement = QuestDefinition.Create("ach_slayer")
                .AddObjective("kills", "kill", null, 100)
                .AsAchievement()
                .WithReward("badge:slayer")
                .Build();
            QuestState state = tracker.Define(achievement);

            // 成就（IsAchievement + AutoActivate）注册后即 Active。
            Assert.IsTrue(state.Definition.IsAchievement);
            Assert.AreEqual(QuestStatus.Active, state.Status);

            int completed = 0;
            tracker.OnQuestCompleted += (t, s) => completed++;

            // 全局追踪：任意 key 的 kill 都计入。
            tracker.ReportProgress("kill", "goblin", 40);
            tracker.ReportProgress("kill", "orc", 40);
            Assert.AreEqual(QuestStatus.Active, state.Status);
            Assert.AreEqual(0, completed);

            tracker.ReportProgress("kill", "dragon", 30); // 110 -> 截断 100，完成
            Assert.AreEqual(QuestStatus.Completed, state.Status);
            Assert.AreEqual(1, completed);

            Assert.IsTrue(tracker.Claim("ach_slayer"));
            Assert.AreEqual(QuestStatus.Claimed, state.Status);
        }

        [Test]
        public void ReportProgress_ReturnsCountOfChangedQuests()
        {
            QuestTracker tracker = new QuestTracker();
            tracker.Define(KillGoblins("q1", count: 5, autoActivate: true));
            tracker.Define(KillGoblins("q2", count: 5, autoActivate: true));
            // 第三个任务监听不同事件，不应被本次上报改变。
            tracker.Define(QuestDefinition.Create("q3")
                .AddObjective("collect_gold", "collect", "gold", 5)
                .AutoActivate()
                .Build());

            int changed = tracker.ReportProgress("kill", "goblin", 1);

            // 两个 kill 任务改变，collect 任务未改变。
            Assert.AreEqual(2, changed);
        }

        [Test]
        public void TryGet_And_Get_BehaveConsistently()
        {
            QuestTracker tracker = new QuestTracker();
            QuestState defined = tracker.Define(KillGoblins("q1"));

            Assert.IsTrue(tracker.TryGet("q1", out QuestState got));
            Assert.AreSame(defined, got);
            Assert.AreSame(defined, tracker.Get("q1"));

            Assert.IsFalse(tracker.TryGet("missing", out QuestState missing));
            Assert.IsNull(missing);
            Assert.IsNull(tracker.Get("missing"));
        }

        [Test]
        public void Quests_EnumeratesInRegistrationOrder()
        {
            QuestTracker tracker = new QuestTracker();
            tracker.Define(KillGoblins("a"));
            tracker.Define(KillGoblins("b"));
            tracker.Define(KillGoblins("c"));

            List<string> ids = new List<string>();
            foreach (QuestState state in tracker.Quests)
            {
                ids.Add(state.Definition.Id);
            }

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ids);
        }

        [Test]
        public void Completion_ReflectsAverageObjectiveRatio()
        {
            QuestTracker tracker = new QuestTracker();
            QuestDefinition def = QuestDefinition.Create("multi")
                .AddObjective("kill_goblin", "kill", "goblin", 4)
                .AddObjective("collect_gold", "collect", "gold", 10)
                .AutoActivate()
                .Build();
            QuestState state = tracker.Define(def);

            Assert.AreEqual(0f, state.Completion, 1e-4f);

            // 第一个目标 2/4 = 0.5，第二个 0/10 = 0 -> 平均 0.25。
            tracker.ReportProgress("kill", "goblin", 2);
            Assert.AreEqual(0.25f, state.Completion, 1e-4f);

            // 两个目标全完成 -> 1.0。
            tracker.ReportProgress("kill", "goblin", 2);
            tracker.ReportProgress("collect", "gold", 10);
            Assert.AreEqual(1f, state.Completion, 1e-4f);
        }
    }
}
