//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Streaming;

namespace EjoyFramework.Core.Navigation
{
    internal sealed class NavigationManager : FrameworkModule, INavigationManager
    {
        private INavigationHelper m_Helper;
        private readonly Dictionary<int, object> m_Agents = new Dictionary<int, object>();
        private int m_NextAgentId;

        public override int Priority { get { return 0; } }

        // 必需配置自检：依赖 NavigationHelper 计算路径/管理 Agent。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Helper != null; } }
        public override string ConfigurationHint { get { return "Call SetHelper(...) before use."; } }

        public override void Update(float a, float b) { }
        public override void Shutdown()
        {
            if (m_Helper != null)
            {
                foreach (var h in m_Agents.Values)
                {
                    try { m_Helper.DestroyAgent(h); }
                    catch (Exception ex) { FrameworkLog.Error("NavigationManager.Shutdown DestroyAgent threw: {0}", ex); }
                }
            }
            m_Agents.Clear();
            m_Helper = null;
        }

        public event Action<NavPathResult> PathCalculated;
        public int AgentCount => m_Agents.Count;

        public void SetHelper(INavigationHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetHelper));
            if (helper == null) throw new FrameworkException("Navigation helper is invalid.");
            m_Helper = helper;
        }

        public void CalculatePathAsync(Vector3Lite from, Vector3Lite to, int areaMask, Action<NavPathResult> onComplete)
        {
            Framework.EnsureMainThread(nameof(CalculatePathAsync));
            EnsureHelper();
            m_Helper.CalculatePath(from, to, areaMask, result =>
            {
                try { onComplete?.Invoke(result); }
                catch (Exception ex) { FrameworkLog.Error("CalculatePath onComplete threw: {0}", ex); }
                try { PathCalculated?.Invoke(result); }
                catch (Exception ex) { FrameworkLog.Error("PathCalculated event threw: {0}", ex); }
            });
        }

        public int RegisterAgent(NavAgentConfig config)
        {
            Framework.EnsureMainThread(nameof(RegisterAgent));
            EnsureHelper();
            if (config == null) throw new FrameworkException("config is null.");
            object helperHandle;
            try { helperHandle = m_Helper.CreateAgent(config); }
            catch (Exception ex) { throw new FrameworkException("CreateAgent threw: " + ex.Message, ex); }
            int id = ++m_NextAgentId;
            m_Agents[id] = helperHandle;
            return id;
        }

        public bool UnregisterAgent(int agentId)
        {
            Framework.EnsureMainThread(nameof(UnregisterAgent));
            if (!m_Agents.TryGetValue(agentId, out var h)) return false;
            try { m_Helper.DestroyAgent(h); }
            catch (Exception ex) { FrameworkLog.Error("DestroyAgent threw: {0}", ex); }
            m_Agents.Remove(agentId);
            return true;
        }

        public void SetDestination(int agentId, Vector3Lite destination)
        {
            Framework.EnsureMainThread(nameof(SetDestination));
            if (!m_Agents.TryGetValue(agentId, out var h)) throw new FrameworkException("Unknown agentId: " + agentId);
            m_Helper.SetAgentDestination(h, destination);
        }

        public void Warp(int agentId, Vector3Lite position)
        {
            Framework.EnsureMainThread(nameof(Warp));
            if (!m_Agents.TryGetValue(agentId, out var h)) throw new FrameworkException("Unknown agentId: " + agentId);
            m_Helper.WarpAgent(h, position);
        }

        public Vector3Lite GetAgentPosition(int agentId)
        {
            if (!m_Agents.TryGetValue(agentId, out var h)) return default;
            return m_Helper.GetAgentPosition(h);
        }

        public bool HasReachedDestination(int agentId)
        {
            if (!m_Agents.TryGetValue(agentId, out var h)) return false;
            return m_Helper.HasReachedDestination(h);
        }

        public void StopAgent(int agentId)
        {
            Framework.EnsureMainThread(nameof(StopAgent));
            if (!m_Agents.TryGetValue(agentId, out var h)) return;
            m_Helper.StopAgent(h);
        }

        private void EnsureHelper()
        {
            if (m_Helper == null) throw new FrameworkException("NavigationManager: helper not set. Call SetHelper first.");
        }
    }
}
