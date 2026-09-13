//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Atmosphere;

namespace EjoyFramework.GamePlay.Tests.Atmosphere
{
    public class WeatherSystemTests
    {
        private const float Delta = 1e-3f;

        [Test]
        public void Constructor_Default_IsClearFullIntensity_NotTransitioning()
        {
            var weather = new WeatherSystem();
            Assert.AreEqual(WeatherType.Clear, weather.Current);
            Assert.AreEqual(WeatherType.Clear, weather.Target);
            Assert.AreEqual(1f, weather.Intensity, Delta);
            Assert.IsFalse(weather.IsTransitioning);
        }

        [Test]
        public void SetWeather_Instant_ChangesImmediately()
        {
            var weather = new WeatherSystem();
            weather.SetWeather(WeatherType.Rain, 0.5f);

            Assert.AreEqual(WeatherType.Rain, weather.Current);
            Assert.AreEqual(0.5f, weather.Intensity, Delta);
            Assert.IsFalse(weather.IsTransitioning);
        }

        [Test]
        public void SetWeather_Instant_FiresOnWeatherChangedOnce()
        {
            var weather = new WeatherSystem();
            var fired = new List<WeatherType>();
            weather.OnWeatherChanged += (w, t) => fired.Add(t);

            weather.SetWeather(WeatherType.Snow, 1f);
            Assert.AreEqual(1, fired.Count);
            Assert.AreEqual(WeatherType.Snow, fired[0]);
        }

        [Test]
        public void SetWeather_SameType_DoesNotFireOnWeatherChanged()
        {
            var weather = new WeatherSystem(WeatherType.Fog);
            int count = 0;
            weather.OnWeatherChanged += (w, t) => count++;

            weather.SetWeather(WeatherType.Fog, 0.3f);
            Assert.AreEqual(0, count, "切到相同类型不应触发事件");
        }

        [Test]
        public void SetWeather_IntensityClampedTo01()
        {
            var weather = new WeatherSystem();
            weather.SetWeather(WeatherType.Storm, 5f);
            Assert.AreEqual(1f, weather.Intensity, Delta);

            weather.SetWeather(WeatherType.Clear, -2f);
            Assert.AreEqual(0f, weather.Intensity, Delta);
        }

        [Test]
        public void SetWeather_Transition_TypeSwitchesAtStart_IntensityLerps()
        {
            var weather = new WeatherSystem(WeatherType.Clear);
            // Clear@1 -> Rain@0 over 4s.
            weather.SetWeather(WeatherType.Rain, 0f, 4f);

            // 类型立即切换（过渡开始即切换）。
            Assert.AreEqual(WeatherType.Rain, weather.Current);
            Assert.IsTrue(weather.IsTransitioning);
            // 起始强度仍为 1（来自 Clear 的强度）。
            Assert.AreEqual(1f, weather.Intensity, Delta);

            weather.Tick(2f); // 一半 => 1 + (0-1)*0.5 = 0.5
            Assert.AreEqual(0.5f, weather.Intensity, Delta);
            Assert.IsTrue(weather.IsTransitioning);

            weather.Tick(2f); // 完成 => 0
            Assert.AreEqual(0f, weather.Intensity, Delta);
            Assert.IsFalse(weather.IsTransitioning);
        }

        [Test]
        public void Transition_OnWeatherChanged_FiresOnceAtStart()
        {
            var weather = new WeatherSystem(WeatherType.Clear);
            int count = 0;
            weather.OnWeatherChanged += (w, t) => count++;

            weather.SetWeather(WeatherType.Rain, 1f, 4f);
            Assert.AreEqual(1, count, "过渡开始触发一次");

            weather.Tick(2f);
            weather.Tick(2f);
            Assert.AreEqual(1, count, "过渡过程中/完成不再触发");
        }

        [Test]
        public void Tick_OvershootDuration_ClampsToTarget()
        {
            var weather = new WeatherSystem(WeatherType.Clear);
            weather.SetWeather(WeatherType.Rain, 0.2f, 2f);

            weather.Tick(10f); // 远超时长
            Assert.AreEqual(0.2f, weather.Intensity, Delta);
            Assert.IsFalse(weather.IsTransitioning);
        }

