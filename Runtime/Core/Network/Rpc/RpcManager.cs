//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EjoyFramework.Core.Network.Rpc
{
    /// <summary>
    /// 简易 RPC：业务发出 request packet → 服务器响应 reply packet。
    ///
    /// 协议要求：reply packet 携带与 request 同一 Sequence。RpcManager 内部按 Sequence 找到 callback。
    ///
    /// 线程安全：所有公共方法对内部状态加锁；回调在持锁<b>外</b>调用，避免回调里再 Call 造成死锁/重入。
    /// 断线处理：业务应在 <c>NetworkClosed</c> 时调用 <see cref="FailAll"/>，使所有未决 RPC 走超时回调，
    /// 不让 await 永久挂起。
    ///
    /// 用法：
    /// <code>
    /// rpc.Call(channel, new LoginReq{...}, replyPacketId: 101, onReply: (LoginResp r) => ..., timeoutSeconds: 10);
    /// // 或 await：
    /// LoginResp resp = await rpc.CallAsync&lt;LoginResp&gt;(channel, new LoginReq{...}, replyPacketId: 101);
    /// </code>
    /// </summary>
    public sealed class RpcManager
    {
        private readonly object m_Lock = new object();
        private readonly Dictionary<int, PendingRpc> m_Pending = new Dictionary<int, PendingRpc>();
        private uint m_NextSequence;
        // 复用的超时收集缓冲（仅持锁内填充）。
        private readonly List<PendingRpc> m_TimeoutScratch = new List<PendingRpc>();

        /// <summary>每帧调一次，处理超时。回调在锁外触发。</summary>
        public void Update(float deltaSeconds)
        {
            lock (m_Lock)
            {
                if (m_Pending.Count == 0) return;
                m_TimeoutScratch.Clear();
                foreach (var kv in m_Pending)
                {
                    kv.Value.ElapsedSeconds += deltaSeconds;
                    if (kv.Value.ElapsedSeconds >= kv.Value.TimeoutSeconds)
                        m_TimeoutScratch.Add(kv.Value);
                }
                for (int i = 0; i < m_TimeoutScratch.Count; i++)
                    m_Pending.Remove(m_TimeoutScratch[i].Sequence);
            }

            // 锁外触发超时回调（可能再次 Call）。
            for (int i = 0; i < m_TimeoutScratch.Count; i++)
            {
                try { m_TimeoutScratch[i].OnTimeout?.Invoke(); }
                catch (Exception ex) { FrameworkLog.Error("Rpc timeout cb threw: {0}", ex); }
            }
            m_TimeoutScratch.Clear();
        }

        /// <summary>
        /// 发起 RPC。返回分配的 sequence。
        /// 若 <paramref name="channel"/>.Send 同步抛出，则先（线程安全地）移除刚登记的 pending 条目再向上抛，
        /// 避免留下只能等超时才清理、且无人 await 的孤儿条目。
        /// </summary>
        public int Call<TReply>(INetworkChannel channel, Packet request, int replyPacketId,
            Action<TReply> onReply,
            Action onTimeout = null,
            float timeoutSeconds = 10f) where TReply : Packet
        {
            if (channel == null) throw new FrameworkException("channel is null.");
            if (request == null) throw new FrameworkException("request is null.");

            var pending = new PendingRpc
            {
                ExpectedReplyPacketId = replyPacketId,
                ElapsedSeconds = 0f,
                TimeoutSeconds = timeoutSeconds,
                OnReply = reply => onReply?.Invoke((TReply)reply),
                OnTimeout = onTimeout,
            };

            int seq;
            lock (m_Lock)
            {
                seq = AllocSequenceLocked();
                pending.Sequence = seq;
                m_Pending[seq] = pending;
            }

            request.Sequence = seq;
            try
            {
                channel.Send(request);   // 锁外发送
            }
            catch
            {
                // Send 失败：移除孤儿条目后再抛。仅当本调用确实移除了该条目才向上抛——
                // 若期间已被 TryResolveReply/Update 超时/FailAll 抢先移除，则相应回调已（或将）了结该 RPC，不重复处理。
                lock (m_Lock)
                {
                    if (!m_Pending.Remove(seq)) return seq;
                }
                throw;
            }
            return seq;
        }

        /// <summary>
        /// await 风格 RPC。超时抛 <see cref="TimeoutException"/>，断线（FailAll）同样以超时异常结束等待。
        /// 若发送同步失败，将该异常 fault 到返回的 Task（不同步抛出），以保持 Task 化返回契约。
        /// </summary>
        public Task<TReply> CallAsync<TReply>(INetworkChannel channel, Packet request, int replyPacketId,
            float timeoutSeconds = 10f) where TReply : Packet
        {
            var tcs = new TaskCompletionSource<TReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Call<TReply>(channel, request, replyPacketId,
                    onReply: r => tcs.TrySetResult(r),
                    onTimeout: () => tcs.TrySetException(new TimeoutException(
                        Utility.Text.Format("RPC timed out after {0}s (replyId={1}).", timeoutSeconds, replyPacketId))),
                    timeoutSeconds: timeoutSeconds);
            }
            catch (Exception ex)
            {
                // 发送同步失败：Call 已移除孤儿条目，这里把异常 fault 到 Task，等待方据此结束 await。
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }

        /// <summary>
        /// NetworkChannel 接收到 packet 时尝试匹配 pending RPC。匹配则触发 callback 并返回 true（业务可跳过 PacketRegistry 派发）。
        /// </summary>
        public bool TryResolveReply(Packet reply)
        {
            if (reply == null) return false;
            PendingRpc p;
            lock (m_Lock)
            {
                if (!m_Pending.TryGetValue(reply.Sequence, out p)) return false;
                if (p.ExpectedReplyPacketId != 0 && p.ExpectedReplyPacketId != reply.Id) return false;
                m_Pending.Remove(reply.Sequence);
            }
            try { p.OnReply?.Invoke(reply); }
            catch (Exception ex) { FrameworkLog.Error("Rpc reply cb threw: {0}", ex); }
            return true;
        }

        public int PendingCount { get { lock (m_Lock) return m_Pending.Count; } }

        /// <summary>取消所有未决 RPC，并触发各自的超时回调（不让 await 永久挂起）。等价于 <see cref="FailAll"/>。</summary>
        public void CancelAll()
        {
            FailAll();
        }

        /// <summary>使所有未决 RPC 失败（走超时回调）。典型在 NetworkClosed 时调用。</summary>
        public void FailAll()
        {
            PendingRpc[] toFail;
            lock (m_Lock)
            {
                if (m_Pending.Count == 0) return;
                toFail = new PendingRpc[m_Pending.Count];
                m_Pending.Values.CopyTo(toFail, 0);
                m_Pending.Clear();
            }
            for (int i = 0; i < toFail.Length; i++)
            {
                try { toFail[i].OnTimeout?.Invoke(); }
                catch (Exception ex) { FrameworkLog.Error("Rpc fail cb threw: {0}", ex); }
            }
        }

        // 必须在 m_Lock 内调用。分配一个未占用且非 0 的 sequence；uint 自增天然回绕，碰撞时跳过。
        private int AllocSequenceLocked()
        {
            for (int guard = 0; guard < 1_000_000; guard++)
            {
                m_NextSequence++;
                int seq = unchecked((int)m_NextSequence);
                if (seq != 0 && !m_Pending.ContainsKey(seq)) return seq;
            }
            throw new FrameworkException("RpcManager: could not allocate a free sequence (too many pending).");
        }

        private sealed class PendingRpc
        {
            public int Sequence;
            public int ExpectedReplyPacketId;
            public float ElapsedSeconds;
            public float TimeoutSeconds;
            public Action<Packet> OnReply;
            public Action OnTimeout;
        }
    }
}
