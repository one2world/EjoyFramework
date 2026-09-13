//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Netcode.Session
{
    /// <summary>
    /// 高层网络门面（<see cref="NetcodeServer"/> / <see cref="NetcodeClient"/>）所用三种线路消息的<b>稳定类型 id</b>。
    /// <para>
    /// 服务器与客户端必须用<b>完全相同</b>的 id 注册这三种消息，否则解帧会失败。
    /// 这些常量集中定义于此，保证两端注册一致、且在版本间保持不变。
    /// </para>
    /// </summary>
    public static class NetcodeMessageIds
    {
        /// <summary>
        /// <see cref="WelcomeMessage"/> 的稳定类型 id（服务器→客户端，加入握手）。
        /// </summary>
        public const ushort Welcome = 1;

        /// <summary>
        /// <see cref="InputMessage"/> 的稳定类型 id（客户端→服务器，输入指令）。
        /// </summary>
        public const ushort Input = 2;

        /// <summary>
        /// <see cref="SnapshotMessage"/> 的稳定类型 id（服务器→客户端，世界快照）。
        /// </summary>
        public const ushort Snapshot = 3;
    }
}
