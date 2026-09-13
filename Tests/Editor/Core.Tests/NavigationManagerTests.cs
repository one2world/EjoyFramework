//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Navigation;
using EjoyFramework.Core.Streaming;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class NavigationManagerTests
    {
        private NavigationManager m_NM;
        private MockNavHelper m_H;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_NM = new NavigationManager();
            m_H = new MockNavHelper();
            m_NM.SetHelper(m_H);
        }

        [Test]
        public void RegisterAgent_AssignsId_And_CountsAgents()
        {
            int a = m_NM.RegisterAgent(new NavAgentConfig { Name = "hero" });
            int b = m_NM.RegisterAgent(new NavAgentConfig { Name = "enemy" });
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(2, m_NM.AgentCount);
        }

        [Test]
        public void UnregisterAgent_DestroysHelperAgent()
        {
            int id = m_NM.RegisterAgent(new NavAgentConfig());
            Assert.IsTrue(m_NM.UnregisterAgent(id));
            Assert.AreEqual(0, m_NM.AgentCount);
            Assert.AreEqual(1, m_H.DestroyCalls);
        }

        [Test]
        public void SetDestination_ForwardsToHelper()
        {
            int id = m_NM.RegisterAgent(new NavAgentConfig());
            m_NM.SetDestination(id, new Vector3Lite(10, 0, 5));
            Assert.AreEqual(1, m_H.SetDestinationCalls);
        }

        [Test]
        public void SetDestination_UnknownAgent_Throws()
        {
            Assert.Throws<FrameworkException>(() => m_NM.SetDestination(999, default));
        }

        [Test]
        public void CalculatePathAsync_TriggersHelper_AndCallback()
        {
            NavPathResult got = null;
            int eventCount = 0;
            m_NM.PathCalculated += r => eventCount++;
            m_NM.CalculatePathAsync(default, new Vector3Lite(10, 0, 0), -1, r => got = r);
            Assert.IsNotNull(got);
            Assert.AreEqual(NavPathStatus.Success, got.Status);
            Assert.AreEqual(1, eventCount);
        }

        [Test]
        public void CalculatePath_Unreachable_ReportsStatus()
        {
            m_H.PathStatusOverride = NavPathStatus.Unreachable;
            NavPathResult got = null;
            m_NM.CalculatePathAsync(default, default, -1, r => got = r);
            Assert.AreEqual(NavPathStatus.Unreachable, got.Status);
        }

        [Test]
        public void WithoutHelper_Throws()
        {
            var bare = new NavigationManager();
            Assert.Throws<FrameworkException>(() => bare.RegisterAgent(new NavAgentConfig()));
            Assert.Throws<FrameworkException>(() => bare.CalculatePathAsync(default, default, -1, null));
        }

        [Test]
        public void Shutdown_DestroysAllAgents()
        {
            m_NM.RegisterAgent(new NavAgentConfig());
            m_NM.RegisterAgent(new NavAgentConfig());
            m_NM.RegisterAgent(new NavAgentConfig());
            m_NM.Shutdown();
            Assert.AreEqual(3, m_H.DestroyCalls);
            Assert.AreEqual(0, m_NM.AgentCount);
        }

        // ===== mock =====

        private sealed class MockNavHelper : INavigationHelper
        {
            public int SetDestinationCalls;
            public int DestroyCalls;
            public NavPathStatus PathStatusOverride = NavPathStatus.Success;
            private readonly Dictionary<object, Vector3Lite> m_Positions = new Dictionary<object, Vector3Lite>();

            public void CalculatePath(Vector3Lite from, Vector3Lite to, int areaMask, Action<NavPathResult> onComplete)
            {
                onComplete?.Invoke(new NavPathResult
                {
                    Status = PathStatusOverride,
                    Corners = new[] { from, to },
                    Length = Vector3Lite.Distance(from, to),
                });
            }
            public object CreateAgent(NavAgentConfig config) { var k = new object(); m_Positions[k] = config.InitialPosition; return k; }
            public void DestroyAgent(object h) { DestroyCalls++; m_Positions.Remove(h); }
            public void SetAgentDestination(object h, Vector3Lite dst) { SetDestinationCalls++; }
            public void WarpAgent(object h, Vector3Lite p) { m_Positions[h] = p; }
            public Vector3Lite GetAgentPosition(object h) { return m_Positions.TryGetValue(h, out var p) ? p : default; }
            public bool HasReachedDestination(object h) => true;
            public void StopAgent(object h) { }
        }
    }
}
