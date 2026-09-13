//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.TouchInput;

namespace EjoyFramework.GamePlay.Tests.TouchInput
{
    public class GestureRecognizerTests
    {
        private const float Delta = 1e-3f;

        private static TouchSample Sample(int id, float x, float y, TouchPhaseKind phase, float time)
        {
            return new TouchSample(id, x, y, phase, time);
        }

        [Test]
        public void Tap_QuickDownUp_SmallMove_FiresOnTap()
        {
            var r = new GestureRecognizer();
            int taps = 0;
            GestureEvent captured = default;
            r.OnTap += e => { taps++; captured = e; };

            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Update(0f);
            r.Feed(Sample(0, 103f, 101f, TouchPhaseKind.Ended, 0.1f)); // 短按、移动 < 阈值。

            Assert.AreEqual(1, taps);
            Assert.AreEqual(GestureKind.Tap, captured.Kind);
            Assert.AreEqual(103f, captured.X, Delta);
            Assert.AreEqual(101f, captured.Y, Delta);
        }

        [Test]
        public void Tap_MovementTooLarge_DoesNotFire()
        {
            var r = new GestureRecognizer();
            int taps = 0;
            r.OnTap += _ => taps++;

            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 200f, 100f, TouchPhaseKind.Moved, 0.05f)); // 移动 100px > 20px。
            r.Feed(Sample(0, 200f, 100f, TouchPhaseKind.Ended, 0.1f));

