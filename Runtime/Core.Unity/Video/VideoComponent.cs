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
    /// 视频播放组件。
    /// Awake 解析 <see cref="IVideoManager"/>，在本 GameObject 上挂一个 <see cref="VideoPlayer"/> 并以
    /// <see cref="UnityVideoPlayerHelper"/> 注入为底层辅助器，然后把 Play / Stop / Pause / Resume / Skip 转发给 Manager。
    ///
    /// 渲染：辅助器默认渲染到一个 <see cref="RenderTexture"/>（<see cref="TargetTexture"/>），业务侧把它绑到 RawImage 即可显示。
    ///
    /// 设计：不提供 GameEntry 静态访问器；业务通过 GetComponent 或自有装配获取本组件（与 HttpComponent 一致）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Video")]
    public sealed class VideoComponent : GameFrameworkComponent
    {
        private IVideoManager m_VideoManager;
        private UnityVideoPlayerHelper m_Helper;

        protected override void Awake()
        {
            base.Awake();
            m_VideoManager = Framework.GetModule<IVideoManager>();
            if (m_VideoManager == null)
            {
                Log.Fatal("Video manager is invalid.");
                return;
            }

            // 在本 GameObject 上准备一个 VideoPlayer（已存在则复用），并安装默认辅助器。
            VideoPlayer player = GetComponent<VideoPlayer>();
            if (player == null)
            {
                player = gameObject.AddComponent<VideoPlayer>();
            }

            m_Helper = new UnityVideoPlayerHelper(player);
            m_VideoManager.SetHelper(m_Helper);
        }

        protected override void OnDestroy()
        {
            // 先解除 Manager 对辅助器的引用，再释放辅助器（解绑事件 / 释放 RenderTexture）。
            if (m_VideoManager != null)
            {
                m_VideoManager.SetHelper(null);
            }
            m_Helper?.Dispose();
            m_Helper = null;

            base.OnDestroy();
        }

        /// <summary>当前视频渲染目标纹理（绑定到 RawImage 用于显示）。</summary>
        public RenderTexture TargetTexture
        {
            get { return m_Helper != null ? m_Helper.TargetTexture : null; }
        }

        /// <summary>当前是否正在播放视频。</summary>
        public bool IsPlaying
        {
            get { return m_VideoManager != null && m_VideoManager.IsPlaying; }
        }

        /// <summary>是否允许跳过当前视频。</summary>
        public bool Skippable
        {
            get { return m_VideoManager != null && m_VideoManager.Skippable; }
            set { if (m_VideoManager != null) m_VideoManager.Skippable = value; }
        }

        /// <summary>
        /// 播放一段视频（source 为 clip 名或 URL，含 "://" 视为 URL）。
        /// </summary>
        public void Play(string source, bool loop = false, Action onCompleted = null, Action<string> onError = null)
        {
            if (m_VideoManager == null)
            {
                Log.Error("Video manager not ready.");
                onError?.Invoke("Video manager not ready.");
                return;
            }
            m_VideoManager.Play(source, loop, onCompleted, onError);
        }

        /// <summary>停止当前播放。</summary>
        public void Stop()
        {
            if (m_VideoManager == null) { Log.Error("Video manager not ready."); return; }
            m_VideoManager.Stop();
        }

        /// <summary>暂停当前播放。</summary>
        public void Pause()
        {
            if (m_VideoManager == null) { Log.Error("Video manager not ready."); return; }
            m_VideoManager.Pause();
        }

        /// <summary>恢复当前播放。</summary>
        public void Resume()
        {
            if (m_VideoManager == null) { Log.Error("Video manager not ready."); return; }
            m_VideoManager.Resume();
        }

        /// <summary>跳过当前视频（仅当 <see cref="Skippable"/> 为 true 时生效），返回是否真正执行了跳过。</summary>
        public bool Skip()
        {
            if (m_VideoManager == null) { Log.Error("Video manager not ready."); return false; }
            return m_VideoManager.Skip();
        }
    }
}
