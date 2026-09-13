//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Streaming; // Vector3Lite（core 层无引擎依赖的轻量 Vector3）

namespace EjoyFramework.Core.Sound
{
    /// <summary>
    /// 声音管理器接口。
    /// </summary>
    public interface ISoundManager
    {
        /// <summary>
        /// 获取声音组数量。
        /// </summary>
        int SoundGroupCount { get; }

        /// <summary>
        /// 是否存在指定声音组。
        /// </summary>
        bool HasSoundGroup(string soundGroupName);

        /// <summary>
        /// 获取指定声音组。
        /// </summary>
        ISoundGroup GetSoundGroup(string soundGroupName);

        /// <summary>
        /// 获取所有声音组。
        /// </summary>
        ISoundGroup[] GetAllSoundGroups();

        /// <summary>
        /// 增加声音组。
        /// </summary>
        bool AddSoundGroup(string soundGroupName, ISoundGroupHelper soundGroupHelper);

        /// <summary>
        /// 播放声音。返回 SerialId（&gt;0 有效；0 表示未派发因参数错误）。
        /// </summary>
        int PlaySound(string soundAssetName, string soundGroupName, int priority, PlaySoundParams playSoundParams, object userData);

        /// <summary>
        /// 停止播放声音。
        /// </summary>
        bool StopSound(int serialId, float fadeOutSeconds);

        /// <summary>
        /// 停止所有已加载的声音。
        /// </summary>
        void StopAllLoadedSounds(float fadeOutSeconds);

        /// <summary>
        /// 暂停播放声音。
        /// </summary>
        void PauseSound(int serialId, float fadeOutSeconds);

        /// <summary>
        /// 恢复播放声音。
        /// </summary>
        void ResumeSound(int serialId, float fadeInSeconds);

        /// <summary>
        /// 设置声音 helper（处理 AudioSource 池与实际播放）。
        /// </summary>
        void SetSoundHelper(ISoundHelper soundHelper);

        /// <summary>
        /// 播放声音成功事件。
        /// </summary>
        event EventHandler<PlaySoundSuccessEventArgs> PlaySoundSuccess;

        /// <summary>
        /// 播放声音失败事件。
        /// </summary>
        event EventHandler<PlaySoundFailureEventArgs> PlaySoundFailure;
    }

    /// <summary>
    /// 声音组接口。
    /// </summary>
    public interface ISoundGroup
    {
        /// <summary>
        /// 获取声音组名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取或设置声音组是否静音。
        /// </summary>
        bool Mute { get; set; }

        /// <summary>
        /// 获取或设置声音组音量。
        /// </summary>
        float Volume { get; set; }
    }

    /// <summary>
    /// 声音组辅助器接口（标记类型；保留以便业务可附加 group 特定元数据）。
    /// </summary>
    public interface ISoundGroupHelper
    {
    }

    /// <summary>
    /// 声音 helper 接口。Unity 层实现 AudioSource 池 + 播放/停止/淡入淡出。
    /// </summary>
    public interface ISoundHelper
    {
        /// <summary>
        /// 注册声音组（helper 内部创建对应 AudioSource 池）。
        /// </summary>
        void RegisterSoundGroup(string groupName);

        /// <summary>
        /// 取消注册声音组。
        /// </summary>
        void UnregisterSoundGroup(string groupName);

        /// <summary>
        /// 播放声音。返回是否成功（无可用 agent 时返回 false）。
        /// clip 由 ResourceManager 加载产物（Unity 中为 AudioClip）。
        /// </summary>
        bool PlaySound(int serialId, string groupName, object clip, PlaySoundParams playSoundParams);

        /// <summary>
        /// 停止声音。
        /// </summary>
        bool StopSound(int serialId, float fadeOutSeconds);

        /// <summary>
        /// 停止所有。
        /// </summary>
        void StopAllLoadedSounds(float fadeOutSeconds);

        /// <summary>
        /// 暂停。
        /// </summary>
        void PauseSound(int serialId, float fadeOutSeconds);

        /// <summary>
        /// 恢复。
        /// </summary>
        void ResumeSound(int serialId, float fadeInSeconds);

        /// <summary>
        /// 通知音量/静音变更。
        /// </summary>
        void OnGroupVolumeChanged(string groupName, float volume);
        void OnGroupMuteChanged(string groupName, bool mute);

        /// <summary>
        /// Shutdown 时清理。
        /// </summary>
        void Shutdown();
    }

    /// <summary>
    /// 播放声音参数。
    /// </summary>
    public sealed class PlaySoundParams : IReference
    {
        private bool m_Loop;
        private float m_Volume;
        private float m_FadeInSeconds;
        private float m_Pitch;
        private float m_PanStereo;
        private float m_SpatialBlend;
        private float m_MaxDistance;
        private float m_Time;
        private bool m_MuteInSoundGroup;
        private int m_Priority;
        private bool m_Is3D;
        private Vector3Lite m_Position;