            Assert.AreEqual(0, taps);
        }

        [Test]
        public void Tap_HeldTooLong_DoesNotFire()
        {
            var r = new GestureRecognizer();
            int taps = 0;
            r.OnTap += _ => taps++;

            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Ended, 0.5f)); // 0.5s > TapMaxDuration 0.3s。

            Assert.AreEqual(0, taps);
        }

        [Test]
        public void LongPress_FiresAfterUpdatePastDuration_WhenHeld()
        {
            var r = new GestureRecognizer();
            int longPresses = 0;
            GestureEvent captured = default;
            r.OnLongPress += e => { longPresses++; captured = e; };

            r.Feed(Sample(0, 50f, 60f, TouchPhaseKind.Began, 0f));
            r.Update(0.3f);
            Assert.AreEqual(0, longPresses, "未到阈值不应触发");

            r.Update(0.65f); // 0.65 > LongPressDuration 0.6。
            Assert.AreEqual(1, longPresses);
            Assert.AreEqual(GestureKind.LongPress, captured.Kind);

            r.Update(1.0f); // 不应重复触发。
            Assert.AreEqual(1, longPresses);
        }

        [Test]
        public void LongPress_FrameworkDrivenUpdate_FiresUsingSampleClock()
        {
            // 回归：框架 Update(elapse, realElapse) 路径下，sample.Time 通常是较大的绝对时钟值
            // （如 realtimeSinceStartup）。修复前 m_Now 从 0 累加、与 sample.Time 不同源 → held 为负 →
            // 长按永不触发。Feed 现在把 m_Now 前向同步到 sample.Time，使两者同源。
            var r = new GestureRecognizer();
            int longPresses = 0;
            r.OnLongPress += _ => longPresses++;

            r.Feed(Sample(0, 50f, 60f, TouchPhaseKind.Began, 123.0f));
            r.Update(0.3f, 0.3f);
            Assert.AreEqual(0, longPresses, "未到阈值不应触发");

            r.Update(0.4f, 0.4f); // 累计 0.7 > LongPressDuration 0.6
            Assert.AreEqual(1, longPresses);
        }

        [Test]
        public void LongPress_DoesNotFire_IfMovedBeyondTapThreshold()
        {
            var r = new GestureRecognizer();
            int longPresses = 0;
            r.OnLongPress += _ => longPresses++;

            r.Feed(Sample(0, 50f, 60f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 120f, 60f, TouchPhaseKind.Moved, 0.1f)); // 移动 70px > 20px。
            r.Update(0.7f);

            Assert.AreEqual(0, longPresses);
        }

        [Test]
        public void Swipe_RightFast_FiresWithPositiveDeltaX_AndMagnitude()
        {
            var r = new GestureRecognizer();
            int swipes = 0;
            GestureEvent captured = default;
            r.OnSwipe += e => { swipes++; captured = e; };

            r.Feed(Sample(0, 100f, 200f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 200f, 200f, TouchPhaseKind.Moved, 0.05f));
            r.Feed(Sample(0, 300f, 200f, TouchPhaseKind.Ended, 0.1f)); // 位移 200px > 60px, 0.1s 快速。

            Assert.AreEqual(1, swipes);
            Assert.AreEqual(GestureKind.Swipe, captured.Kind);
            Assert.Greater(captured.DeltaX, 0f, "向右滑动 DeltaX 应为正");
            Assert.AreEqual(0f, captured.DeltaY, Delta);
            Assert.AreEqual(200f, captured.Magnitude, Delta, "Magnitude 应为滑动距离");
        }

        [Test]
        public void Swipe_Down_FiresWithNegativeDeltaY()
        {
            var r = new GestureRecognizer();
            GestureEvent captured = default;
            int swipes = 0;
            r.OnSwipe += e => { swipes++; captured = e; };

            r.Feed(Sample(0, 100f, 300f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 100f, 200f, TouchPhaseKind.Moved, 0.05f));
            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Ended, 0.1f)); // 向下（y 减小）。

            Assert.AreEqual(1, swipes);
            Assert.Less(captured.DeltaY, 0f, "向下滑动 DeltaY 应为负");
        }

        [Test]
        public void DoubleTap_TwoQuickTapsWithinGap_FiresOnDoubleTap()
        {
            var r = new GestureRecognizer();
            int taps = 0;
            int doubleTaps = 0;
            r.OnTap += _ => taps++;
            r.OnDoubleTap += _ => doubleTaps++;

            // 第一次单击。
            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Ended, 0.1f));
            r.Update(0.1f);

            // 第二次单击，间隔 0.1s < DoubleTapMaxGap 0.3s，位置相近。
            r.Feed(Sample(1, 102f, 101f, TouchPhaseKind.Began, 0.2f));
            r.Feed(Sample(1, 102f, 101f, TouchPhaseKind.Ended, 0.25f));

            Assert.AreEqual(1, doubleTaps, "应触发一次双击");
            Assert.AreEqual(1, taps, "仅第一次为单击；第二次落地为双击不再额外 Tap");
        }

        [Test]
        public void DoubleTap_GapTooLarge_FiresTwoTaps_NoDoubleTap()
        {
            var r = new GestureRecognizer();
            int taps = 0;
            int doubleTaps = 0;
            r.OnTap += _ => taps++;
            r.OnDoubleTap += _ => doubleTaps++;

            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Ended, 0.1f));
            r.Update(0.5f); // 超过双击窗口，清理候选。

            r.Feed(Sample(1, 100f, 100f, TouchPhaseKind.Began, 0.8f));
            r.Feed(Sample(1, 100f, 100f, TouchPhaseKind.Ended, 0.85f));

            Assert.AreEqual(0, doubleTaps);
            Assert.AreEqual(2, taps);
        }

        [Test]
        public void Pinch_TwoFingersMovingApart_FiresWithScaleGreaterThanOne()
        {
            var r = new GestureRecognizer();
            int pinches = 0;
            GestureEvent captured = default;
            r.OnPinch += e => { pinches++; captured = e; };

            // 两指按下：初始距离 100px。
            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(1, 200f, 100f, TouchPhaseKind.Began, 0f));

            // 两指张开：距离增大到 200px。
            r.Feed(Sample(0, 50f, 100f, TouchPhaseKind.Moved, 0.05f));
            r.Feed(Sample(1, 250f, 100f, TouchPhaseKind.Moved, 0.05f));

            Assert.Greater(pinches, 0, "应至少触发一次捏合");
            Assert.AreEqual(GestureKind.Pinch, captured.Kind);
            Assert.Greater(captured.Magnitude, 1f, "张开时缩放比例应 > 1");
        }

        [Test]
        public void Pinch_TwoFingersMovingTogether_FiresWithScaleLessThanOne()
        {
            var r = new GestureRecognizer();
            GestureEvent captured = default;
            int pinches = 0;
            r.OnPinch += e => { pinches++; captured = e; };

            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(1, 300f, 100f, TouchPhaseKind.Began, 0f)); // 初始 200px。

            r.Feed(Sample(0, 150f, 100f, TouchPhaseKind.Moved, 0.05f));
            r.Feed(Sample(1, 250f, 100f, TouchPhaseKind.Moved, 0.05f)); // 缩小到 100px。

            Assert.Greater(pinches, 0);
            Assert.Less(captured.Magnitude, 1f, "捏合时缩放比例应 < 1");
        }

        [Test]
        public void Drag_FiresWhileMoving_WithFrameDelta()
        {
            var r = new GestureRecognizer();
            int drags = 0;
            GestureEvent last = default;
            r.OnDrag += e => { drags++; last = e; };

            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 130f, 100f, TouchPhaseKind.Moved, 0.05f)); // 超过拖拽阈值 8px。
            r.Feed(Sample(0, 160f, 100f, TouchPhaseKind.Moved, 0.1f));

            Assert.GreaterOrEqual(drags, 2, "移动过程中应多次触发拖拽");
            Assert.AreEqual(GestureKind.Drag, last.Kind);
            Assert.AreEqual(30f, last.DeltaX, Delta, "最后一帧横向位移应为 30");
        }

        [Test]
        public void Drag_TinyMovementBelowThreshold_DoesNotFire()
        {
            var r = new GestureRecognizer();
            int drags = 0;
            r.OnDrag += _ => drags++;

            r.Feed(Sample(0, 100f, 100f, TouchPhaseKind.Began, 0f));
            r.Feed(Sample(0, 103f, 101f, TouchPhaseKind.Moved, 0.05f)); // < 8px。

            Assert.AreEqual(0, drags);
        }

        [Test]
        public void Reset_ClearsState_NoLongPressAfterReset()
        {
            var r = new GestureRecognizer();
            int longPresses = 0;
            r.OnLongPress += _ => longPresses++;

            r.Feed(Sample(0, 50f, 50f, TouchPhaseKind.Began, 0f));
            r.Reset();
            r.Update(1.0f); // 已重置，不应触发长按。

            Assert.AreEqual(0, longPresses);
        }

        [Test]
        public void Parameterless_DefaultsToDefaultConfig()
        {
            var r = new GestureRecognizer();

            Assert.AreEqual(GestureConfig.Default.LongPressDuration, r.Config.LongPressDuration, Delta);
            Assert.AreEqual(GestureConfig.Default.TapMaxDuration, r.Config.TapMaxDuration, Delta);
            Assert.AreEqual(GestureConfig.Default.SwipeMinDistance, r.Config.SwipeMinDistance, Delta);
        }

        [Test]
        public void SetConfig_OverridesThreshold_LongPressFiresAtInjectedDuration()
        {
            var r = new GestureRecognizer();
            r.SetConfig(new GestureConfig
            {
                TapMaxDuration = 0.3f,
                TapMaxMovement = 20f,
                DoubleTapMaxGap = 0.3f,
                LongPressDuration = 0.2f, // 缩短长按阈值至 0.2s。
                SwipeMinDistance = 60f
            });
            int longPresses = 0;
            r.OnLongPress += _ => longPresses++;

            r.Feed(Sample(0, 50f, 60f, TouchPhaseKind.Began, 0f));
            r.Update(0.25f); // 0.25 > 注入阈值 0.2。

            Assert.AreEqual(0.2f, r.Config.LongPressDuration, Delta);
            Assert.AreEqual(1, longPresses);
        }

        [Test]
        public void FrameworkUpdate_AccumulatesTime_DrivesLongPress()
        {
            var r = new GestureRecognizer();
            int longPresses = 0;
            r.OnLongPress += _ => longPresses++;

            r.Feed(Sample(0, 50f, 60f, TouchPhaseKind.Began, 0f));
            // 以相对帧时长经框架 Update 累计驱动：0.3 + 0.4 = 0.7 > LongPressDuration 0.6。
            r.Update(0.3f, 0.3f);
            Assert.AreEqual(0, longPresses, "累计 0.3s 未到阈值不应触发");
            r.Update(0.4f, 0.4f);

            Assert.AreEqual(1, longPresses);
        }
    }
}
