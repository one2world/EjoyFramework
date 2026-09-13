//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// AudioSource 包装。负责单个声音实例的播放/停止/淡入淡出。
    /// </summary>
    internal sealed class SoundAgent : MonoBehaviour
    {
        public int SerialId { get; private set; }
        public string GroupName { get; private set; }
        public int Priority { get; private set; }   // 当前在播声音的优先级（用于满组时的抢占决策）
        public bool IsBusy { get { return m_AudioSource != null && (m_AudioSource.isPlaying || m_FadeRoutine != null); } }

        private AudioSource m_AudioSource;
        private Coroutine m_FadeRoutine;
        private float m_TargetVolume;
        private float m_GroupVolume = 1f;
        private bool m_GroupMute;
        private bool m_InstanceMute;

        public void Init(AudioMixerGroup mixerGroup)
        {
            m_AudioSource = gameObject.AddComponent<AudioSource>();
            m_AudioSource.playOnAwake = false;
            m_AudioSource.outputAudioMixerGroup = mixerGroup;
        }

        public void ApplyGroup(float volume, bool mute)
        {
            m_GroupVolume = volume;
            m_GroupMute = mute;
            UpdateEffectiveVolume();
        }

        public bool Play(int serialId, string groupName, AudioClip clip, EjoyFramework.Core.Sound.PlaySoundParams p)
        {
            if (clip == null) return false;
            SerialId = serialId;
            GroupName = groupName;
            Priority = p != null ? p.Priority : 0;
            m_AudioSource.clip = clip;
            m_AudioSource.loop = p != null && p.Loop;
            m_AudioSource.pitch = p != null ? p.Pitch : 1f;
            m_AudioSource.panStereo = p != null ? p.PanStereo : 0f;
            m_AudioSource.spatialBlend = p != null ? p.SpatialBlend : 0f;
            m_AudioSource.maxDistance = p != null ? p.MaxDistance : 100f;
            m_AudioSource.time = p != null ? p.Time : 0f;

            // 3D 音效：把 agent transform 移动到指定世界坐标（Vector3Lite → UnityEngine.Vector3）。
            // 2D（Is3D=false）保持原行为不动 transform；spatialBlend 仍由上面的 p.SpatialBlend 决定。
            if (p != null && p.Is3D)
            {
                transform.position = new Vector3(p.Position.X, p.Position.Y, p.Position.Z);
            }
            m_InstanceMute = p != null && p.MuteInSoundGroup;
            m_TargetVolume = p != null ? p.Volume : 1f;

            float fadeIn = p != null ? p.FadeInSeconds : 0f;
            if (fadeIn > 0f)
            {
                m_AudioSource.volume = 0f;
                m_AudioSource.Play();
                StartFade(fadeIn, ComputeEffectiveVolume(), null);
            }
            else
            {
                UpdateEffectiveVolume();
                m_AudioSource.Play();
            }
            return true;
        }

        public void Stop(float fadeOutSeconds)
        {
            if (fadeOutSeconds <= 0f)
            {
                ClearAndDeactivate();
                return;
            }
            StartFade(fadeOutSeconds, 0f, ClearAndDeactivate);
        }

        public void Pause(float fadeOutSeconds)
        {
            if (fadeOutSeconds <= 0f) { m_AudioSource.Pause(); return; }
            StartFade(fadeOutSeconds, 0f, () => m_AudioSource.Pause());
        }

        public void Resume(float fadeInSeconds)
        {
            m_AudioSource.UnPause();
            if (fadeInSeconds <= 0f) { UpdateEffectiveVolume(); return; }
            StartFade(fadeInSeconds, ComputeEffectiveVolume(), null);
        }

        private void StartFade(float duration, float target, System.Action onComplete)
        {
            if (m_FadeRoutine != null) StopCoroutine(m_FadeRoutine);
            m_FadeRoutine = StartCoroutine(FadeRoutine(duration, target, onComplete));
        }

        private IEnumerator FadeRoutine(float duration, float target, System.Action onComplete)
        {
            float from = m_AudioSource.volume;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                m_AudioSource.volume = Mathf.Lerp(from, target, elapsed / duration);
                yield return null;
            }
            m_AudioSource.volume = target;
            m_FadeRoutine = null;
            if (onComplete != null) onComplete();
        }

        private void UpdateEffectiveVolume()
        {
            // fade 进行中时由 fade 协程独占 volume；此处直接写会与每帧 Lerp 互相打架（组音量变更 vs 淡入淡出）。
            if (m_FadeRoutine != null) return;
            m_AudioSource.volume = ComputeEffectiveVolume();
        }

        private float ComputeEffectiveVolume()
        {
            if (m_GroupMute || m_InstanceMute) return 0f;
            return m_TargetVolume * m_GroupVolume;
        }

        private void ClearAndDeactivate()
        {
            // SetActive(false) 会停止协程，但不会替我们清空 Coroutine 句柄。
            // 若在淡入期间被抢占，残留句柄会让 IsBusy 永久为 true，agent 再也无法回池。
            if (m_FadeRoutine != null)
            {
                StopCoroutine(m_FadeRoutine);
                m_FadeRoutine = null;
            }

            if (m_AudioSource != null)
            {
                m_AudioSource.Stop();
                m_AudioSource.clip = null;
            }
            SerialId = 0;
            gameObject.SetActive(false);
        }
    }
}
