//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.AntiCheat
{
    /// <summary>
    /// 篡改探测注册表模块接口：周期性地校验一组混淆值是否被外部篡改。
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
    public interface ITamperDetector
    {
        /// <summary>
        /// 当 <see cref="CheckIntegrity"/> 期间任一探针报告篡改时触发。
        /// 注意：单次 <see cref="CheckIntegrity"/> 调用最多触发一次该事件。
        /// </summary>
        event Action OnTamperDetected;

        /// <summary>
        /// 当前已注册的探针数量。
        /// </summary>
        int WatchCount { get; }

        /// <summary>
        /// 注册一个篡改探针。
        /// </summary>
        /// <param name="isTamperedProbe">返回 true 表示被监视的值已被篡改的委托。</param>
        /// <exception cref="ArgumentNullException">探针为 null 时抛出。</exception>
        void Watch(Func<bool> isTamperedProbe);

        /// <summary>
        /// 检查所有探针。只要任意探针报告篡改即返回 true，并触发一次 <see cref="OnTamperDetected"/>。
        /// 会遍历全部探针（不短路），以便每个探针都有机会执行其副作用。
        /// </summary>
        /// <returns>任一探针报告篡改则为 true。</returns>
        bool CheckIntegrity();

        /// <summary>
        /// 清空所有已注册的探针。不影响已订阅的 <see cref="OnTamperDetected"/>。
        /// </summary>
        void Clear();
    }
}
