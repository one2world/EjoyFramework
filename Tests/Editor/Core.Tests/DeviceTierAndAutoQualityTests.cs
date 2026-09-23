//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Quality;

namespace EjoyFramework.Tests
{
    /// <summary>WS4-M3：设备分级器（评分 / 阈值 / 覆盖 / 上限 / 规则文本与报错）与自动画质控制器。</summary>
    public sealed class DeviceTierAndAutoQualityTests
    {
        private static DeviceProfile Phone(int cores, int mhz, int memMb, int vramMb, int shader, string gpu = "Mali-G52", string model = "Generic")
        {
            DeviceProfile p = default(DeviceProfile);
            p.IsMobile = true;
            p.CpuCores = cores;
            p.CpuFrequencyMHz = mhz;
            p.SystemMemoryMB = memMb;
            p.GraphicsMemoryMB = vramMb;
            p.GraphicsShaderLevel = shader;
            p.GpuName = gpu;
            p.DeviceModel = model;
            p.CpuName = "ARM64";
            p.OperatingSystem = "Android OS 14";
            return p;
        }

        // ---------------- 评分 / 阈值 ----------------

        [Test]
        public void Score_IsWeightedAndClamped()
        {
            Assert.AreEqual(1f, DeviceTierClassifier.Score(Phone(16, 4000, 16384, 8192, 50)), 1e-5f, "全部饱和 = 1。");
            Assert.AreEqual(0f, DeviceTierClassifier.Score(Phone(0, 0, 0, 0, 30)), 1e-5f, "全部为 0（着色器低于 35 夹到 0）。");
            // 4 核 1.4GHz 4GB 2GB SL45：.15*.5 + .15*.5 + .3*.5 + .25*.5 + .15*(10/15) = .525
            Assert.AreEqual(0.525f, DeviceTierClassifier.Score(Phone(4, 1400, 4096, 2048, 45)), 1e-4f);
        }

        [Test]
        public void Classify_ByThresholds_MobileAndDesktopDiffer()
        {
            DeviceTierClassifier c = new DeviceTierClassifier();
            DeviceProfile p = Phone(8, 2800, 8192, 4096, 50);   // 移动参考下全部饱和 = 1.0
            DeviceTierResult r = c.Classify(in p);
            Assert.AreEqual(DeviceTier.Ultra, r.Tier);
            Assert.AreEqual(DeviceTierReason.Score, r.Reason);

            p.IsMobile = false;   // 桌面参考值更大：.15 + .15 + .3*.5 + .25*.5 + .15 = .725
            r = c.Classify(in p);
            Assert.AreEqual(0.725f, r.Score, 1e-4f);
            Assert.AreEqual(DeviceTier.High, r.Tier, "桌面阈值 .65 ≤ .725 < .85。");
        }

        [Test]
        public void Override_WinsFirstMatch_AndIgnoresCaps()
        {
            DeviceTierClassifier c = new DeviceTierClassifier();
            c.AddOverride(DeviceTierClassifier.Field.Gpu, "adreno (tm) 740", DeviceTier.Ultra, 3);
            c.AddOverride(DeviceTierClassifier.Field.Gpu, "Adreno", DeviceTier.Low, 4);
            c.AddCap(DeviceTierClassifier.Metric.Memory, 8000, DeviceTier.Low, 5);
            DeviceProfile p = Phone(8, 3000, 6144, 3072, 50, "Adreno (TM) 740");
            DeviceTierResult r = c.Classify(in p);
            Assert.AreEqual(DeviceTier.Ultra, r.Tier, "大小写不敏感子串，先写先得。");
            Assert.AreEqual(DeviceTierReason.Override, r.Reason);
            Assert.AreEqual(3, r.RuleLine);
            Assert.Greater(r.Score, 0f, "覆盖时仍给出评分，供后台校准。");
        }

        [Test]
        public void Cap_LowersScoreResult_OnlyWhenBelow()
        {
            DeviceTierClassifier c = new DeviceTierClassifier();
            c.AddCap(DeviceTierClassifier.Metric.Memory, 3072, DeviceTier.Low, 9);
            DeviceProfile strong = Phone(8, 3000, 2048, 4096, 50);   // 内存 2GB，其余很强
            DeviceTierResult r = c.Classify(in strong);
            Assert.AreEqual(DeviceTier.Low, r.Tier);
            Assert.AreEqual(DeviceTierReason.Capped, r.Reason);
            Assert.AreEqual(9, r.RuleLine);

            DeviceProfile weak = Phone(2, 1000, 1024, 512, 35);      // 本就是 Minimum，上限不抬高
            r = c.Classify(in weak);
            Assert.AreEqual(DeviceTier.Minimum, r.Tier);
            Assert.AreEqual(DeviceTierReason.Score, r.Reason);
        }

