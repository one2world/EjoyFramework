//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Netcode.Transport;
using UnityEngine;

namespace EjoyFramework.GamePlay.Netcode
{
    /// <summary>
    /// 网络传输的 Unity 驱动组件。持有一个 <see cref="INetTransport"/>（可由外部赋值），
    /// 在 <see cref="Update"/> 中调用其 <c>Poll()</c>，使所有网络事件在游戏主线程上触发；
    /// 在 <see cref="OnDestroy"/> 中调用其 <c>Shutdown()</c> 关闭套接字与线程。
    /// <para>本组件只负责“泵”传输层，不关心具体传输实现（Loopback / TCP / 未来的 UDP / KCP）。</para>
    /// </summary>
    public sealed class NetcodeRunner : MonoBehaviour
    {
        private INetTransport m_Transport;

        /// <summary>
        /// 当前驱动的传输实例。赋值后即在每帧 <see cref="Update"/> 中被泵送。
        /// </summary>
        public INetTransport Transport
        {
            get { return m_Transport; }
            set { m_Transport = value; }
        }

        /// <summary>
        /// 设置要驱动的传输实例（等价于赋值 <see cref="Transport"/>）。
        /// </summary>
        /// <param name="transport">传输实例，可为 null 以解除驱动。</param>
        public void SetTransport(INetTransport transport)
        {
            m_Transport = transport;
        }

        // 每帧在主线程上排空网络事件队列。
        private void Update()
        {
            m_Transport?.Poll();
        }

        // 组件销毁时关闭传输，确保套接字与后台线程被清理。
        private void OnDestroy()
        {
            m_Transport?.Shutdown();
            m_Transport = null;
        }
    }
}
