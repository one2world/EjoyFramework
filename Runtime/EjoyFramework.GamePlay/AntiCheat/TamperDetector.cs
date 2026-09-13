//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.AntiCheat
{
    /// <summary>
    /// 篡改探测注册表：周期性地校验一组混淆值是否被外部篡改。
    /// 调用方注册若干“探针”（返回 bool 的委托，例如 <c>() =&gt; myObscuredInt.IsTampered</c>），
    /// 然后通过 <see cref="CheckIntegrity"/> 一次性检查所有探针。
    /// </summary>
    /// <remarks>
    /// 典型用法：
    /// <code>
    /// var detector = Framework.GetModule&lt;ITamperDetector&gt;();
    /// detector.Watch(() =&gt; playerGold.IsTampered);
    /// detector.OnTamperDetected += () =&gt; HandleCheat();
    /// // 在某个低频心跳里：
    /// if (detector.CheckIntegrity()) { /* 已检测到篡改 */ }
    /// </code>
    /// </remarks>
    public sealed class TamperDetector : FrameworkModule, ITamperDetector
    {
        /// <summary>已注册的探针集合。每个探针返回 true 表示其监视的值被篡改。</summary>
        private readonly List<Func<bool>> m_Probes = new List<Func<bool>>();

        /// <summary>
        /// 当 <see cref="CheckIntegrity"/> 期间任一探针报告篡改时触发。
        /// 注意：单次 <see cref="CheckIntegrity"/> 调用最多触发一次该事件。
        /// </summary>
        public event Action OnTamperDetected;

        /// <summary>
        /// 当前已注册的探针数量。
        /// </summary>
        public int WatchCount
        {
            get { return m_Probes.Count; }
        }

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        /// <remarks>
        /// 篡改探测由调用方在低频心跳里显式调用 <see cref="CheckIntegrity"/> 驱动，
        /// 模块本身无逐帧工作，故此处为空实现。
        /// </remarks>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理模块运行时状态：清空所有已注册探针。
        /// </summary>
        public override void Shutdown()
        {
            m_Probes.Clear();
        }

        /// <summary>
        /// 注册一个篡改探针。
        /// </summary>
        /// <param name="isTamperedProbe">返回 true 表示被监视的值已被篡改的委托。</param>
        /// <exception cref="ArgumentNullException">探针为 null 时抛出。</exception>
        public void Watch(Func<bool> isTamperedProbe)
        {
            if (isTamperedProbe == null)
            {
                throw new ArgumentNullException(nameof(isTamperedProbe));
            }

            m_Probes.Add(isTamperedProbe);
        }

        /// <summary>
        /// 检查所有探针。只要任意探针报告篡改即返回 true，并触发一次 <see cref="OnTamperDetected"/>。
        /// 会遍历全部探针（不短路），以便每个探针都有机会执行其副作用。
        /// </summary>
        /// <returns>任一探针报告篡改则为 true。</returns>
        public bool CheckIntegrity()
        {
            bool tampered = false;

            // 在遍历前对探针列表快照，避免探针回调在遍历过程中修改集合导致异常。
            Func<bool>[] snapshot = m_Probes.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i]())
                {
                    tampered = true;
                }
            }

            if (tampered)
            {
                OnTamperDetected?.Invoke();
            }

            return tampered;
        }

        /// <summary>
        /// 清空所有已注册的探针。不影响已订阅的 <see cref="OnTamperDetected"/>。
        /// </summary>
        public void Clear()
        {
            m_Probes.Clear();
        }
    }
}