        public PlaySoundParams()
        {
            m_Loop = false;
            m_Volume = 1f;
            m_FadeInSeconds = 0f;
            m_Pitch = 1f;
            m_PanStereo = 0f;
            m_SpatialBlend = 0f;
            m_MaxDistance = 100f;
            m_Time = 0f;
            m_MuteInSoundGroup = false;
            m_Priority = 0;
            m_Is3D = false;
            m_Position = default(Vector3Lite);
        }

        public bool Loop { get { return m_Loop; } set { m_Loop = value; } }
        public float Volume { get { return m_Volume; } set { m_Volume = value; } }
        public float FadeInSeconds { get { return m_FadeInSeconds; } set { m_FadeInSeconds = value; } }
        public float Pitch { get { return m_Pitch; } set { m_Pitch = value; } }
        public float PanStereo { get { return m_PanStereo; } set { m_PanStereo = value; } }
        public float SpatialBlend { get { return m_SpatialBlend; } set { m_SpatialBlend = value; } }
        public float MaxDistance { get { return m_MaxDistance; } set { m_MaxDistance = value; } }
        public float Time { get { return m_Time; } set { m_Time = value; } }
        public bool MuteInSoundGroup { get { return m_MuteInSoundGroup; } set { m_MuteInSoundGroup = value; } }
        public int Priority { get { return m_Priority; } set { m_Priority = value; } }

        /// <summary>是否 3D 音效。为 true 时声音 agent 会把自身 transform 移动到 <see cref="Position"/> 并按 SpatialBlend 做空间混音。</summary>
        public bool Is3D { get { return m_Is3D; } set { m_Is3D = value; } }

        /// <summary>3D 播放位置（core 层 Vector3Lite，无引擎依赖）；仅在 <see cref="Is3D"/> 为 true 时生效。</summary>
        public Vector3Lite Position { get { return m_Position; } set { m_Position = value; } }

        public static PlaySoundParams Create()
        {
            PlaySoundParams playSoundParams = ReferencePool.Acquire<PlaySoundParams>();
            return playSoundParams;
        }

        public void Clear()
        {
            m_Loop = false;
            m_Volume = 1f;
            m_FadeInSeconds = 0f;
            m_Pitch = 1f;
            m_PanStereo = 0f;
            m_SpatialBlend = 0f;
            m_MaxDistance = 100f;
            m_Time = 0f;
            m_MuteInSoundGroup = false;
            m_Priority = 0;
            m_Is3D = false;
            m_Position = default(Vector3Lite);
        }
    }

    /// <summary>
    /// 播放声音成功事件。
    /// </summary>
    public sealed class PlaySoundSuccessEventArgs : FrameworkEventArgs
    {
        public int SerialId { get; private set; }
        public string SoundAssetName { get; private set; }
        public string SoundGroupName { get; private set; }
        public float Duration { get; private set; }
        public object UserData { get; private set; }

        public override void Clear()
        {
            SerialId = 0;
            SoundAssetName = null;
            SoundGroupName = null;
            Duration = 0f;
            UserData = null;
        }

        public static PlaySoundSuccessEventArgs Create(int serialId, string soundAssetName, string soundGroupName, float duration, object userData)
        {
            var e = ReferencePool.Acquire<PlaySoundSuccessEventArgs>();
            e.SerialId = serialId;
            e.SoundAssetName = soundAssetName;
            e.SoundGroupName = soundGroupName;
            e.Duration = duration;
            e.UserData = userData;
            return e;
        }
    }

    /// <summary>
    /// 播放声音失败事件。
    /// </summary>
    public sealed class PlaySoundFailureEventArgs : FrameworkEventArgs
    {
        public int SerialId { get; private set; }
        public string SoundAssetName { get; private set; }
        public string SoundGroupName { get; private set; }
        public string ErrorMessage { get; private set; }
        public object UserData { get; private set; }

        public override void Clear()
        {
            SerialId = 0;
            SoundAssetName = null;
            SoundGroupName = null;
            ErrorMessage = null;
            UserData = null;
        }

        public static PlaySoundFailureEventArgs Create(int serialId, string soundAssetName, string soundGroupName, string errorMessage, object userData)
        {
            var e = ReferencePool.Acquire<PlaySoundFailureEventArgs>();
            e.SerialId = serialId;
            e.SoundAssetName = soundAssetName;
            e.SoundGroupName = soundGroupName;
            e.ErrorMessage = errorMessage;
            e.UserData = userData;
            return e;
        }
    }
}
