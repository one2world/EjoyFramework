//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Resource;
using EjoyFramework.Core.Sound;
using EjoyFramework.Tests.TestSupport;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class SoundManagerTests
    {
        private sealed class FakeSoundHelper : ISoundHelper
        {
            public readonly HashSet<string> Groups = new HashSet<string>();
            public readonly Dictionary<int, string> Playing = new Dictionary<int, string>();
            public bool ReturnFailOnPlay;
            public int VolumeChangedCalls, MuteChangedCalls;

            public void RegisterSoundGroup(string groupName) { Groups.Add(groupName); }
            public void UnregisterSoundGroup(string groupName) { Groups.Remove(groupName); }

            public bool PlaySound(int serialId, string groupName, object clip, PlaySoundParams p)
            {
                if (ReturnFailOnPlay) return false;
                if (!Groups.Contains(groupName)) return false;
                Playing[serialId] = groupName;
                return true;
            }

            public bool StopSound(int serialId, float fade) { return Playing.Remove(serialId); }
            public void StopAllLoadedSounds(float fade) { Playing.Clear(); }
            public void PauseSound(int serialId, float fade) { }
            public void ResumeSound(int serialId, float fade) { }
            public void OnGroupVolumeChanged(string g, float v) { VolumeChangedCalls++; }
            public void OnGroupMuteChanged(string g, bool m) { MuteChangedCalls++; }
            public void Shutdown() { Groups.Clear(); Playing.Clear(); }
        }

        private ISoundManager m_SM;
        private FakeSoundHelper m_Helper;
        private MockResourceManager m_Res;

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
            m_Helper = new FakeSoundHelper();
            m_Res = new MockResourceManager();
            m_SM = new SoundManager(m_Res);
            m_SM.SetSoundHelper(m_Helper);
            m_SM.AddSoundGroup("Music", null);
            m_SM.AddSoundGroup("Effect", null);
        }

        [Test]
        public void AddSoundGroup_RegistersWithHelper()
        {
            Assert.IsTrue(m_Helper.Groups.Contains("Music"));
            Assert.IsTrue(m_Helper.Groups.Contains("Effect"));
            Assert.AreEqual(2, m_SM.SoundGroupCount);
        }

        [Test]
        public void PlaySound_SyncSuccess_FiresEvent()
        {
            int capturedSerial = -1;
            string capturedAsset = null;
            string capturedGroup = null;
            m_SM.PlaySoundSuccess += (s, e) =>
            {
                capturedSerial = e.SerialId;
                capturedAsset = e.SoundAssetName;
                capturedGroup = e.SoundGroupName;
            };

            int serial = m_SM.PlaySound("Audio/bgm.wav", "Music", 0, null, null);

            Assert.Greater(serial, 0);
            Assert.AreEqual(serial, capturedSerial);
            Assert.AreEqual("Audio/bgm.wav", capturedAsset);
            Assert.AreEqual("Music", capturedGroup);
            Assert.IsTrue(m_Helper.Playing.ContainsKey(serial));
        }

        [Test]
        public void PlaySound_HelperRejects_FiresFailure()
        {
            m_Helper.ReturnFailOnPlay = true;
            string capturedError = null;
            m_SM.PlaySoundFailure += (s, e) => capturedError = e.ErrorMessage;

            int serial = m_SM.PlaySound("Audio/bgm.wav", "Music", 0, null, null);

            Assert.Greater(serial, 0);
            Assert.IsNotNull(capturedError);
            Assert.IsFalse(m_Helper.Playing.ContainsKey(serial));
        }

        [Test]
        public void PlaySound_LoadFailure_FiresFailure()
        {
            m_Res.SyncSucceed = false;
            string capturedError = null;
            m_SM.PlaySoundFailure += (s, e) => capturedError = e.ErrorMessage;

            int serial = m_SM.PlaySound("Audio/Bad.wav", "Music", 0, null, null);
            Assert.Greater(serial, 0);
            StringAssert.Contains("fake load failed", capturedError);
        }

        [Test]
        public void PlaySound_UnknownGroup_ReturnsZero()
        {
            int serial = m_SM.PlaySound("Audio/x.wav", "Missing", 0, null, null);
            Assert.AreEqual(0, serial);
        }

        [Test]
        public void StopSound_DelegatesToHelper()
        {
            int serial = m_SM.PlaySound("Audio/bgm.wav", "Music", 0, null, null);
            Assert.IsTrue(m_Helper.Playing.ContainsKey(serial));

            Assert.IsTrue(m_SM.StopSound(serial, 0f));
            Assert.IsFalse(m_Helper.Playing.ContainsKey(serial));
        }

        [Test]
        public void SetGroupVolume_PropagatesToHelper()
        {
            var g = m_SM.GetSoundGroup("Music");
            g.Volume = 0.5f;
            Assert.AreEqual(1, m_Helper.VolumeChangedCalls);
            Assert.AreEqual(0.5f, g.Volume);
        }

        [Test]
        public void SetGroupMute_PropagatesToHelper()
        {
            var g = m_SM.GetSoundGroup("Music");
            g.Mute = true;
            Assert.AreEqual(1, m_Helper.MuteChangedCalls);
            Assert.IsTrue(g.Mute);
        }

        [Test]
        public void Volume_ClampedTo_0_1()
        {
            var g = m_SM.GetSoundGroup("Music");
            g.Volume = -1f;
            Assert.AreEqual(0f, g.Volume);
            g.Volume = 5f;
            Assert.AreEqual(1f, g.Volume);
        }
    }
}
