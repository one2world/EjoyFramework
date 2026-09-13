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
    /// HTTP 请求描述（纯数据，无 Unity 依赖）。
    /// 由 <see cref="IHttpClient"/> 实际发送；<see cref="IHttpManager"/> 在发送前合并 BaseUrl 与默认头。
    /// </summary>
    public sealed class HttpRequest
    {
        /// <summary>HTTP 方法（GET / POST / PUT / DELETE）。默认 GET。</summary>
        public string Method = HttpMethods.Get;

        /// <summary>请求 URL。可为相对路径，Manager 会拼接 BaseUrl。</summary>
        public string Url;

        /// <summary>请求头集合（大小写按底层实现处理，通常不区分大小写）。</summary>
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>请求体字节（GET 通常为 null）。</summary>
        public byte[] Body;

        /// <summary>超时时间（秒）。&lt;=0 由底层使用默认值。默认 15 秒。</summary>
        public int TimeoutSeconds = 15;

        /// <summary>
        /// 是否允许 POST 等非幂等方法自动重试。默认 false，避免网络失败后重复下单/扣费。
        /// GET/PUT/DELETE 等幂等方法无需设置此字段。
        /// </summary>
        public bool RetryNonIdempotent;

        /// <summary>
        /// 设置或覆盖一个请求头。key/value 为空时静默忽略，永不抛异常。
        /// </summary>
        public HttpRequest SetHeader(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return this;
            Headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Headers[key] = value;
            return this;
        }

        /// <summary>
        /// 构造一个 GET 请求。
        /// </summary>
        public static HttpRequest Get(string url)
        {
            return new HttpRequest { Method = HttpMethods.Get, Url = url };
        }

        /// <summary>
        /// 构造一个携带二进制体的 POST 请求，并设置 Content-Type。
        /// </summary>
        public static HttpRequest Post(string url, byte[] body, string contentType)
        {
            var req = new HttpRequest { Method = HttpMethods.Post, Url = url, Body = body };
            if (!string.IsNullOrEmpty(contentType)) req.SetHeader("Content-Type", contentType);
            return req;
        }

        /// <summary>
        /// 构造一个 JSON POST 请求（UTF8 编码体，Content-Type=application/json）。
        /// </summary>
        public static HttpRequest PostJson(string url, string json)
        {
            byte[] body = string.IsNullOrEmpty(json) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(json);
            return Post(url, body, "application/json");
        }
    }

    /// <summary>
    /// HTTP 方法常量。避免散落的魔法字符串。
    /// </summary>
    public static class HttpMethods
    {
        public const string Get = "GET";
        public const string Post = "POST";
        public const string Put = "PUT";
        public const string Delete = "DELETE";
    }
}
