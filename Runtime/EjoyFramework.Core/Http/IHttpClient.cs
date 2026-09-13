//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Http
{
    /// <summary>
    /// HTTP 客户端辅助器接口。由 Unity 层实现（封装 UnityWebRequest），负责真正的网络发送。
    ///
    /// 约定：
    ///   - <see cref="Send"/> 不阻塞调用线程；底层异步完成后在<b>主线程</b>回调 <paramref name="onComplete"/>。
    ///   - 实现方负责把底层结果映射为 <see cref="HttpResponse"/>（成功 / 协议错误 / 网络错误 / 超时）。
    ///   - 实现方负责释放底层请求资源。
    /// </summary>
    public interface IHttpClient
    {
        /// <summary>
        /// 发送一个请求；完成（成功或失败）后在主线程回调 <paramref name="onComplete"/>，回调参数非 null。
        /// </summary>
        void Send(HttpRequest request, Action<HttpResponse> onComplete);
    }
}
