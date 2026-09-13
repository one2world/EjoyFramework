//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Video
{
    /// <summary>
    /// 视频播放辅助器接口。由 Unity 层实现（封装 <c>UnityEngine.Video.VideoPlayer</c>），负责真正的解码与播放。
    ///
    /// 约定：
    ///   - 所有方法均在<b>主线程</b>调用；实现方无需自行做线程封送。
    ///   - <paramref name="source"/> 为一个 clip 名 / URL，由 Unity 实现自行解析
    ///     （含 "://" 视为远端 URL；否则视为本地 <c>VideoClip</c> 资源名）。
    ///   - 完成 / 准备就绪 / 出错均通过事件向上汇报；事件回调发生在主线程。
    ///   - 实现方负责释放底层播放器与渲染资源。
    /// </summary>
    public interface IVideoPlayerHelper
    {
        /// <summary>当前是否正在播放（已开始且未暂停、未停止）。</summary>
        bool IsPlaying { get; }

        /// <summary>当前是否处于暂停态（已开始但被 <see cref="Pause"/> 挂起）。</summary>
        bool IsPaused { get; }

        /// <summary>
        /// 开始播放。<paramref name="source"/> 为 clip 名或 URL（含 "://" 视为 URL）；
        /// <paramref name="loop"/> 为 true 时循环播放（此时 <see cref="Completed"/> 不再触发）。
        /// </summary>
        void Play(string source, bool loop);

        /// <summary>停止播放并释放当前内容；停止后 <see cref="IsPlaying"/> / <see cref="IsPaused"/> 均为 false。</summary>
        void Stop();

        /// <summary>暂停播放（保留进度，可由 <see cref="Resume"/> 恢复）。</summary>
        void Pause();

        /// <summary>从暂停点恢复播放。</summary>
        void Resume();

        /// <summary>
        /// 每帧轮询（由 <see cref="IVideoManager"/> 转发）。
        /// 多数实现可留空（VideoPlayer 自驱），保留此入口以便实现方按需做进度 / 超时检查。
        /// </summary>
        void Update(float elapseSeconds, float realElapseSeconds);

        /// <summary>底层内容准备就绪（可开始播放）时触发。</summary>
        event Action<IVideoPlayerHelper> Prepared;

        /// <summary>非循环播放正常结束（到达结尾）时触发。</summary>
        event Action<IVideoPlayerHelper> Completed;

        /// <summary>底层发生错误时触发，第二参数为错误信息。</summary>
        event Action<IVideoPlayerHelper, string> ErrorOccurred;
    }
}
