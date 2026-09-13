//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.GamePlay.Music
{
    /// <summary>
    /// BGM / 音乐总监（Unity 驱动组件）。持有两个 <see cref="AudioSource"/>（通道 A / B）、
    /// 一个 <see cref="CrossfadeMixer"/>、一个 <see cref="DuckController"/>，以及可选的 <see cref="MusicPlaylist"/>。
    /// <para>
    /// 纯逻辑（交叉淡变 / 压低 / 播放列表推进）位于 EjoyFramework.GamePlay 程序集，本组件仅负责：
    /// 在 <see cref="Update"/> 中按 Time.deltaTime 推进 mixer 与 duck，并把
    /// <c>mixer.VolumeX * duck.CurrentMultiplier</c> 写入对应 AudioSource.volume。
    /// </para>
    /// 对 null clip 保持健壮。
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        [Tooltip("通道 A 的 AudioSource，可在编辑器中指定；为空则运行时自动创建。")]
        [SerializeField] private AudioSource m_SourceA;

        [Tooltip("通道 B 的 AudioSource，可在编辑器中指定；为空则运行时自动创建。")]
        [SerializeField] private AudioSource m_SourceB;

        private readonly CrossfadeMixer m_Mixer = new CrossfadeMixer();
        private readonly DuckController m_Duck = new DuckController();
        private MusicPlaylist m_Playlist;

        // 是否在淡出（Stop）后需要在静音时停止音源。
        private bool m_StopWhenSilent = false;

        /// <summary>
        /// 主音量，范围 [0,1]。直接转发到内部 <see cref="CrossfadeMixer.MasterVolume"/>。
        /// </summary>
        public float MasterVolume
        {
            get { return m_Mixer.MasterVolume; }
            set { m_Mixer.MasterVolume = value; }
        }

        /// <summary>
        /// 当前是否正在交叉淡变 / 淡入淡出。
        /// </summary>
        public bool IsCrossfading
        {
            get { return m_Mixer.IsCrossfading; }
        }

        /// <summary>
        /// 当前生效的压低倍率，[0,1]。
        /// </summary>
        public float DuckMultiplier
        {
            get { return m_Duck.CurrentMultiplier; }
        }

        /// <summary>
        /// 可选的播放列表。为 null 表示未启用自动连播。
        /// </summary>
        public MusicPlaylist Playlist
        {
            get { return m_Playlist; }
            set { m_Playlist = value; }
        }

        private void Awake()
        {
            EnsureSources();
        }

        /// <summary>
        /// 播放一段音乐：将 clip 路由到当前“非活动”通道、令其开始播放，并发起交叉淡变。
        /// 完成后该通道成为新的活动通道。clip 为 null 时为空操作。
        /// </summary>
        /// <param name="clip">要播放的音频片段。</param>
        /// <param name="fadeDuration">交叉淡变时长（秒），默认 1。&lt;= 0 表示瞬时切换。</param>
        public void Play(AudioClip clip, float fadeDuration = 1f)
        {
            if (clip == null)
            {
                return;
            }

            EnsureSources();
            m_StopWhenSilent = false;

            // 进入通道 = 当前活动通道的另一个。
            AudioSource incoming = m_Mixer.ActiveChannel == 0 ? m_SourceB : m_SourceA;

            incoming.clip = clip;
            incoming.volume = 0f;
            incoming.loop = true;
            if (!incoming.isPlaying)
            {
                incoming.Play();
            }

            m_Mixer.StartCrossfade(fadeDuration);
        }

        /// <summary>
        /// 停止音乐：将活动通道淡出，待静音后停止两个音源。
        /// </summary>
        /// <param name="fadeDuration">淡出时长（秒），默认 1。&lt;= 0 表示瞬时停止。</param>
        public void Stop(float fadeDuration = 1f)
        {
            EnsureSources();
            m_StopWhenSilent = true;
            m_Mixer.FadeOut(fadeDuration);
        }

        /// <summary>
        /// 压入一个压低请求（如对白期间降低 BGM）。
        /// </summary>
        /// <param name="multiplier">目标倍率，[0,1]。</param>
        /// <param name="attack">达到目标的时间（秒），默认 0.2。</param>
        /// <returns>句柄，用于 <see cref="PopDuck"/>。</returns>
        public int PushDuck(float multiplier, float attack = 0.2f)
        {
            return m_Duck.PushDuck(multiplier, attack);
        }

        /// <summary>
        /// 弹出指定压低请求，朝剩余最强压低（或 1）恢复。
        /// </summary>
        /// <param name="handle">由 <see cref="PushDuck"/> 返回的句柄。</param>
        /// <param name="release">恢复时间（秒），默认 0.4。</param>
        public void PopDuck(int handle, float release = 0.4f)
        {
            m_Duck.PopDuck(handle, release);
        }

        /// <summary>
        /// 推进播放列表并播放下一首（若已配置 <see cref="Playlist"/> 且其有曲目）。
        /// 本方法仅按需调用（如外部判定当前曲目播放完毕），不在 Update 中自动连播以保持简单可控。
        /// </summary>
        /// <param name="fadeDuration">交叉淡变时长（秒）。</param>
        /// <param name="clipResolver">把曲目标识解析为 AudioClip 的回调（由游戏层提供资源加载）。</param>
        /// <returns>推进得到的曲目标识；无可播放曲目时返回 null。</returns>
        public string PlayNext(float fadeDuration, System.Func<string, AudioClip> clipResolver)
        {
            if (m_Playlist == null || m_Playlist.Count == 0)
            {
                return null;
            }

            string trackId = m_Playlist.Next();
            if (string.IsNullOrEmpty(trackId))
            {
                return null;
            }

            if (clipResolver != null)
            {
                AudioClip clip = clipResolver(trackId);
                Play(clip, fadeDuration);
            }

            return trackId;
        }

        /// <summary>
        /// 每帧推进 mixer 与 duck，并把最终增益写入两个 AudioSource。
        /// 当处于停止流程且两通道均已静音时，停止音源。
        /// </summary>
        private void Update()
        {
            float dt = Time.deltaTime;
            m_Mixer.Tick(dt);
            m_Duck.Tick(dt);

            EnsureSources();

            float duck = m_Duck.CurrentMultiplier;
            m_SourceA.volume = m_Mixer.VolumeA * duck;
            m_SourceB.volume = m_Mixer.VolumeB * duck;

            if (m_StopWhenSilent && !m_Mixer.IsCrossfading)
            {
                // 活动通道已淡出到 ~0，停止两个音源。
                if (m_SourceA.volume <= 0.0001f)
                {
                    StopSource(m_SourceA);
                }

                if (m_SourceB.volume <= 0.0001f)
                {
                    StopSource(m_SourceB);
                }

                m_StopWhenSilent = false;
            }
        }

        // 确保两个 AudioSource 存在（运行时自动创建）。
        private void EnsureSources()
        {
            if (m_SourceA == null)
            {
                m_SourceA = CreateSource();
            }

            if (m_SourceB == null)
            {
                m_SourceB = CreateSource();
            }
        }

        private AudioSource CreateSource()
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0f;
            return source;
        }

        private static void StopSource(AudioSource source)
        {
            if (source != null && source.isPlaying)
            {
                source.Stop();
            }
        }
    }
}
