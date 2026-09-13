//------------------------------------------------------------
// EjoyGame Framework Tests (PlayMode)
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using System.Reflection;
using EjoyFramework.Core.Sound;
using EjoyFramework.Core.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EjoyFramework.Tests.PlayMode
{
    public sealed class SoundAgentLifecycleTests : PlayModeTestBase
    {
        [UnityTest]
        public IEnumerator ImmediateStop_DuringFade_ClearsBusyState()
        {
            Type agentType = typeof(BaseComponent).Assembly.GetType(
                "EjoyFramework.Core.Unity.SoundAgent",
                throwOnError: true);
            GameObject go = CreateGameObject("SoundAgent");
            Component agent = go.AddComponent(agentType);
            Invoke(agent, "Init", (object)null);

            AudioClip clip = AudioClip.Create("SoundAgentLifecycle", 4410, 1, 44100, false);
            var playParams = new PlaySoundParams
            {
                Volume = 1f,
                FadeInSeconds = 10f,
            };

            Assert.IsTrue((bool)Invoke(agent, "Play", 1, "Sfx", clip, playParams));
            yield return null;
            Assert.IsTrue(ReadBusy(agent), "淡入期间 agent 应处于 busy。 ");

            Invoke(agent, "Stop", 0f);

            Assert.IsFalse(ReadBusy(agent),
                "立即停止必须终止淡入协程并清空句柄，否则 agent 会永久无法回池。 ");
            UnityEngine.Object.DestroyImmediate(clip);
        }

        private static object Invoke(Component target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing method: " + methodName);
            return method.Invoke(target, args);
        }

        private static bool ReadBusy(Component target)
        {
            PropertyInfo property = target.GetType().GetProperty(
                "IsBusy",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(property, "Missing IsBusy property.");
            return (bool)property.GetValue(target);
        }
    }
}
