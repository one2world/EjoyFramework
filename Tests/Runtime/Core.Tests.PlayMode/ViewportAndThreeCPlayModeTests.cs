using System.Collections;
using EjoyFramework.Core;
using EjoyFramework.Core.UI;
using EjoyFramework.Core.Unity;
using EjoyFramework.GamePlay.ThreeC;
using EjoyFramework.GamePlay.Unity.ThreeC;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EjoyFramework.Tests.PlayMode
{
    public sealed class ViewportAndThreeCPlayModeTests
    {
        [Test]
        public void ViewportRegion_AppliesSyntheticAsymmetricSafeArea()
        {
            var root = new GameObject("ViewportTestRoot", typeof(RectTransform), typeof(Canvas));
            var child = new GameObject("SafeContent", typeof(RectTransform));
            RectTransform childRect = child.GetComponent<RectTransform>();
            childRect.SetParent(root.transform, false);
            UIViewportRegion region = child.AddComponent<UIViewportRegion>();
            var insets = new ViewportInsets(100, 50, 200, 25);
            var metrics = new ViewportMetrics(1000, 500, in insets, ViewportOrientation.LandscapeLeft);
            IViewportManager manager = Framework.GetModule<IViewportManager>();

            manager.SetHelper(new FixedViewportHelper(in metrics));
            region.ApplyImmediately();

            Assert.That(childRect.anchorMin.x, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(childRect.anchorMin.y, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(childRect.anchorMax.x, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(childRect.anchorMax.y, Is.EqualTo(0.95f).Within(0.0001f));

            Object.DestroyImmediate(root);
        }

        [UnityTest]
        public IEnumerator ThreeCUnityComponents_RunTogetherWithoutSceneSpecificBootstrap()
        {
            var player = new GameObject("ThreeCTestPlayer");
            player.AddComponent<CharacterController>();
            ThirdPersonInputSource input = player.AddComponent<ThirdPersonInputSource>();
            ThirdPersonCharacterMotor motor = player.AddComponent<ThirdPersonCharacterMotor>();

            var cameraObject = new GameObject("ThreeCTestCamera", typeof(Camera));
            ThirdPersonCameraRig cameraRig = cameraObject.AddComponent<ThirdPersonCameraRig>();
            cameraRig.Bind(player.transform, input);

            yield return null;
            yield return null;

            Assert.That(motor.isActiveAndEnabled, Is.True);
            Assert.That(cameraRig.isActiveAndEnabled, Is.True);
            Assert.That(ThreeCPerformanceCounters.Motor.Snapshot().SampleCount, Is.GreaterThan(0));
            Assert.That(ThreeCPerformanceCounters.Camera.Snapshot().SampleCount, Is.GreaterThan(0));

            Object.Destroy(player);
            Object.Destroy(cameraObject);
            yield return null;
        }

        private sealed class FixedViewportHelper : IViewportHelper
        {
            private readonly ViewportMetrics m_Metrics;
            private bool m_HasValue = true;

            public FixedViewportHelper(in ViewportMetrics metrics)
            {
                m_Metrics = metrics;
            }

            public bool TryGetMetrics(out ViewportMetrics metrics)
            {
                metrics = m_Metrics;
                if (!m_HasValue) return false;
                m_HasValue = false;
                return true;
            }
        }
    }
}
