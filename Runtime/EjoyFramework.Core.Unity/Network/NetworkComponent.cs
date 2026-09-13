//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Network;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 网络组件。完全转发给 NetworkManager；提供 Unity 事件与频道访问辅助。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Network")]
    public sealed class NetworkComponent : GameFrameworkComponent
    {
        public event Action<INetworkChannel, object> NetworkConnected;
        public event Action<INetworkChannel> NetworkClosed;
        public event Action<INetworkChannel, int, string> NetworkError;
        public event Action<INetworkChannel, Packet> NetworkPacketReceived;
        public event Action<INetworkChannel, NetworkReconnectPhase, int, float> NetworkReconnect;

        private INetworkManager m_NetworkManager;

        protected override void Awake()
        {
            base.Awake();
            m_NetworkManager = Framework.GetModule<INetworkManager>();
            if (m_NetworkManager == null) { Log.Fatal("Network manager is invalid."); return; }
            m_NetworkManager.NetworkConnected += OnConnected;
            m_NetworkManager.NetworkClosed += OnClosed;
            m_NetworkManager.NetworkError += OnError;
            m_NetworkManager.NetworkPacketReceived += OnReceived;
            m_NetworkManager.NetworkReconnect += OnReconnect;
        }

        protected override void OnDestroy()
        {
            if (m_NetworkManager != null)
            {
                m_NetworkManager.NetworkConnected -= OnConnected;
                m_NetworkManager.NetworkClosed -= OnClosed;
                m_NetworkManager.NetworkError -= OnError;
                m_NetworkManager.NetworkPacketReceived -= OnReceived;
                m_NetworkManager.NetworkReconnect -= OnReconnect;
            }
            base.OnDestroy();
        }

        public int NetworkChannelCount { get { return m_NetworkManager != null ? m_NetworkManager.NetworkChannelCount : 0; } }

        public bool HasNetworkChannel(string name) { return m_NetworkManager != null && m_NetworkManager.HasNetworkChannel(name); }

        public INetworkChannel GetNetworkChannel(string name) { return m_NetworkManager != null ? m_NetworkManager.GetNetworkChannel(name) : null; }

        public INetworkChannel[] GetAllNetworkChannels() { return m_NetworkManager != null ? m_NetworkManager.GetAllNetworkChannels() : Array.Empty<INetworkChannel>(); }

        public INetworkChannel CreateNetworkChannel(string name, INetworkChannelHelper helper)
        {
            if (m_NetworkManager == null) { Log.Fatal("Network manager is invalid."); return null; }
            return m_NetworkManager.CreateNetworkChannel(name, helper);
        }

        public bool DestroyNetworkChannel(string name)
        {
            return m_NetworkManager != null && m_NetworkManager.DestroyNetworkChannel(name);
        }

        private void OnConnected(object s, NetworkConnectedEventArgs e)
        {
            try { NetworkConnected?.Invoke(e.NetworkChannel, e.UserData); }
            catch (Exception ex) { Log.Error("NetworkComponent.NetworkConnected listener threw: {0}", ex); }
        }
        private void OnClosed(object s, NetworkClosedEventArgs e)
        {
            try { NetworkClosed?.Invoke(e.NetworkChannel); }
            catch (Exception ex) { Log.Error("NetworkComponent.NetworkClosed listener threw: {0}", ex); }
        }
        private void OnError(object s, NetworkErrorEventArgs e)
        {
            try { NetworkError?.Invoke(e.NetworkChannel, e.ErrorCode, e.ErrorMessage); }
            catch (Exception ex) { Log.Error("NetworkComponent.NetworkError listener threw: {0}", ex); }
        }
        private void OnReceived(object s, NetworkPacketReceivedEventArgs e)
        {
            try { NetworkPacketReceived?.Invoke(e.NetworkChannel, e.Packet); }
            catch (Exception ex) { Log.Error("NetworkComponent.NetworkPacketReceived listener threw: {0}", ex); }
        }
        private void OnReconnect(object s, NetworkReconnectEventArgs e)
        {
            try { NetworkReconnect?.Invoke(e.NetworkChannel, e.Phase, e.Attempt, e.DelaySeconds); }
            catch (Exception ex) { Log.Error("NetworkComponent.NetworkReconnect listener threw: {0}", ex); }
        }
    }
}
