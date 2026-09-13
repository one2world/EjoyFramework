//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace EjoyFramework.Core.Http
{
    /// <summary>
    /// HTTP 响应结果（纯数据，无 Unity 依赖）。
    /// 由 <see cref="IHttpClient"/> 映射底层结果后回调给上层。
    /// </summary>
    public sealed class HttpResponse
    {
        /// <summary>是否成功（2xx 状态码且无传输错误）。</summary>
        public bool IsSuccess;

        /// <summary>HTTP 状态码（传输失败时通常为 0）。</summary>
        public int StatusCode;

        /// <summary>响应体字节（可能为 null）。</summary>
        public byte[] Body;

        /// <summary>传输错误描述（成功时为 null）。</summary>
        public string Error;

        /// <summary>是否为网络/连接错误（DNS 失败、连接拒绝、断网等）。</summary>
        public bool IsNetworkError;

        /// <summary>是否为超时。</summary>
        public bool IsTimeout;

        /// <summary>响应头集合（可能为空）。</summary>
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 响应体的 UTF8 文本视图。每次访问按需解码；Body 为 null/空时返回空串。
        /// </summary>
        public string Text
        {
            get
            {
                if (Body == null || Body.Length == 0) return string.Empty;
                try { return Encoding.UTF8.GetString(Body); }
                catch { return string.Empty; }
            }
        }

        /// <summary>
        /// 构造一个成功响应（IsSuccess 由状态码自动判定为 2xx）。
        /// </summary>
        public static HttpResponse Success(int statusCode, byte[] body, Dictionary<string, string> headers = null)
        {
            return new HttpResponse
            {
                StatusCode = statusCode,
                IsSuccess = statusCode >= 200 && statusCode < 300,
                Body = body,
                Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Error = null,
                IsNetworkError = false,
                IsTimeout = false,
            };
        }

        /// <summary>
        /// 构造一个收到了 HTTP 响应但状态码非 2xx 的结果（如 404 / 500）。
        /// </summary>
        public static HttpResponse Protocol(int statusCode, byte[] body, string error, Dictionary<string, string> headers = null)
        {
            return new HttpResponse
            {
                StatusCode = statusCode,
                IsSuccess = statusCode >= 200 && statusCode < 300,
                Body = body,
                Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Error = error,
                IsNetworkError = false,
                IsTimeout = false,
            };
        }

        /// <summary>
        /// 构造一个网络/连接错误结果（无 HTTP 状态）。
        /// </summary>
        public static HttpResponse NetworkError(string error)
        {
            return new HttpResponse
            {
                StatusCode = 0,
                IsSuccess = false,
                Body = null,
                Error = error,
                IsNetworkError = true,
                IsTimeout = false,
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };
        }

        /// <summary>
        /// 构造一个超时结果。
        /// </summary>
        public static HttpResponse Timeout(string error)
        {
            return new HttpResponse
            {
                StatusCode = 0,
                IsSuccess = false,
                Body = null,
                Error = error,
                IsNetworkError = true,
                IsTimeout = true,
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };
        }
    }
}
