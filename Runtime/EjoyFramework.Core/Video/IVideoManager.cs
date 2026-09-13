//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Video
{
    /// <summary>
    /// 视频播放管理器接口。
    /// 负责：注入底层播放辅助器（<see cref="IVideoPlayerHelper"/>）；统一播放 / 暂停 / 恢复 / 停止 / 跳过控制；
    /// 把辅助器事件转译为自身事件，并触发单次播放注册的 onCompleted / onError 回调。
    ///
    /// 业务层一般通过 Unity 层的 VideoComponent 间接使用本接口。
    /// </summary>
    public interface IVideoManager
    {
        /// <summary>注入底层视频播放辅助器（通常是 Unity 层的 UnityVideoPlayerHelper）。</summary>
        void SetHelper(IVideoPlayerHelper helper);

        /// <summary>当前是否正在播放视频。</summary>
        bool IsPlaying { get; }

        /// <summary>
        /// 是否允许跳过当前视频。仅当为 true 时 <see cref="Skip"/> 才会生效（停止播放并按"已完成"处理）。
        /// 常用于过场 CG：在某些剧情节点禁止跳过。
        /// </summary>
        bool Skippable { get; set; }

        /// <summary>
        /// 播放一段视频。<paramref name="source"/> 为 clip 名或 URL（含 "://" 视为 URL）。
        /// <paramref name="loop"/> 为 true 时循环（此时不会触发完成事件 / <paramref name="onCompleted"/>）。
        /// <paramref name="onCompleted"/> / <paramref name="onError"/> 为本次播放的一次性回调，
        /// 在完成 / 出错后触发并随即清空；未设置辅助器时以错误方式回调 <paramref name="onError"/> 并触发 <see cref="OnVideoError"/>。
        /// </summary>
        void Play(string source, bool loop = false, Action onCompleted = null, Action<string> onError = null);

        /// <summary>停止当前播放（不触发完成事件，也不触发单次 onCompleted）。</summary>
        void Stop();

        /// <summary>暂停当前播放。</summary>
        void Pause();

        /// <summary>恢复当前播放。</summary>
        void Resume();

        /// <summary>
        /// 跳过当前视频：仅当 <see cref="Skippable"/> 为 true 且正在播放时生效，
        /// 此时停止播放并按"已完成"处理（触发 <see cref="OnVideoCompleted"/> 及单次 onCompleted）。返回是否真正执行了跳过。
        /// </summary>
        bool Skip();

        /// <summary>视频开始播放时触发，第二参数为 source。</summary>
        event Action<IVideoManager, string> OnVideoStarted;

        /// <summary>视频播放完成（含被 <see cref="Skip"/> 跳过）时触发，第二参数为 source。</summary>
        event Action<IVideoManager, string> OnVideoCompleted;

        /// <summary>视频播放出错时触发：(manager, source, message)。</summary>
        event Action<IVideoManager, string, string> OnVideoError;
    }
}
