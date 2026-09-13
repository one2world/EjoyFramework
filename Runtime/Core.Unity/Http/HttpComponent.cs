//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Http;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// HTTP 组件。
    /// Awake 解析 <see cref="IHttpManager"/> 并安装一个 <see cref="UnityWebRequestHttpClient"/> 作为底层客户端，
    /// 然后把 Send / Get / PostJson 以及 BaseUrl / RetryPolicy / SetDefaultHeader 转发给 Manager。
    ///
    /// 设计：不提供 GameEntry 静态访问器；业务通过 GetComponent 或自有装配获取本组件。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Http")]
    public sealed class HttpComponent : GameFrameworkComponent
    {
        private IHttpManager m_HttpManager;

        protected override void Awake()
        {
            base.Awake();
            m_HttpManager = Framework.GetModule<IHttpManager>();
            if (m_HttpManager == null)
            {
                Log.Fatal("Http manager is invalid.");
                return;
            }

            // 安装默认的 UnityWebRequest 客户端（由框架协程驱动，主线程回调）。
            m_HttpManager.SetClient(new UnityWebRequestHttpClient());
        }

        /// <summary>
        /// 基础 URL。请求 URL 为相对路径时自动前置拼接。
        /// </summary>
        public string BaseUrl
        {
            get { return m_HttpManager != null ? m_HttpManager.BaseUrl : string.Empty; }
            set { if (m_HttpManager != null) m_HttpManager.BaseUrl = value; }
        }

        /// <summary>
        /// 重试策略。
        /// </summary>
        public HttpRetryPolicy RetryPolicy
        {
            get { return m_HttpManager != null ? m_HttpManager.RetryPolicy : HttpRetryPolicy.Default; }
            set { if (m_HttpManager != null) m_HttpManager.RetryPolicy = value; }
        }

        /// <summary>
        /// 设置一个默认请求头（value 为 null 表示移除）。
        /// </summary>
        public void SetDefaultHeader(string key, string value)
        {
            if (m_HttpManager == null) { Log.Error("Http manager not ready."); return; }
            m_HttpManager.SetDefaultHeader(key, value);
        }

        /// <summary>
        /// 发送请求（应用 BaseUrl / 默认头 / 重试）。
        /// </summary>
        public void Send(HttpRequest request, Action<HttpResponse> onComplete)
        {
            if (m_HttpManager == null) { Log.Error("Http manager not ready."); onComplete?.Invoke(HttpResponse.NetworkError("Http manager not ready.")); return; }
            m_HttpManager.Send(request, onComplete);
        }

        /// <summary>
        /// 发起 GET。
        /// </summary>
        public void Get(string url, Action<HttpResponse> onComplete)
        {
            if (m_HttpManager == null) { Log.Error("Http manager not ready."); onComplete?.Invoke(HttpResponse.NetworkError("Http manager not ready.")); return; }
            m_HttpManager.Get(url, onComplete);
        }

        /// <summary>
        /// 发起 JSON POST。
        /// </summary>
        public void PostJson(string url, string json, Action<HttpResponse> onComplete)
        {
            if (m_HttpManager == null) { Log.Error("Http manager not ready."); onComplete?.Invoke(HttpResponse.NetworkError("Http manager not ready.")); return; }
            m_HttpManager.PostJson(url, json, onComplete);
        }
    }
}