        [Test]
        public void Rules_ParseAllDirectives_WithCommentsAndQuotes()
        {
            DeviceTierClassifier c = new DeviceTierClassifier();
            c.LoadRules("# 注释行\r\n"
                        + "override model \"SM-A 105\" = low   # 行尾注释\r\n"
                        + "\r\n"
                        + "override gpu \"Apple A17\" = 4\n"
                        + "cap vram < 1024 = Minimum\n"
                        + "thresholds mobile 0.3 0.5 0.6 0.9\n"
                        + "thresholds desktop 0.2 0.3 0.4 0.5\n");
            Assert.AreEqual(2, c.OverrideCount);
            Assert.AreEqual(1, c.CapCount);

            DeviceProfile p = Phone(4, 1400, 4096, 2048, 45, "Mali", "Samsung SM-A 105F");
            DeviceTierResult r = c.Classify(in p);
            Assert.AreEqual(DeviceTier.Low, r.Tier);
            Assert.AreEqual(2, r.RuleLine, "行号从 1 起，注释 / 空行计入。");

            p.DeviceModel = "Other";   // .525 在新移动阈值 [.5, .6) → Medium
            Assert.AreEqual(DeviceTier.Medium, c.Classify(in p).Tier);
            p.GraphicsMemoryMB = 512;  // 显存上限规则 → Minimum
            Assert.AreEqual(DeviceTier.Minimum, c.Classify(in p).Tier);
        }

        [TestCase("frobnicate x", 1, "未知指令")]
        [TestCase("override gpu \"x\" = Legendary", 1, "未知档位")]
        [TestCase("\n\noverride gpu \"unterminated = 3", 3, "引号未闭合")]
        [TestCase("cap memory > 3 = Low", 1, "格式应为")]
        [TestCase("override weight \"x\" = 1", 1, "未知字段")]
        [TestCase("thresholds mobile 0.5 0.4 0.6 0.9", 1, "阈值须满足")]
        [TestCase("cap cores < many = Low", 1, "不是整数")]
        public void Rules_Errors_ReportLineNumber(string text, int line, string fragment)
        {
            DeviceTierClassifier c = new DeviceTierClassifier();
            FrameworkException ex = Assert.Throws<FrameworkException>(() => c.LoadRules(text));
            StringAssert.Contains("第 " + line + " 行", ex.Message);
            StringAssert.Contains(fragment, ex.Message);
        }

        // ---------------- 自动画质控制器 ----------------

        private static AutoQualityDecision Run(AutoQualityController c, float ms, float seconds, ref int level, int min, int max, List<AutoQualityDecision> log = null)
        {
            AutoQualityDecision last = AutoQualityDecision.None;
            // dt = 1/32 秒：二进制精确，0.5s 周期恰好 16 帧，时间累计无舍入误差
            int frames = (int)System.Math.Round(seconds * 32.0);
            for (int i = 0; i < frames; i++)
            {
                AutoQualityDecision d = c.Feed(ms, 0.03125f, level, min, max);
                if (d == AutoQualityDecision.LevelDown) level--;
                if (d == AutoQualityDecision.LevelUp) level++;
                if (d != AutoQualityDecision.None)
                {
                    last = d;
                    if (log != null) log.Add(d);
                }
            }

            return last;
        }

        private static AutoQualityController NewController()
        {
            AutoQualityController c = new AutoQualityController();
            c.TargetFrameMs = 20f;
            return c;
        }

        [Test]
        public void OverBudget_LowersRenderScaleFirst_ThenLevelAfterSustain()
        {
            AutoQualityController c = NewController();
            int level = 3;
            List<AutoQualityDecision> log = new List<AutoQualityDecision>();
            Run(c, 22f, 3.0f, ref level, 0, 4, log);   // 超 5%~10%：每 0.5s 降一步 0.05
            Assert.AreEqual(3, level, "分辨率未到底前不降档。");
            Assert.AreEqual(0.7f, c.RenderScale, 1e-4f, "6 个周期降到下限 0.7。");
            Assert.IsTrue(log.TrueForAll(d => d == AutoQualityDecision.RenderScaleChanged));

            Run(c, 22f, 1.5f, ref level, 0, 4);
            Assert.AreEqual(3, level, "到底后持续 < 2s 不降档。");
            Run(c, 22f, 0.5f, ref level, 0, 4);
            Assert.AreEqual(2, level, "到底后持续 2s 降一档。");
        }

