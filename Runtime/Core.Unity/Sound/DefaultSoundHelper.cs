//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Sound;
using UnityEngine;
using UnityEngine.Audio;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认声音 helper：每组维护 N 个 AudioSource SoundAgent，按 serialId → agent 反查。
    /// 业务可注入自定义 helper 替换。
    /// </summary>
    public sealed class DefaultSoundHelper : ISoundHelper
    {
        private readonly Transform m_AgentRoot;
        private readonly AudioMixer m_AudioMixer;
        private readonly int m_AgentsPerGroup;
        private readonly Dictionary<string, GroupState> m_Groups = new Dictionary<string, GroupState>(System.StringComparer.Ordinal);

        public DefaultSoundHelper(Transform agentRoot, AudioMixer audioMixer, int agentsPerGroup = 8)
        {
            m_AgentRoot = agentRoot;
            m_AudioMixer = audioMixer;
            m_AgentsPerGroup = Mathf.Max(1, agentsPerGroup);
        }

        public void RegisterSoundGroup(string groupName)
        {
            if (m_Groups.ContainsKey(groupName)) return;
            var groupRoot = new GameObject("SoundGroup-" + groupName).transform;
            if (m_AgentRoot != null) groupRoot.SetParent(m_AgentRoot, false);

            var state = new GroupState { Root = groupRoot, MixerGroup = ResolveMixerGroup(groupName) };
            for (int i = 0; i < m_AgentsPerGroup; i++)
            {
                var go = new GameObject("Agent-" + i);
                go.transform.SetParent(groupRoot, false);
                go.SetActive(false);
                var agent = go.AddComponent<SoundAgent>();
                agent.Init(state.MixerGroup);
                agent.ApplyGroup(state.Volume, state.Mute);
                state.Agents.Add(agent);
            }
            m_Groups[groupName] = state;
        }

        public void UnregisterSoundGroup(string groupName)
        {
            GroupState s;
            if (!m_Groups.TryGetValue(groupName, out s)) return;
            foreach (var a in s.Agents)
            {
                if (a != null && a.gameObject != null) Object.Destroy(a.gameObject);
            }
            if (s.Root != null) Object.Destroy(s.Root.gameObject);
            m_Groups.Remove(groupName);
        }

        public bool PlaySound(int serialId, string groupName, object clipObj, PlaySoundParams playSoundParams)
        {
            GroupState s;
            if (!m_Groups.TryGetValue(groupName, out s)) return false;
            var clip = clipObj as AudioClip;
            if (clip == null) return false;

            SoundAgent agent = FindIdleAgent(s);
            if (agent == null)
            {
                // 满组：按优先级抢占——抢走优先级严格低于本次的在播声音；若全部 >= 本次则放弃播放。
                agent = TryStealAgent(s, playSoundParams != null ? playSoundParams.Priority : 0);
                if (agent == null) return false;
            }

            agent.gameObject.SetActive(true);
            agent.ApplyGroup(s.Volume, s.Mute);
            return agent.Play(serialId, groupName, clip, playSoundParams);
        }

        // 满组抢占：找当前优先级最低的在播 agent；若其优先级严格低于新声音优先级则停掉复用，否则返回 null。
        private SoundAgent TryStealAgent(GroupState s, int newPriority)
        {
            SoundAgent lowest = null;
            int lowestPriority = int.MaxValue;
            for (int i = 0; i < s.Agents.Count; i++)
            {
                var a = s.Agents[i];
                if (a == null || !a.IsBusy) continue;
                if (a.Priority < lowestPriority) { lowestPriority = a.Priority; lowest = a; }
            }
            if (lowest != null && lowestPriority < newPriority)
            {
                lowest.Stop(0f);   // 立即释放（ClearAndDeactivate 同步执行）
                return lowest;
            }
            return null;
        }

        public bool StopSound(int serialId, float fadeOutSeconds)
        {
            var agent = FindAgent(serialId);
            if (agent == null) return false;
            agent.Stop(fadeOutSeconds);
            return true;
        }

        public void StopAllLoadedSounds(float fadeOutSeconds)
        {
            foreach (var kv in m_Groups)
            {
                foreach (var a in kv.Value.Agents)
                {
                    if (a != null && a.IsBusy) a.Stop(fadeOutSeconds);
                }
            }
        }

        public void PauseSound(int serialId, float fadeOutSeconds)
        {
            var agent = FindAgent(serialId);
            if (agent != null) agent.Pause(fadeOutSeconds);
        }

        public void ResumeSound(int serialId, float fadeInSeconds)
        {
            var agent = FindAgent(serialId);
            if (agent != null) agent.Resume(fadeInSeconds);
        }

        public void OnGroupVolumeChanged(string groupName, float volume)
        {
            GroupState s;
            if (!m_Groups.TryGetValue(groupName, out s)) return;
            s.Volume = volume;
            foreach (var a in s.Agents) if (a != null) a.ApplyGroup(s.Volume, s.Mute);
        }

        public void OnGroupMuteChanged(string groupName, bool mute)
        {
            GroupState s;
            if (!m_Groups.TryGetValue(groupName, out s)) return;
            s.Mute = mute;
            foreach (var a in s.Agents) if (a != null) a.ApplyGroup(s.Volume, s.Mute);
        }

        public void Shutdown()
        {
            foreach (var kv in m_Groups)
            {
                if (kv.Value.Root != null) Object.Destroy(kv.Value.Root.gameObject);
            }
            m_Groups.Clear();
        }

        // ===== private =====

        private SoundAgent FindIdleAgent(GroupState s)
        {
            for (int i = 0; i < s.Agents.Count; i++)
            {
                var a = s.Agents[i];
                if (a != null && !a.IsBusy) return a;
            }
            return null;
        }

        private SoundAgent FindAgent(int serialId)
        {
            foreach (var kv in m_Groups)
            {
                for (int i = 0; i < kv.Value.Agents.Count; i++)
                {
                    var a = kv.Value.Agents[i];
                    if (a != null && a.SerialId == serialId) return a;
                }
            }
            return null;
        }

        private AudioMixerGroup ResolveMixerGroup(string groupName)
        {
            if (m_AudioMixer == null) return null;
            var groups = m_AudioMixer.FindMatchingGroups(groupName);
            return groups != null && groups.Length > 0 ? groups[0] : null;
        }

        private sealed class GroupState
        {
            public Transform Root;
            public AudioMixerGroup MixerGroup;
            public float Volume = 1f;
            public bool Mute = false;
            public readonly List<SoundAgent> Agents = new List<SoundAgent>();
        }
    }
}
