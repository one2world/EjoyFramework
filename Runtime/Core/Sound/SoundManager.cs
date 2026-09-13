//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core.Sound
{
    /// <summary>
    /// 声音管理器（完整实现）。
    /// 流程：
    ///   PlaySound: 分配 serial → ResourceManager.LoadAsset<AudioClip> → 回调中 helper.PlaySound → 触发事件
    ///   StopSound: helper.StopSound(serial, fade) → 触发事件
    ///   Group 静音/音量 → helper.OnGroup{Volume,Mute}Changed
    /// </summary>
    internal sealed class SoundManager : FrameworkModule, ISoundManager
    {
        private readonly Dictionary<string, SoundGroup> m_SoundGroups = new Dictionary<string, SoundGroup>(StringComparer.Ordinal);
        private readonly Dictionary<int, PlayingSoundInfo> m_PlayingSounds = new Dictionary<int, PlayingSoundInfo>();
        private readonly Dictionary<int, LoadingSoundInfo> m_LoadingSounds = new Dictionary<int, LoadingSoundInfo>();
        private int m_Serial;
        private IResourceManager m_ResourceManager;
        private ISoundHelper m_SoundHelper;

        public event EventHandler<PlaySoundSuccessEventArgs> PlaySoundSuccess;
        public event EventHandler<PlaySoundFailureEventArgs> PlaySoundFailure;

        public SoundManager()
        {
        }

        /// <summary>测试用构造：直接注入 IResourceManager。</summary>
        internal SoundManager(IResourceManager resourceManager)
        {
            if (resourceManager == null) throw new FrameworkException("Resource manager is invalid.");
            m_ResourceManager = resourceManager;
        }

        // Priority 0：业务模块，依赖 Resource。
        public override int Priority { get { return 0; } }

        // 必需配置自检：依赖 SoundHelper 播放/停止声音。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_SoundHelper != null; } }
        public override string ConfigurationHint { get { return "Call SetSoundHelper(...) before use."; } }

        public int SoundGroupCount { get { return m_SoundGroups.Count; } }

        public override void Update(float a, float b) { }

        public override void Shutdown()
        {
            if (m_SoundHelper != null)
            {
                try { m_SoundHelper.StopAllLoadedSounds(0f); } catch (Exception e) { FrameworkLog.Error("SoundHelper.StopAllLoadedSounds on shutdown: {0}", e); }
                try { m_SoundHelper.Shutdown(); } catch (Exception e) { FrameworkLog.Error("SoundHelper.Shutdown: {0}", e); }
            }
            m_PlayingSounds.Clear();
            m_LoadingSounds.Clear();
            m_SoundGroups.Clear();
        }

        // ===== 注入 =====

        private IResourceManager Resource
        {
            get { return m_ResourceManager ?? (m_ResourceManager = Framework.GetModule<IResourceManager>()); }
        }

        public void SetSoundHelper(ISoundHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetSoundHelper));
            if (helper == null) throw new FrameworkException("Sound helper is invalid.");
            m_SoundHelper = helper;
            // 重放已注册的 group：AddSoundGroup 可能早于 SetSoundHelper（组件 Awake/Start 顺序不定），
            // 此前那些组不会被 helper 建出 AudioSource，导致播放无声。
            foreach (var kv in m_SoundGroups)
            {
                try { helper.RegisterSoundGroup(kv.Key); }
                catch (Exception ex) { FrameworkLog.Error("SoundHelper.RegisterSoundGroup('{0}') threw: {1}", kv.Key, ex); }
            }
        }

        // ===== Group =====

        public bool HasSoundGroup(string n) { return n != null && m_SoundGroups.ContainsKey(n); }

        public ISoundGroup GetSoundGroup(string n)
        {
            SoundGroup g;
            return n != null && m_SoundGroups.TryGetValue(n, out g) ? (ISoundGroup)g : null;
        }

        public ISoundGroup[] GetAllSoundGroups()
        {
            var arr = new ISoundGroup[m_SoundGroups.Count];
            int i = 0;
            foreach (var kv in m_SoundGroups) arr[i++] = kv.Value;
            return arr;
        }

        public bool AddSoundGroup(string name, ISoundGroupHelper groupHelper)
        {
            Framework.EnsureMainThread(nameof(AddSoundGroup));
            if (string.IsNullOrEmpty(name) || m_SoundGroups.ContainsKey(name)) return false;
            var group = new SoundGroup(name, groupHelper, this);
            m_SoundGroups.Add(name, group);
            if (m_SoundHelper != null) m_SoundHelper.RegisterSoundGroup(name);
            return true;
        }

        // ===== Play =====

        public int PlaySound(string assetName, string groupName, int priority, PlaySoundParams playSoundParams, object userData)
        {
            Framework.EnsureMainThread(nameof(PlaySound));
            if (m_SoundHelper == null) throw new FrameworkException("Sound helper is not set.");
            if (string.IsNullOrEmpty(assetName)) throw new FrameworkException("Sound asset name is invalid.");
            if (string.IsNullOrEmpty(groupName)) throw new FrameworkException("Sound group name is invalid.");

            if (!m_SoundGroups.ContainsKey(groupName))
            {
                FrameworkLog.Warning("PlaySound: group '{0}' does not exist.", groupName);
                return 0;
            }

            int serial = ++m_Serial;
            float startTime = NowSeconds();
            var entry = new LoadingSoundInfo
            {
                AssetName = assetName,
                GroupName = groupName,
                Priority = priority,
                Params = playSoundParams,
                UserData = userData,
                StartTime = startTime,
            };
            m_LoadingSounds.Add(serial, entry);
            var handle = Resource.LoadAssetWithHandle(assetName, priority, serial);
            entry.LoadHandle = handle;
            handle.Completed += OnSoundLoadCompleted;
            return serial;
        }

        public bool StopSound(int serialId, float fadeOutSeconds)
        {
            Framework.EnsureMainThread(nameof(StopSound));
            // 加载中：取消句柄，从字典移除
            LoadingSoundInfo loading;
            if (m_LoadingSounds.TryGetValue(serialId, out loading))
            {
                m_LoadingSounds.Remove(serialId);
                if (loading.LoadHandle != null) loading.LoadHandle.Cancel();
                if (loading.Params != null) ReferencePool.Release(loading.Params);
                FrameworkLog.Debug("Cancelled loading sound serial={0}", serialId);
                return true;
            }
            if (!m_PlayingSounds.ContainsKey(serialId)) return false;
            m_PlayingSounds.Remove(serialId);
            try { return m_SoundHelper.StopSound(serialId, fadeOutSeconds); }
            catch (Exception ex) { FrameworkLog.Error("SoundHelper.StopSound threw: {0}", ex); return false; }
        }

        public void StopAllLoadedSounds(float fadeOutSeconds)
        {
            Framework.EnsureMainThread(nameof(StopAllLoadedSounds));
            // 取消所有在加载的句柄
            foreach (var kv in m_LoadingSounds)
            {
                if (kv.Value.LoadHandle != null && !kv.Value.LoadHandle.IsDone) kv.Value.LoadHandle.Cancel();
                if (kv.Value.Params != null) ReferencePool.Release(kv.Value.Params);
            }
            m_LoadingSounds.Clear();
            m_PlayingSounds.Clear();
            if (m_SoundHelper != null)
            {
                try { m_SoundHelper.StopAllLoadedSounds(fadeOutSeconds); }
                catch (Exception ex) { FrameworkLog.Error("SoundHelper.StopAllLoadedSounds threw: {0}", ex); }
            }
        }

        public void PauseSound(int serialId, float fadeOutSeconds)
        {
            Framework.EnsureMainThread(nameof(PauseSound));
            if (m_SoundHelper == null) return;
            try { m_SoundHelper.PauseSound(serialId, fadeOutSeconds); }
            catch (Exception ex) { FrameworkLog.Error("SoundHelper.PauseSound threw: {0}", ex); }
        }

        public void ResumeSound(int serialId, float fadeInSeconds)
        {
            Framework.EnsureMainThread(nameof(ResumeSound));
            if (m_SoundHelper == null) return;
            try { m_SoundHelper.ResumeSound(serialId, fadeInSeconds); }
            catch (Exception ex) { FrameworkLog.Error("SoundHelper.ResumeSound threw: {0}", ex); }
        }

        // ===== 加载回调（handle.Completed） =====

        private void OnSoundLoadCompleted(IAssetLoadHandle handle)
        {
            if (!(handle.UserData is int serial))
            {
                FrameworkLog.Error("OnSoundLoadCompleted: unexpected UserData type '{0}', expected int serial.",
                    handle.UserData?.GetType().FullName ?? "null");
                if (handle.Asset != null && Resource != null) Resource.UnloadAsset(handle.Asset);
                return;
            }
            if (!m_LoadingSounds.TryGetValue(serial, out var info))
            {
                // 已被 StopSound 取消并移出字典；handle 也会处于 Cancelled 状态
                return;
            }
            m_LoadingSounds.Remove(serial);

            if (handle.Status == LoadAssetStatus.Cancelled)
            {
                if (info.Params != null) ReferencePool.Release(info.Params);
                return;
            }
            if (handle.Status == LoadAssetStatus.Failed)
            {
                if (info.Params != null) ReferencePool.Release(info.Params);
                FireFailure(serial, info.AssetName, info.GroupName, handle.FailureStatus + ": " + handle.ErrorMessage, info.UserData);
                return;
            }

            object asset = handle.Asset;
            bool ok;
            try { ok = m_SoundHelper.PlaySound(serial, info.GroupName, asset, info.Params); }
            catch (Exception ex)
            {
                FireFailure(serial, info.AssetName, info.GroupName, "SoundHelper.PlaySound threw: " + ex.Message, info.UserData);
                if (info.Params != null) ReferencePool.Release(info.Params);
                Resource.UnloadAsset(asset);
                return;
            }
            if (!ok)
            {
                FireFailure(serial, info.AssetName, info.GroupName, "No available sound agent in group.", info.UserData);
                if (info.Params != null) ReferencePool.Release(info.Params);
                Resource.UnloadAsset(asset);
                return;
            }

            m_PlayingSounds[serial] = new PlayingSoundInfo
            {
                AssetName = info.AssetName,
                GroupName = info.GroupName,
                ClipAsset = asset,
                UserData = info.UserData,
            };

            float totalDuration = NowSeconds() - info.StartTime;
            if (info.Params != null) ReferencePool.Release(info.Params);

            var h = PlaySoundSuccess;
            if (h != null)
            {
                var args = PlaySoundSuccessEventArgs.Create(serial, info.AssetName, info.GroupName, totalDuration, info.UserData);
                try { h(this, args); }
                catch (Exception ex) { FrameworkLog.Error("PlaySoundSuccess threw: {0}", ex); }
                ReferencePool.Release(args);
            }
        }

        private void FireFailure(int serial, string assetName, string groupName, string error, object userData)
        {
            FrameworkLog.Error("PlaySound failed: serial={0} asset='{1}' group='{2}' error={3}", serial, assetName, groupName, error);
            var h = PlaySoundFailure;
            if (h != null)
            {
                var args = PlaySoundFailureEventArgs.Create(serial, assetName, groupName, error, userData);
                try { h(this, args); }
                catch (Exception ex) { FrameworkLog.Error("PlaySoundFailure threw: {0}", ex); }
                ReferencePool.Release(args);
            }
        }

        private static float NowSeconds()
        {
            return Utility.Timestamp.SecondsF;
        }

        // ===== 内部数据 =====

        private sealed class LoadingSoundInfo
        {
            public string AssetName;
            public string GroupName;
            public int Priority;
            public PlaySoundParams Params;
            public object UserData;
            public float StartTime;
            public IAssetLoadHandle LoadHandle;
        }

        private sealed class PlayingSoundInfo
        {
            public string AssetName;
            public string GroupName;
            public object ClipAsset;
            public object UserData;
        }

        // ===== SoundGroup =====

        internal sealed class SoundGroup : ISoundGroup
        {
            private readonly string m_Name;
            private readonly ISoundGroupHelper m_GroupHelper;
            private readonly SoundManager m_Owner;
            private bool m_Mute;
            private float m_Volume = 1f;

            public SoundGroup(string name, ISoundGroupHelper helper, SoundManager owner)
            {
                m_Name = name; m_GroupHelper = helper; m_Owner = owner;
            }

            public string Name { get { return m_Name; } }

            public bool Mute
            {
                get { return m_Mute; }
                set
                {
                    if (m_Mute == value) return;
                    m_Mute = value;
                    if (m_Owner.m_SoundHelper != null)
                    {
                        try { m_Owner.m_SoundHelper.OnGroupMuteChanged(m_Name, value); }
                        catch (Exception ex) { FrameworkLog.Error("OnGroupMuteChanged threw: {0}", ex); }
                    }
                }
            }

            public float Volume
            {
                get { return m_Volume; }
                set
                {
                    float v = value < 0f ? 0f : value > 1f ? 1f : value;
                    if (m_Volume == v) return;
                    m_Volume = v;
                    if (m_Owner.m_SoundHelper != null)
                    {
                        try { m_Owner.m_SoundHelper.OnGroupVolumeChanged(m_Name, v); }
                        catch (Exception ex) { FrameworkLog.Error("OnGroupVolumeChanged threw: {0}", ex); }
                    }
                }
            }
        }
    }
}
