//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Sound;
using UnityEngine;
using UnityEngine.Audio;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 声音组件。完全转发给 SoundManager；保留 Inspector 配置预创建 group。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Sound")]
    public sealed class SoundComponent : GameFrameworkComponent
    {
        [Serializable]
        public sealed class SoundGroupSetting
        {
            public string Name = "Default";
            [Range(0f, 1f)] public float Volume = 1f;
            public bool Mute = false;
        }

        [SerializeField]
        private AudioMixer m_AudioMixer = null;

        [SerializeField]
        private int m_AgentsPerGroup = 8;

        [SerializeField]
        private SoundGroupSetting[] m_SoundGroups = null;

        [SerializeField]
        private Transform m_AgentRoot = null;

        private ISoundManager m_SoundManager;
        private DefaultSoundHelper m_Helper;

        protected override void Awake()
        {
            base.Awake();
            m_SoundManager = Framework.GetModule<ISoundManager>();
            if (m_SoundManager == null) { Log.Fatal("Sound manager is invalid."); return; }
            ConfigureManager();
        }

        private void ConfigureManager()
        {
            // SoundManager 自取 ResourceManager（懒拉取）
            if (m_AgentRoot == null)
            {
                m_AgentRoot = new GameObject("Sound Agents").transform;
                m_AgentRoot.SetParent(gameObject.transform);
            }
            m_Helper = new DefaultSoundHelper(m_AgentRoot, m_AudioMixer, m_AgentsPerGroup);
            m_SoundManager.SetSoundHelper(m_Helper);

            if (m_SoundGroups != null)
            {
                foreach (var g in m_SoundGroups)
                {
                    if (g == null || string.IsNullOrEmpty(g.Name)) continue;
                    AddSoundGroup(g.Name, g.Mute, g.Volume);
                }
            }
        }

        public int SoundGroupCount { get { return m_SoundManager.SoundGroupCount; } }

        public bool HasSoundGroup(string n) { return m_SoundManager.HasSoundGroup(n); }

        public bool AddSoundGroup(string name, bool mute = false, float volume = 1f)
        {
            if (!m_SoundManager.AddSoundGroup(name, null)) return false;
            var g = m_SoundManager.GetSoundGroup(name);
            if (g != null) { g.Mute = mute; g.Volume = volume; }
            return true;
        }

        public void Mute(string groupName, bool mute)
        {
            var g = m_SoundManager.GetSoundGroup(groupName);
            if (g != null) g.Mute = mute;
        }

        public void SetVolume(string groupName, float volume)
        {
            var g = m_SoundManager.GetSoundGroup(groupName);
            if (g != null) g.Volume = volume;
        }

        public float GetVolume(string groupName)
        {
            var g = m_SoundManager.GetSoundGroup(groupName);
            return g != null ? g.Volume : 0f;
        }

        public bool IsMuted(string groupName)
        {
            var g = m_SoundManager.GetSoundGroup(groupName);
            return g != null && g.Mute;
        }

        public int PlaySound(string assetName, string groupName)
        {
            return m_SoundManager.PlaySound(assetName, groupName, 0, null, null);
        }

        public int PlaySound(string assetName, string groupName, int priority, PlaySoundParams p, object userData)
        {
            return m_SoundManager.PlaySound(assetName, groupName, priority, p, userData);
        }

        public bool StopSound(int serialId, float fadeOut = 0f)
        {
            return m_SoundManager.StopSound(serialId, fadeOut);
        }

        public void StopAllLoadedSounds(float fadeOut = 0f)
        {
            m_SoundManager.StopAllLoadedSounds(fadeOut);
        }

        public void PauseSound(int serialId, float fadeOut = 0f)
        {
            m_SoundManager.PauseSound(serialId, fadeOut);
        }

        public void ResumeSound(int serialId, float fadeIn = 0f)
        {
            m_SoundManager.ResumeSound(serialId, fadeIn);
        }
    }
}
