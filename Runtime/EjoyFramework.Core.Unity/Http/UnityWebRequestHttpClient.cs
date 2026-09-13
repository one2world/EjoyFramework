//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using EjoyFramework.Core.Coroutines;
using EjoyFramework.Core.Http;
using UnityEngine.Networking;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 基于 <see cref="UnityWebRequest"/> 的 <see cref="IHttpClient"/> 实现。
    ///
    /// 由框架的 <see cref="ICoroutineManager"/> 驱动协程跑请求，因此 onComplete 必然在<b>主线程</b>回调
    /// （Unity 协程恢复点都在主线程 PlayerLoop 上）。完成后映射 <see cref="UnityWebRequest.result"/> →
    /// <see cref="HttpResponse"/>，并 Dispose 底层请求。
    ///
    /// 命名空间提示：本文件位于 EjoyFramework.Core.Unity，与 core 的 EjoyFramework.Core.Http 是平级命名空间；
    /// UnityEngine 不存在名为 Http 的类型，无遮蔽风险。UnityWebRequest 等类型通过显式 using 引入。
    /// </summary>
    public sealed class UnityWebRequestHttpClient : IHttpClient
    {
        private readonly ICoroutineManager m_Coroutines;

        /// <summary>用显式协程管理器构造（测试 / 自定义注入）。</summary>
        public UnityWebRequestHttpClient(ICoroutineManager coroutineManager)
        {
            m_Coroutines = coroutineManager ?? throw new ArgumentNullException(nameof(coroutineManager));
        }

        /// <summary>从框架解析 <see cref="ICoroutineManager"/> 构造。</summary>
        public UnityWebRequestHttpClient()
            : this(Framework.GetModule<ICoroutineManager>())
        {
        }

        public void Send(HttpRequest request, Action<HttpResponse> onComplete)
        {
            if (request == null)
            {
                onComplete?.Invoke(HttpResponse.NetworkError("HttpRequest is null."));
                return;
            }
            if (m_Coroutines == null || !m_Coroutines.HasHelper)
            {
                onComplete?.Invoke(HttpResponse.NetworkError("Coroutine manager/helper is not ready for HTTP."));
                return;
            }

            m_Coroutines.Run(SendCoroutine(request, onComplete), tag: "Http/" + request.Method + "/" + request.Url, owner: this);
        }

        private IEnumerator SendCoroutine(HttpRequest request, Action<HttpResponse> onComplete)
        {
            UnityWebRequest www = null;
            HttpResponse failFast = null;
            try
            {
                www = BuildRequest(request);
            }
            catch (Exception ex)
            {
                failFast = HttpResponse.NetworkError("Failed to build UnityWebRequest: " + ex.Message);
            }

            if (failFast != null)
            {
                onComplete?.Invoke(failFast);
                yield break;
            }

            UnityWebRequestAsyncOperation op;
            try
            {
                op = www.SendWebRequest();
            }
            catch (Exception ex)
            {
                www.Dispose();
                onComplete?.Invoke(HttpResponse.NetworkError("UnityWebRequest.SendWebRequest threw: " + ex.Message));
                yield break;
            }

            while (!op.isDone) yield return null;

            HttpResponse response;
            try
            {
                response = MapResponse(www);
            }
            catch (Exception ex)
            {
                response = HttpResponse.NetworkError("Failed to map UnityWebRequest result: " + ex.Message);
            }
            finally
            {
                www.Dispose();
            }

            onComplete?.Invoke(response);
        }

        // 构造并配置 UnityWebRequest：方法、URL、超时、上传/下载 handler、请求头。
        private static UnityWebRequest BuildRequest(HttpRequest request)
        {
            string method = string.IsNullOrEmpty(request.Method) ? HttpMethods.Get : request.Method.ToUpperInvariant();
            var www = new UnityWebRequest(request.Url, method);
            www.downloadHandler = new DownloadHandlerBuffer();

            if (request.Body != null && request.Body.Length > 0)
            {
                www.uploadHandler = new UploadHandlerRaw(request.Body);
            }

            if (request.TimeoutSeconds > 0)
            {
                www.timeout = request.TimeoutSeconds;
            }

            if (request.Headers != null)
            {
                foreach (var kv in request.Headers)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    try { www.SetRequestHeader(kv.Key, kv.Value); }
                    catch (Exception ex) { FrameworkLog.Warning("SetRequestHeader('{0}') threw: {1}", kv.Key, ex.Message); }
                }
            }

            return www;
        }

        // 映射 UnityWebRequest.result → HttpResponse。
        private static HttpResponse MapResponse(UnityWebRequest www)
        {
            int statusCode = (int)www.responseCode;
            byte[] body = www.downloadHandler != null ? www.downloadHandler.data : null;
            Dictionary<string, string> headers = ExtractHeaders(www);

            switch (www.result)
            {
                case UnityWebRequest.Result.Success:
                    return new HttpResponse
                    {
                        StatusCode = statusCode,
                        IsSuccess = statusCode >= 200 && statusCode < 300,
                        Body = body,
                        Headers = headers,
                        Error = null,
                        IsNetworkError = false,
                        IsTimeout = false,
                    };

                case UnityWebRequest.Result.ProtocolError:
                    // 收到了 HTTP 响应但状态码 >= 400。保留 body / 状态码以便上层判断与重试（5xx）。
                    return new HttpResponse
                    {
                        StatusCode = statusCode,
                        IsSuccess = false,
                        Body = body,
                        Headers = headers,
                        Error = www.error,
                        IsNetworkError = false,
                        IsTimeout = false,
                    };

                case UnityWebRequest.Result.ConnectionError:
                {
                    bool timeout = IsTimeoutError(www.error);
                    var r = timeout
                        ? HttpResponse.Timeout(www.error)
                        : HttpResponse.NetworkError(www.error);
                    r.Headers = headers;
                    return r;
                }

                case UnityWebRequest.Result.DataProcessingError:
                default:
                {
                    var r = HttpResponse.NetworkError(www.error ?? "Unknown UnityWebRequest error.");
                    r.StatusCode = statusCode;
                    r.Body = body;
                    r.Headers = headers;
                    return r;
                }
            }
        }

        private static Dictionary<string, string> ExtractHeaders(UnityWebRequest www)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var raw = www.GetResponseHeaders();
                if (raw != null)
                {
                    foreach (var kv in raw) dict[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                FrameworkLog.Warning("GetResponseHeaders threw: {0}", ex.Message);
            }
            return dict;
        }

        // UnityWebRequest 在超时时 result=ConnectionError 且 error 文本含 "timeout"（各平台措辞略异）。
        private static bool IsTimeoutError(string error)
        {
            return !string.IsNullOrEmpty(error) &&
                   error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
