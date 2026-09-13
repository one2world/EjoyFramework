//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Music
{
    /// <summary>
    /// 临时压低控制器接口（ducking，纯逻辑，引擎无关）。
    /// 用于在对白 / 重要音效期间临时降低 BGM 音量。支持多个压低请求叠加。
    /// <para>
    /// 语义：维护一组活动的压低请求，每个请求带一个目标倍率（target multiplier，[0,1]，1 表示不压低）。
    /// 生效目标 = 所有活动请求中“最强”（最低）的目标倍率；若无活动请求则为 1。
    /// </para>
    /// <para>
    /// <see cref="CurrentMultiplier"/> 在每帧推进中朝目标平滑移动：
    /// 朝更低（压低）移动时使用对应请求的 attack 时间，朝更高（恢复）移动时使用 release 时间。
    /// attack/release 表示“从满量程 1.0 走完所需的秒数”，按此速率线性逼近目标。
    /// </para>
    /// 单线程使用，非线程安全。
    /// </summary>
    public interface IDuckController
    {
        /// <summary>
        /// 当前生效的压低倍率，范围 [0,1]，1 表示不压低。
        /// </summary>
        float CurrentMultiplier { get; }

        /// <summary>
        /// 当前活动的压低请求数量。
        /// </summary>
        int ActiveDuckCount { get; }

        /// <summary>
        /// 压入一个压低请求：目标倍率 targetMultiplier，在 attackSeconds 内达到。
        /// </summary>
        /// <param name="targetMultiplier">目标倍率，自动钳制到 [0,1]。</param>
        /// <param name="attackSeconds">达到目标所需时间（秒），负值视为 0（瞬时）。</param>
        /// <returns>句柄 id，用于后续 <see cref="PopDuck"/>。</returns>
        int PushDuck(float targetMultiplier, float attackSeconds);

        /// <summary>
        /// 弹出指定句柄的压低请求，并朝剩余请求中“最强”的目标（或 1）恢复，历时 releaseSeconds。
        /// </summary>
        /// <param name="handle">由 <see cref="PushDuck"/> 返回的句柄。无效句柄为空操作。</param>
        /// <param name="releaseSeconds">恢复时间（秒），负值视为 0（瞬时）。</param>
        void PopDuck(int handle, float releaseSeconds);
    }
}
