//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Video;
using UnityEngine;
using UnityEngine.Video;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 基于 <see cref="VideoPlayer"/> 的 <see cref="IVideoPlayerHelper"/> 实现。
    ///
    /// 渲染：默认渲染到一个 <see cref="RenderTexture"/>（<see cref="TargetTexture"/>），业务侧把它绑到 RawImage 即可显示。
    /// 也可在外部把 <see cref="VideoPlayer.renderMode"/> 改为相机近/远平面等其它模式。
    ///
    /// 事件映射：
    ///   <see cref="VideoPlayer.prepareCompleted"/> → <see cref="Prepared"/>（准备就绪后自动开始播放）；
    ///   <see cref="VideoPlayer.loopPointReached"/> → <see cref="Completed"/>（仅非循环时透出，循环态吞掉）；
    ///   <see cref="VideoPlayer.errorReceived"/> → <see cref="ErrorOccurred"/>。
    ///
    /// 命名空间提示：本文件位于 EjoyFramework.Core.Unity，与 core 的 EjoyFramework.Core.Video 平级；
    /// UnityEngine.Video.VideoPlayer 等类型通过显式 using 引入，无遮蔽风险。
    /// </summary>
    public sealed class UnityVideoPlayerHelper : IVideoPlayerHelper
    {
        // 默认渲染纹理尺寸（仅在自建 RenderTexture 时使用）。
        private const int DefaultWidth = 1280;
        private const int DefaultHeight = 720;

        private readonly VideoPlayer m_Player;
        private readonly RenderTexture m_RenderTexture;
        private readonly bool m_OwnsRenderTexture;

        private bool m_Loop;
        private bool m_Started;   // 已发起一次 Prepare/Play 且尚未 Stop。
        private bool m_Paused;

        /// <summary>
        /// 用一个已有的 <see cref="VideoPlayer"/> 构造；渲染到自建的默认 <see cref="RenderTexture"/>。
        /// </summary>
        public UnityVideoPlayerHelper(VideoPlayer player)
            : this(player, null)
        {
        }

        /// <summary>
        /// 用一个已有的 <see cref="VideoPlayer"/> 与（可选）外部 <see cref="RenderTexture"/> 构造。
        /// <paramref name="renderTexture"/> 为 null 时自建一个默认尺寸的纹理并在 <see cref="Dispose"/> 时释放。
        /// </summary>
        public UnityVideoPlayerHelper(VideoPlayer player, RenderTexture renderTexture)
        {
            m_Player = player != null ? player : throw new ArgumentNullException(nameof(player));

            if (renderTexture != null)
            {
                m_RenderTexture = renderTexture;
                m_OwnsRenderTexture = false;
            }
            else
            {
                m_RenderTexture = new RenderTexture(DefaultWidth, DefaultHeight, 0);
                m_OwnsRenderTexture = true;
            }

            // 由 Helper 控制何时开始：关闭 playOnAwake，统一走 Prepare → Play。
            m_Player.playOnAwake = false;
            m_Player.waitForFirstFrame = true;
            m_Player.renderMode = VideoRenderMode.RenderTexture;
            m_Player.targetTexture = m_RenderTexture;

            m_Player.prepareCompleted += OnPrepareCompleted;
            m_Player.loopPointReached += OnLoopPointReached;
            m_Player.errorReceived += OnErrorReceived;
        }

        /// <summary>当前渲染目标纹理（绑定到 RawImage 用于显示）。</summary>
        public RenderTexture TargetTexture
        {
            get { return m_RenderTexture; }
        }

        public bool IsPlaying
        {
            get { return m_Started && !m_Paused && m_Player != null && m_Player.isPlaying; }
        }

        public bool IsPaused
        {
            get { return m_Started && m_Paused; }
        }

        public void Play(string source, bool loop)
        {
            if (string.IsNullOrEmpty(source))
            {
                RaiseError("Video source is null or empty.");
                return;
            }

            m_Loop = loop;
            m_Paused = false;
            m_Started = true;
            m_Player.isLooping = loop;

            // 解析 source：含 "://" 视为远端 / 本地文件 URL；否则视为 Resources 下的 VideoClip 资源名。
            if (source.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                m_Player.source = VideoSource.Url;
                m_Player.clip = null;
                m_Player.url = source;
            }
            else
            {
                // TODO: 这里用 Resources.Load 作为合理默认；接入项目后可改为走框架 IResourceManager 异步加载，
                //       由上层加载好 VideoClip 后再注入本 Helper，以统一资源管线与热更。
                VideoClip clip = Resources.Load<VideoClip>(source);
                if (clip == null)
                {
                    m_Started = false;
                    RaiseError("VideoClip not found in Resources: " + source);
                    return;
                }
                m_Player.source = VideoSource.VideoClip;
                m_Player.url = null;
                m_Player.clip = clip;
            }

            // 异步准备；prepareCompleted 回调里再 Play（透出 Prepared）。
            m_Player.Prepare();
        }

        public void Stop()
        {
            if (m_Player != null)
            {
                m_Player.Stop();
            }
            m_Started = false;
            m_Paused = false;
        }

        public void Pause()
        {
            if (!m_Started || m_Player == null)
            {
                return;
            }
            m_Player.Pause();
            m_Paused = true;
        }

        public void Resume()
        {
            if (!m_Started || !m_Paused || m_Player == null)
            {
                return;
            }
            m_Player.Play();
            m_Paused = false;
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            // VideoPlayer 自驱，无需每帧推进。保留入口以便将来做超时 / 进度上报。
        }

        /// <summary>
        /// 释放：解绑事件并（若自建）释放 RenderTexture。VideoComponent 销毁时调用。
        /// </summary>
        public void Dispose()
        {
            if (m_Player != null)
            {
                m_Player.prepareCompleted -= OnPrepareCompleted;
                m_Player.loopPointReached -= OnLoopPointReached;
                m_Player.errorReceived -= OnErrorReceived;
            }

            if (m_OwnsRenderTexture && m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                UnityEngine.Object.Destroy(m_RenderTexture);
            }
        }

        // ===== VideoPlayer 事件 =====

        private void OnPrepareCompleted(VideoPlayer source)
        {
            RaisePrepared();
            // 仅在仍处于"已发起且未暂停"时才真正开始，避免准备期间被 Stop/Pause 的竞态。
            if (m_Started && !m_Paused)
            {
                m_Player.Play();
            }
        }

        private void OnLoopPointReached(VideoPlayer source)
        {
            // 循环态由 VideoPlayer 自行回卷，不向上透出完成。
            if (m_Loop)
            {
                return;
            }
            m_Started = false;
            RaiseCompleted();
        }

        private void OnErrorReceived(VideoPlayer source, string message)
        {
            m_Started = false;
            RaiseError(string.IsNullOrEmpty(message) ? "Unknown VideoPlayer error." : message);
        }

        // ===== 事件触发 =====

        private void RaisePrepared()
        {
            Prepared?.Invoke(this);
        }

        private void RaiseCompleted()
        {
            Completed?.Invoke(this);
        }

        private void RaiseError(string message)
        {
            ErrorOccurred?.Invoke(this, message);
        }

        public event Action<IVideoPlayerHelper> Prepared;
        public event Action<IVideoPlayerHelper> Completed;
        public event Action<IVideoPlayerHelper, string> ErrorOccurred;
    }
}