        [Test]
        public void Tick_WhenNotTransitioning_IsNoOp()
        {
            var weather = new WeatherSystem();
            weather.SetWeather(WeatherType.Cloudy, 0.7f);
            weather.Tick(1f);
            Assert.AreEqual(0.7f, weather.Intensity, Delta);
        }

        [Test]
        public void RollNext_WeightedTable_PicksDeterministicallyWithInjectedRandom()
        {
            // Clear -> {Rain weight 1, Storm weight 3}. total=4。
            // roll*total 命中区间：[0,1)->Rain, [1,4)->Storm。
            double rollValue = 0d;
            var weather = new WeatherSystem(WeatherType.Clear, () => rollValue);
            weather.DefineTransition(WeatherType.Clear, WeatherType.Rain, 1f);
            weather.DefineTransition(WeatherType.Clear, WeatherType.Storm, 3f);

            // roll=0.1 => 0.1*4=0.4 < 1 => Rain。
            rollValue = 0.1d;
            weather.RollNext(0f);
            Assert.AreEqual(WeatherType.Rain, weather.Current);

            // 回到 Clear，roll=0.5 => 0.5*4=2.0 ∈ [1,4) => Storm。
            weather.SetWeather(WeatherType.Clear);
            rollValue = 0.5d;
            weather.RollNext(0f);
            Assert.AreEqual(WeatherType.Storm, weather.Current);
        }

        [Test]
        public void RollNext_BoundaryRoll_PicksLastBucket()
        {
            double rollValue = 0.999999d;
            var weather = new WeatherSystem(WeatherType.Clear, () => rollValue);
            weather.DefineTransition(WeatherType.Clear, WeatherType.Rain, 1f);
            weather.DefineTransition(WeatherType.Clear, WeatherType.Snow, 1f);

            weather.RollNext(0f);
            Assert.AreEqual(WeatherType.Snow, weather.Current);
        }

        [Test]
        public void RollNext_NoTransitionsDefined_IsSafeNoOp()
        {
            bool randomCalled = false;
            var weather = new WeatherSystem(WeatherType.Clear, () => { randomCalled = true; return 0.5d; });
            int changeCount = 0;
            weather.OnWeatherChanged += (w, t) => changeCount++;

            weather.RollNext(1f);

            Assert.AreEqual(WeatherType.Clear, weather.Current, "无转移表应保持不变");
            Assert.IsFalse(weather.IsTransitioning);
            Assert.AreEqual(0, changeCount, "不应触发事件");
            Assert.IsFalse(randomCalled, "无转移表时不应消耗随机源");
        }

        [Test]
        public void RollNext_WithTransitionSeconds_StartsTransition()
        {
            var weather = new WeatherSystem(WeatherType.Clear, () => 0d);
            weather.DefineTransition(WeatherType.Clear, WeatherType.Fog, 1f);

            // 当前 Clear@1，Roll 到 Fog 目标强度 1，过渡 2s。
            // 起止强度都是 1，无可插值量 => 立即落定，不算过渡。
            weather.RollNext(2f);
            Assert.AreEqual(WeatherType.Fog, weather.Current);
            Assert.AreEqual(1f, weather.Intensity, Delta);

            // 用一个会改变强度的场景验证过渡确实推进。
            weather.SetWeather(WeatherType.Clear, 0f); // 强度归零
            weather.DefineTransition(WeatherType.Clear, WeatherType.Rain, 1f);
            weather.RollNext(2f); // Clear@0 -> Rain@1 over 2s
            Assert.IsTrue(weather.IsTransitioning);
            weather.Tick(1f);
            Assert.AreEqual(0.5f, weather.Intensity, Delta);
        }

        [Test]
        public void DefineTransition_NonPositiveWeight_Ignored()
        {
            var weather = new WeatherSystem(WeatherType.Clear, () => 0d);
            weather.DefineTransition(WeatherType.Clear, WeatherType.Rain, 0f);
            weather.DefineTransition(WeatherType.Clear, WeatherType.Storm, -1f);

            int changeCount = 0;
            weather.OnWeatherChanged += (w, t) => changeCount++;

            // 所有权重均无效 => 转移表为空 => no-op。
            weather.RollNext(0f);
            Assert.AreEqual(WeatherType.Clear, weather.Current);
            Assert.AreEqual(0, changeCount);
        }
    }
}
