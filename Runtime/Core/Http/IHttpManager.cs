//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Http
{
    /// <summary>
    /// HTTP 管理器接口。
    /// 负责：注入底层客户端（<see cref="IHttpClient"/>）；统一拼接 BaseUrl、合并默认请求头；
    /// 按 <see cref="RetryPolicy"/> 在可重试失败上自动重发。
    ///
    /// 业务层一般通过 Unity 层的 HttpComponent 间接使用本接口。
    /// </summary>
    public interface IHttpManager
    {
        /// <summary>注入底层 HTTP 客户端（通常是 Unity 层的 UnityWebRequestHttpClient）。</summary>
        void SetClient(IHttpClient client);

        /// <summary>
        /// 基础 URL。请求 URL 为相对路径时自动前置拼接；为绝对 URL（含 "://"）时原样使用。
        /// </summary>
        string BaseUrl { get; set; }

        /// <summary>
        /// 重试策略（默认 <see cref="HttpRetryPolicy.Default"/>）。退避由模块 Update 推进；
        /// POST 等非幂等请求默认不重试，除非请求显式设置 <see cref="HttpRequest.RetryNonIdempotent"/>。
        /// </summary>
        HttpRetryPolicy RetryPolicy { get; set; }

        /// <summary>
        /// 设置一个默认请求头；发送时合并进每个请求（请求自带的同名头优先，不被覆盖）。
        /// value 为 null 表示移除该默认头。
        /// </summary>
        void SetDefaultHeader(string key, string value);

        /// <summary>
        /// 发送请求：应用 BaseUrl 与默认头后交给客户端；可重试失败按 <see cref="RetryPolicy"/> 延迟重试。
        /// 完成（含重试耗尽）后在主线程回调 <paramref name="onComplete"/>。未设置客户端时回调一个网络错误响应。
        /// </summary>
        void Send(HttpRequest request, Action<HttpResponse> onComplete);

        /// <summary>便捷方法：发起 GET。</summary>
        void Get(string url, Action<HttpResponse> onComplete);

        /// <summary>便捷方法：发起 JSON POST。默认不自动重试。</summary>
        void PostJson(string url, string json, Action<HttpResponse> onComplete);
    }
}
