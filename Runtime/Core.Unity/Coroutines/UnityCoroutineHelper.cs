//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using EjoyFramework.Core.Coroutines;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Unity 协程驱动器。把 IEnumerator 转给 MonoBehaviour.StartCoroutine。
    /// </summary>
    public sealed class UnityCoroutineHelper : ICoroutineHelper, ICoroutineHelperStatus
    {
        private readonly MonoBehaviour m_Host;

        public UnityCoroutineHelper(MonoBehaviour host)
        {
            m_Host = host != null ? host : throw new System.ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// UnityEngine.Object 在原生对象销毁后会比较为 null；借此让 Core 层识别失效 host，
        /// 使下一个场景创建的 BaseComponent 能够重新注入 helper。
        /// </summary>
        public bool IsAvailable { get { return m_Host != null; } }

        public object StartCoroutine(IEnumerator routine)
        {
            return m_Host.StartCoroutine(routine);
        }

        public void StopCoroutine(object cookie)
        {
            if (cookie is UnityEngine.Coroutine c) m_Host.StopCoroutine(c);
        }
    }
}
