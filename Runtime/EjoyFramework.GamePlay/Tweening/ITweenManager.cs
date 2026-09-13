//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 缓动管理器接口。持有活跃 Tween 列表，逐帧推进并移除已完成 / 已杀死的项。
    /// 通过 <c>Framework.GetModule&lt;ITweenManager&gt;()</c> 访问。
    /// 单线程使用，非线程安全。
    /// </summary>
    public interface ITweenManager
    {
        /// <summary>
        /// 当前活跃（含待加入）的 Tween 数量。
        /// </summary>
        int ActiveCount { get; }

        /// <summary>
        /// 加入一个 Tween 进行驱动。重复加入同一实例会被忽略。
        /// 若在推进过程中调用，会暂存到待加入缓冲，本帧结束后并入。
        /// </summary>
        /// <param name="tween">要加入的 Tween，为空则忽略。</param>
        void Add(Tween tween);

        /// <summary>
        /// 推进所有活跃 Tween 一帧，并移除已完成 / 已杀死的项。
        /// </summary>
        /// <param name="deltaTime">本帧时间增量（秒）。</param>
        void Tick(float deltaTime);

        /// <summary>
        /// 杀死并移除所有活跃 Tween。
        /// </summary>
        /// <param name="complete">为 true 时每个 Tween 都快照到终点并触发 OnComplete。</param>
        void KillAll(bool complete = false);
    }
}
