// Copyright (c) Ejoy. All rights reserved.

using System;
using EjoyFramework.Core.UI;
using NUnit.Framework;

namespace EjoyFramework.Core.Tests
{
    public sealed class ViewportEdgeLayoutTests
    {
        [Test]
        public void GetInset_UsesAsymmetricCutoutTopologyWhenAvailable()
        {
            var fullRect = new ViewportRect(0, 0, 1100, 508);
            var safeRect = new ViewportRect(80, 0, 940, 508);
            var leftCutout = new ViewportRect(0, 100, 80, 308);
            var empty = default(ViewportRect);
            var occlusions = new ViewportOcclusions(
                1,
                false,
                in leftCutout,
                in empty,
                in empty,
                in empty);
            var metrics = new ViewportMetrics(
                in fullRect,
                in safeRect,
                in occlusions,
                ViewportOrientation.LandscapeLeft);

            Assert.That(
                ViewportEdgeLayout.GetInset(in metrics, ViewportEdge.Left, ViewportInsetSource.Occlusion),
                Is.EqualTo(80));
            Assert.That(
                ViewportEdgeLayout.GetInset(in metrics, ViewportEdge.Right, ViewportInsetSource.Occlusion),
                Is.Zero);
        }

        [Test]
        public void GetInset_UsesSafeAreaWhenExplicitlySelected()
        {
            var insets = new ViewportInsets(80, 0, 80, 0);
            var metrics = new ViewportMetrics(
                1100,
                508,
                in insets,
                ViewportOrientation.LandscapeLeft);

            Assert.That(
                ViewportEdgeLayout.GetInset(in metrics, ViewportEdge.Left, ViewportInsetSource.SafeArea),
                Is.EqualTo(80));
            Assert.That(
                ViewportEdgeLayout.GetInset(in metrics, ViewportEdge.Right, ViewportInsetSource.SafeArea),
                Is.EqualTo(80));
        }

        [Test]
        public void GetInset_RejectsTruncatedOcclusionTopology()
        {
            var fullRect = new ViewportRect(0, 0, 1100, 508);
            var safeRect = new ViewportRect(80, 0, 940, 508);
            var empty = default(ViewportRect);
            var occlusions = new ViewportOcclusions(0, true, in empty, in empty, in empty, in empty);
            var metrics = new ViewportMetrics(
                in fullRect,
                in safeRect,
                in occlusions,
                ViewportOrientation.LandscapeLeft);

            Assert.Throws<InvalidOperationException>(() =>
                ViewportEdgeLayout.GetInset(in metrics, ViewportEdge.Left, ViewportInsetSource.Occlusion));
        }
    }
}
