using EjoyFramework.Core.UI;
using NUnit.Framework;

namespace EjoyFramework.Tests
{
    public sealed class ViewportMetricsTests
    {
        [Test]
        public void Constructor_ClampsSafeRectToViewport()
        {
            var full = new ViewportRect(0, 0, 100, 50);
            var safe = new ViewportRect(90, 10, 90, 100);
            var metrics = new ViewportMetrics(
                in full, in safe, in UnsafeEmptyOcclusions, ViewportOrientation.LandscapeLeft);

            Assert.That(metrics.SafeRect.XMin, Is.EqualTo(90));
            Assert.That(metrics.SafeRect.XMax, Is.EqualTo(100));
            Assert.That(metrics.SafeRect.YMax, Is.EqualTo(50));
        }

        [Test]
        public void Occlusions_PreserveAsymmetricCutoutGeometry()
        {
            var leftCutout = new ViewportRect(0, 120, 64, 180);
            var empty = default(ViewportRect);
            var occlusions = new ViewportOcclusions(
                1, false, in leftCutout, in empty, in empty, in empty);
            ViewportRect stored = occlusions.Get(0);

            Assert.That(stored.XMax, Is.EqualTo(64));
            Assert.That(stored.YMin, Is.EqualTo(120));
            Assert.That(occlusions.IsTruncated, Is.False);
        }

        [Test]
        public void Layout_ProducesNormalizedSafeRect()
        {
            var insets = new ViewportInsets(10, 5, 20, 15);
            var metrics = new ViewportMetrics(100, 50, in insets, ViewportOrientation.LandscapeLeft);
            NormalizedViewportRect rect = ViewportLayout.GetSafeRect(in metrics);

            Assert.That(rect.XMin, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(rect.XMax, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(rect.YMin, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(rect.YMax, Is.EqualTo(0.7f).Within(0.0001f));
        }

        private static readonly ViewportOcclusions UnsafeEmptyOcclusions = default;
    }
}
