//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

#if UNITY_2023_1_OR_NEWER   // UnityEngine.Awaitable 自 2023.1 起可用
using System;
using System.Threading;
using EjoyFramework.Core.Http;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 异步现代化：把 <see cref="IHttpManager"/> 的回调式发送桥接为可 <c>await</c> 的 Unity
    /// <see cref="Awaitable{HttpResponse}"/>，并支持 <see cref="CancellationToken"/>。
    ///
    /// 设计（对齐 AwaitableExtensions）：
    ///   - <b>附加式、不破坏</b>：纯扩展方法，不改任何 core 接口；回调 API 原样保留。
    ///   - <b>取消</b>：token 取消时立即把 Awaitable 置为 canceled（HTTP 发送已在途的请求由底层在完成后被忽略）。
    ///   - <b>主线程恢复</b>：底层 onComplete 在主线程回调，Awaitable 在主线程 PlayerLoop 上恢复。
    ///
    /// 用法：
    /// <code>
    /// HttpResponse res = await httpManager.SendAsync(HttpRequest.Get("/ping"), ct);
    /// if (res.IsSuccess) { var text = res.Text; }
    /// </code>
    /// </summary>
    public static class HttpAwaitableExtensions
    {
        /// <summary>
        /// await 风格发送 HTTP 请求。返回的 <see cref="HttpResponse"/> 非 null；传输失败也以响应形式返回（IsSuccess=false）。
        /// token 取消时 Awaitable 进入 canceled 状态。
        /// </summary>
        public static Awaitable<HttpResponse> SendAsync(this IHttpManager manager, HttpRequest request,
            CancellationToken cancellationToken = default)
        {
            if (manager == null) throw new FrameworkException("Http manager is null.");
            if (request == null) throw new FrameworkException("Http request is null.");

            var source = new AwaitableCompletionSource<HttpResponse>();
            CancellationTokenRegistration registration = default;   // default 上 Dispose 为 no-op

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() => source.TrySetCanceled());
            }

            manager.Send(request, response =>
            {
                registration.Dispose();
                source.TrySetResult(response ?? HttpResponse.NetworkError("Null HttpResponse."));
            });

            return source.Awaitable;
        }
    }
}
#endif
