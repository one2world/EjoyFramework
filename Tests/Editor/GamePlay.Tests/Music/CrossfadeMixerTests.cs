//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Music;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Music
{
    /// <summary>
    /// 针对 <see cref="CrossfadeMixer"/> 交叉淡变 / 淡入淡出 / 主音量语义的单元测试。
    /// </summary>
    [TestFixture]
    public class CrossfadeMixerTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void StartCrossfade_HalfWay_BothHalf()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            // 初始：A 活动（增益 1），B 为 0。
            Assert.AreEqual(0, mixer.ActiveChannel);
            Assert.AreEqual(1f, mixer.VolumeA, Delta);
            Assert.AreEqual(0f, mixer.VolumeB, Delta);

            mixer.StartCrossfade(1f);
            Assert.IsTrue(mixer.IsCrossfading);

            mixer.Tick(0.5f);
            Assert.AreEqual(0.5f, mixer.VolumeA, Delta);
            Assert.AreEqual(0.5f, mixer.VolumeB, Delta);
            Assert.IsTrue(mixer.IsCrossfading);
            // 尚未完成，活动通道未变。
            Assert.AreEqual(0, mixer.ActiveChannel);
        }

        [Test]
        public void StartCrossfade_Complete_SwapsAndFires()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            int completed = 0;
            mixer.OnCrossfadeComplete += m => completed++;

            mixer.StartCrossfade(1f);
            mixer.Tick(0.5f);
            mixer.Tick(0.5f);

            Assert.AreEqual(0f, mixer.VolumeA, Delta);
            Assert.AreEqual(1f, mixer.VolumeB, Delta);
            Assert.IsFalse(mixer.IsCrossfading);
            // 活动通道已交换到 B。
            Assert.AreEqual(1, mixer.ActiveChannel);
            // 完成事件恰好触发一次。
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void MasterVolume_ScalesReportedVolumes()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            mixer.MasterVolume = 0.5f;

            // 静止状态：A=1*0.5=0.5，B=0。
            Assert.AreEqual(0.5f, mixer.VolumeA, Delta);
            Assert.AreEqual(0f, mixer.VolumeB, Delta);

            mixer.StartCrossfade(1f);
            mixer.Tick(0.5f);
            // 半程时两通道原始增益各 0.5，乘 Master 0.5 => 0.25。
            Assert.AreEqual(0.25f, mixer.VolumeA, Delta);
            Assert.AreEqual(0.25f, mixer.VolumeB, Delta);
        }

        [Test]
        public void MasterVolume_ClampedToUnitRange()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            mixer.MasterVolume = 5f;
            Assert.AreEqual(1f, mixer.MasterVolume, Delta);

            mixer.MasterVolume = -2f;
            Assert.AreEqual(0f, mixer.MasterVolume, Delta);
        }

        [Test]
        public void FadeOut_RampsActiveToZero()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            mixer.FadeOut(1f);

            mixer.Tick(0.5f);
            Assert.AreEqual(0.5f, mixer.VolumeA, Delta);

            mixer.Tick(0.5f);
            Assert.AreEqual(0f, mixer.VolumeA, Delta);
            Assert.IsFalse(mixer.IsCrossfading);
            // FadeOut 不交换活动通道。
            Assert.AreEqual(0, mixer.ActiveChannel);
        }

        [Test]
        public void FadeOut_ZeroDuration_Instant()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            mixer.FadeOut(0f);

            Assert.AreEqual(0f, mixer.VolumeA, Delta);
            Assert.IsFalse(mixer.IsCrossfading);
        }

        [Test]
        public void StartCrossfade_ZeroDuration_InstantSwap()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            int completed = 0;
            mixer.OnCrossfadeComplete += m => completed++;

            mixer.StartCrossfade(0f);

            Assert.AreEqual(0f, mixer.VolumeA, Delta);
            Assert.AreEqual(1f, mixer.VolumeB, Delta);
            Assert.AreEqual(1, mixer.ActiveChannel);
            Assert.AreEqual(1, completed);
            Assert.IsFalse(mixer.IsCrossfading);
        }

        [Test]
        public void FadeInActive_RampsActiveToOne()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            // 先把活动通道淡出到 0。
            mixer.FadeOut(0f);
            Assert.AreEqual(0f, mixer.VolumeA, Delta);

            mixer.FadeInActive(1f);
            mixer.Tick(0.5f);
            Assert.AreEqual(0.5f, mixer.VolumeA, Delta);

            mixer.Tick(0.5f);
            Assert.AreEqual(1f, mixer.VolumeA, Delta);
            Assert.AreEqual(0, mixer.ActiveChannel);
        }

        [Test]
        public void Tick_NonPositiveDelta_NoOp()
        {
            CrossfadeMixer mixer = new CrossfadeMixer();
            mixer.StartCrossfade(1f);

            mixer.Tick(0f);
            mixer.Tick(-1f);

            // 仍在初始端点，未推进。
            Assert.AreEqual(1f, mixer.VolumeA, Delta);
            Assert.AreEqual(0f, mixer.VolumeB, Delta);
            Assert.IsTrue(mixer.IsCrossfading);
        }
    }
}
