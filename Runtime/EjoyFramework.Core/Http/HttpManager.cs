//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Http
{
    /// <summary>
    /// HTTP 管理器（FrameworkModule 实现）。
    ///
    /// 职责：
    ///   1) 拼接 BaseUrl（相对 URL 前置；绝对 URL 原样）；
    ///   2) 合并默认请求头（请求自带的同名头优先）；
    ///   3) 调 <see cref="IHttpClient.Send"/> 发送；
    ///   4) 失败时按 <see cref="HttpRetryPolicy"/> 自动重发，重试决策完全委托给纯函数 <see cref="HttpRetryPolicy.ShouldRetry"/>（可单测）。
    ///
    /// 重试时序说明：失败请求进入待重试队列，由 <see cref="Update"/> 消费
    /// <see cref="HttpRetryPolicy.NextDelaySeconds"/> 计算出的真实时间退避；回调内不递归重发。
    /// POST 等非幂等方法默认不重试，业务确认请求可安全重复时需显式设置
    /// <see cref="HttpRequest.RetryNonIdempotent"/>。
    /// </summary>
    internal sealed class HttpManager : FrameworkModule, IHttpManager
    {
        private IHttpClient m_Client;
        private string m_BaseUrl = string.Empty;
        private HttpRetryPolicy m_RetryPolicy = HttpRetryPolicy.Default;
        private readonly Dictionary<string, string> m_DefaultHeaders =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingRetry> m_PendingRetries = new List<PendingRetry>();

        public HttpManager()
        {
        }

        /// <summary>测试用构造：直接注入 IHttpClient。</summary>
        internal HttpManager(IHttpClient client)
        {
            m_Client = client;
        }

        // Priority 0：业务模块，不参与依赖图排序。
        public override int Priority { get { return 0; } }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            float delta = realElapseSeconds > 0f ? realElapseSeconds : 0f;
            for (int i = m_PendingRetries.Count - 1; i >= 0; i--)
            {
                PendingRetry pending = m_PendingRetries[i];
                pending.RemainingSeconds -= delta;
                if (pending.RemainingSeconds > 0f) continue;

                m_PendingRetries.RemoveAt(i);
                SendWithRetry(pending.Request, pending.Attempt, pending.OnComplete);
            }
        }

        public override void Shutdown()
        {
            m_Client = null;
            m_DefaultHeaders.Clear();
            m_PendingRetries.Clear();
        }

        public string BaseUrl
        {
            get { return m_BaseUrl; }
            set { m_BaseUrl = value ?? string.Empty; }
        }

        public HttpRetryPolicy RetryPolicy
        {
            get { return m_RetryPolicy; }
            set { m_RetryPolicy = value; }
        }

        public void SetClient(IHttpClient client)
        {
            Framework.EnsureMainThread(nameof(SetClient));
            m_Client = client;
        }

        public void SetDefaultHeader(string key, string value)
        {
            Framework.EnsureMainThread(nameof(SetDefaultHeader));
            if (string.IsNullOrEmpty(key)) return;
            if (value == null) m_DefaultHeaders.Remove(key);
            else m_DefaultHeaders[key] = value;
        }

        public void Send(HttpRequest request, Action<HttpResponse> onComplete)
        {
            Framework.EnsureMainThread(nameof(Send));
            if (request == null)
            {
                onComplete?.Invoke(HttpResponse.NetworkError("HttpRequest is null."));
                return;
            }
            if (m_Client == null)
            {
                onComplete?.Invoke(HttpResponse.NetworkError("No IHttpClient set."));
                return;
            }

            PrepareRequest(request);
            SendWithRetry(request, attempt: 0, onComplete);
        }

        public void Get(string url, Action<HttpResponse> onComplete)
        {
            Send(HttpRequest.Get(url), onComplete);
        }

        public void PostJson(string url, string json, Action<HttpResponse> onComplete)
        {
            Send(HttpRequest.PostJson(url, json), onComplete);
        }

        // ===== 内部 =====

        // 一次性地把 BaseUrl 拼接进 URL 并合并默认头（请求自带的同名头不被覆盖）。
        // 仅在首发前调用一次；重试沿用同一个已处理的 request。
        private void PrepareRequest(HttpRequest request)
        {
            request.Url = CombineUrl(m_BaseUrl, request.Url);

            if (m_DefaultHeaders.Count > 0)
            {
                request.Headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in m_DefaultHeaders)
                {
                    // 请求自带的同名头优先：仅在缺失时填入默认头。
                    if (!request.Headers.ContainsKey(kv.Key))
                    {
                        request.Headers[kv.Key] = kv.Value;
                    }
                }
            }
        }

        // attempt: 当前是第几次重试（首发为 0）。
        private void SendWithRetry(HttpRequest request, int attempt, Action<HttpResponse> onComplete)
        {
            m_Client.Send(request, response =>
            {
                response ??= HttpResponse.NetworkError("IHttpClient returned a null response.");

                if (m_RetryPolicy.ShouldRetry(attempt, response) && CanRetryRequest(request))
                {
                    int next = attempt + 1;
                    float delay = m_RetryPolicy.NextDelaySeconds(attempt);
                    FrameworkLog.Warning(
                        "HTTP retry {0}/{1} for '{2}' in {3:0.###}s (status={4}, network={5}, timeout={6}).",
                        next, m_RetryPolicy.MaxRetries, request.Url, delay,
                        response.StatusCode, response.IsNetworkError, response.IsTimeout);
                    m_PendingRetries.Add(new PendingRetry
                    {
                        Request = request,
                        Attempt = next,
                        RemainingSeconds = delay,
                        OnComplete = onComplete,
                    });
                    return;
                }

                onComplete?.Invoke(response);
            });
        }

        private static bool CanRetryRequest(HttpRequest request)
        {
            if (request.RetryNonIdempotent) return true;
            string method = request.Method ?? HttpMethods.Get;
            return string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, HttpMethods.Put, StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, HttpMethods.Delete, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class PendingRetry
        {
            public HttpRequest Request;
            public int Attempt;
            public float RemainingSeconds;
            public Action<HttpResponse> OnComplete;
        }

        // 拼接 baseUrl 与 url：url 为绝对（含 "://"）或 baseUrl 为空时原样返回 url；否则规整斜杠后拼接。
        internal static string CombineUrl(string baseUrl, string url)
        {
            if (string.IsNullOrEmpty(url)) return baseUrl ?? string.Empty;
            if (string.IsNullOrEmpty(baseUrl)) return url;
            if (url.IndexOf("://", StringComparison.Ordinal) >= 0) return url;

            bool baseEndsSlash = baseUrl[baseUrl.Length - 1] == '/';
            bool urlStartsSlash = url[0] == '/';

            if (baseEndsSlash && urlStartsSlash) return baseUrl + url.Substring(1);
            if (!baseEndsSlash && !urlStartsSlash) return baseUrl + "/" + url;
            return baseUrl + url;
        }
    }
}
