//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Music
{
    /// <summary>
    /// 双通道交叉淡入淡出混音器接口（纯逻辑，引擎无关）。
    /// 维护两个逻辑通道 A、B：其中一个为“前台/活动”通道，另一个为“后台/进入”通道。
    /// <para>
    /// 交叉淡变：活动通道增益从 1 线性降到 0，进入通道增益从 0 线性升到 1，历时 duration 秒；
    /// 完成后交换活动通道并触发 <see cref="OnCrossfadeComplete"/>。
    /// </para>
    /// <para>
    /// <see cref="VolumeA"/> / <see cref="VolumeB"/> 为各通道最终上报增益（已乘以 <see cref="MasterVolume"/> 并钳制到 [0,1]），
    /// 供 Unity 层直接写入 AudioSource.volume。本类不持有任何音频对象。
    /// </para>
    /// 单线程使用，非线程安全。
    /// </summary>
    public interface ICrossfadeMixer
    {
        /// <summary>
        /// 主音量，乘到两个通道的最终上报增益上。范围 [0,1]，写入时自动钳制。默认 1。
        /// </summary>
        float MasterVolume { get; set; }

        /// <summary>
        /// 通道 A 的最终上报增益（已乘 <see cref="MasterVolume"/> 并钳制到 [0,1]）。
        /// </summary>
        float VolumeA { get; }

        /// <summary>
        /// 通道 B 的最终上报增益（已乘 <see cref="MasterVolume"/> 并钳制到 [0,1]）。
        /// </summary>
        float VolumeB { get; }

        /// <summary>
        /// 是否正在进行淡变（交叉淡变 / 淡出 / 淡入）。
        /// </summary>
        bool IsCrossfading { get; }

        /// <summary>
        /// 当前活动（前台）通道：0 => A，1 => B。
        /// </summary>
        int ActiveChannel { get; }

        /// <summary>
        /// 交叉淡变完成时触发（仅 <see cref="StartCrossfade"/> 完成时触发，参数为本混音器自身）。
        /// </summary>
        event Action<CrossfadeMixer> OnCrossfadeComplete;

        /// <summary>
        /// 从当前活动通道交叉淡变到另一通道，历时 duration 秒。
        /// <para>活动通道 1→0，进入通道 0→1；完成后两者角色互换（进入通道成为新的活动通道）。</para>
        /// <para>duration &lt;= 0 视为瞬时切换：立即完成交换并触发完成事件。</para>
        /// </summary>
        /// <param name="duration">淡变时长（秒）。</param>
        void StartCrossfade(float duration);

        /// <summary>
        /// 将活动通道增益淡出到 0（用于停止音乐），历时 duration 秒。不交换活动通道。
        /// <para>duration &lt;= 0 视为瞬时。</para>
        /// </summary>
        /// <param name="duration">淡出时长（秒）。</param>
        void FadeOut(float duration);

        /// <summary>
        /// 将活动通道增益从当前值淡入到 1，历时 duration 秒。不交换活动通道。
        /// <para>duration &lt;= 0 视为瞬时。</para>
        /// </summary>
        /// <param name="duration">淡入时长（秒）。</param>
        void FadeInActive(float duration);
    }
}