        [Test]
        public void SevereOverBudget_DoubleScaleStep()
        {
            AutoQualityController c = NewController();
            int level = 3;
            Run(c, 30f, 0.5f, ref level, 0, 4);   // 1.5 倍 > SevereRatio 1.3
            Assert.AreEqual(0.9f, c.RenderScale, 1e-4f);
        }

        [Test]
        public void UnderBudget_RestoresScaleThenLevelUpAfterSustainAndCooldown()
        {
            AutoQualityController c = NewController();
            c.ResetScale(0.9f);
            int level = 2;
            Run(c, 10f, 2.0f, ref level, 0, 4);   // 每 0.5s +0.025：4 周期回到 1.0
            Assert.AreEqual(1f, c.RenderScale, 1e-4f);
            Assert.AreEqual(2, level);
            Run(c, 10f, 7.5f, ref level, 0, 4);
            Assert.AreEqual(2, level, "满分辨率后富余需持续 8s。");
            Run(c, 10f, 1.0f, ref level, 0, 4);
            Assert.AreEqual(3, level);
            Run(c, 10f, 10f, ref level, 0, 4);
            Assert.AreEqual(3, level, "升档冷却 15s 内不再升。");
            Run(c, 10f, 6f, ref level, 0, 4);
            Assert.AreEqual(4, level);
            Run(c, 10f, 30f, ref level, 0, 4);
            Assert.AreEqual(4, level, "不超过上限。");
        }

        [Test]
        public void FailedUpgrade_DoublesCooldown_StableUpgradeRestoresIt()
        {
            AutoQualityController c = NewController();
            int level = 2;
            Run(c, 10f, 8.5f, ref level, 0, 4);    // 8.0s 时升档
            Assert.AreEqual(3, level);
            Assert.AreEqual(15f, c.UpCooldownSeconds);

            // 升档后立刻扛不住：先降分辨率 3s 到底，再 2s 降档——都在 10s 失败窗口内
            Run(c, 30f, 3.5f, ref level, 0, 4);
            Assert.AreEqual(2, level);
            Assert.AreEqual(30f, c.UpCooldownSeconds, "升档失败，冷却翻倍。");

            // 再次恢复富余：分辨率回满 + 8s 持续 + 30s 冷却后才升
            Run(c, 10f, 20f, ref level, 0, 4);
            Assert.AreEqual(2, level, "冷却 30s 未到。");
            Run(c, 10f, 15f, ref level, 0, 4);
            Assert.AreEqual(3, level);
            Run(c, 10f, 11f, ref level, 0, 3);   // 稳定超过失败窗口
            Assert.AreEqual(15f, c.UpCooldownSeconds, "稳定后恢复基础冷却。");
        }

        [Test]
        public void Hitches_AreIgnored_InBandResetsSustain()
        {
            AutoQualityController c = NewController();
            c.ResetScale(0.7f);
            int level = 3;
            Run(c, 1000f, 5f, ref level, 0, 4);
            Assert.AreEqual(3, level, "加载卡顿帧不计入。");
            Assert.AreEqual(0.7f, c.RenderScale, 1e-4f);

            Run(c, 22f, 1.5f, ref level, 0, 4);
            Run(c, 19f, 0.5f, ref level, 0, 4);   // 回到带内：超预算累计清零
            Run(c, 22f, 1.5f, ref level, 0, 4);
            Assert.AreEqual(3, level, "累计被打断，1.5s 不足 2s。");
            Run(c, 22f, 5f, ref level, 1, 4);
            Assert.AreEqual(1, level, "不低于下限。");
        }

        [Test]
        public void Feed_DoesNotAllocate()
        {
            AutoQualityController c = NewController();
            int level = 3;
            TestDelegate body = () =>
            {
                for (int i = 0; i < 500; i++)
                {
                    AutoQualityDecision d = c.Feed(i % 2 == 0 ? 30f : 10f, 0.02f, level, 0, 4);
                    if (d == AutoQualityDecision.LevelDown && level > 0) level--;
                    if (d == AutoQualityDecision.LevelUp && level < 4) level++;
                }
            };
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }
    }
}
