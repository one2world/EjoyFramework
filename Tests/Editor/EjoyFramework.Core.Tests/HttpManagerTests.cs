//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Http;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// HttpManager 单测：用一个可编程的 fake IHttpClient（同步回调）验证
    /// BaseUrl 前置、默认头合并（请求自带优先）、以及“失败重试后成功”的编排。
    /// 不依赖 Unity / 网络。
    /// </summary>
    public class HttpManagerTests
    {
        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        /// <summary>
        /// 可编程 fake：按预置的脚本依次返回响应；记录每次实际发送的请求（URL/头快照）。
        /// 同步回调，方便断言。
        /// </summary>
        private sealed class FakeHttpClient : IHttpClient
        {
            private readonly Queue<HttpResponse> m_Scripted = new Queue<HttpResponse>();
            public readonly List<string> SentUrls = new List<string>();
            public readonly List<Dictionary<string, string>> SentHeaders = new List<Dictionary<string, string>>();
            public int SendCount;

            public void Enqueue(HttpResponse response) { m_Scripted.Enqueue(response); }

            public void Send(HttpRequest request, Action<HttpResponse> onComplete)
            {
                SendCount++;
                SentUrls.Add(request.Url);
                SentHeaders.Add(request.Headers != null
                    ? new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

                HttpResponse resp = m_Scripted.Count > 0 ? m_Scripted.Dequeue() : HttpResponse.Success(200, null);
                onComplete?.Invoke(resp);
            }
        }

        // ===== BaseUrl 前置 =====

        [Test]
        public void Send_PrependsBaseUrl_ForRelativePath()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake) { BaseUrl = "https://api.example.com" };

            mgr.Get("/v1/ping", _ => { });

            Assert.AreEqual(1, fake.SendCount);
            Assert.AreEqual("https://api.example.com/v1/ping", fake.SentUrls[0]);
        }

        [Test]
        public void Send_HandlesTrailingAndLeadingSlashes_NoDoubleSlash()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake) { BaseUrl = "https://api.example.com/" };

            mgr.Get("/v1/ping", _ => { });

            Assert.AreEqual("https://api.example.com/v1/ping", fake.SentUrls[0]);
        }

        [Test]
        public void Send_AbsoluteUrl_NotPrefixed()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake) { BaseUrl = "https://api.example.com" };

            mgr.Get("https://other.host/path", _ => { });

            Assert.AreEqual("https://other.host/path", fake.SentUrls[0]);
        }

        // ===== 默认头合并 =====

        [Test]
        public void Send_MergesDefaultHeaders()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake);
            mgr.SetDefaultHeader("Authorization", "Bearer token123");
            mgr.SetDefaultHeader("X-App", "ejoy");

            mgr.Get("https://api.example.com/me", _ => { });

            var headers = fake.SentHeaders[0];
            Assert.AreEqual("Bearer token123", headers["Authorization"]);
            Assert.AreEqual("ejoy", headers["X-App"]);
        }

        [Test]
        public void Send_RequestHeader_TakesPrecedenceOverDefault()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake);
            mgr.SetDefaultHeader("X-App", "default-app");

            var req = HttpRequest.Get("https://api.example.com/me");
            req.SetHeader("X-App", "request-app");
            mgr.Send(req, _ => { });

            Assert.AreEqual("request-app", fake.SentHeaders[0]["X-App"]);
        }

        [Test]
        public void SetDefaultHeader_NullValue_RemovesHeader()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake);
            mgr.SetDefaultHeader("X-App", "ejoy");
            mgr.SetDefaultHeader("X-App", null);

            mgr.Get("https://api.example.com/me", _ => { });

            Assert.IsFalse(fake.SentHeaders[0].ContainsKey("X-App"));
        }

        // ===== 重试编排 =====

        [Test]
        public void Send_RetriesOn500_ThenSucceeds()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Protocol(500, null, "server error"));   // 首发失败
            fake.Enqueue(HttpResponse.Protocol(503, null, "unavailable"));    // 第一次重试失败
            fake.Enqueue(HttpResponse.Success(200, null));                    // 第二次重试成功

            var mgr = new HttpManager(fake)
            {
                RetryPolicy = new HttpRetryPolicy { MaxRetries = 3, BaseDelaySeconds = 0f, MaxDelaySeconds = 0f },
            };

            HttpResponse final = null;
            mgr.Get("https://api.example.com/data", r => final = r);
            mgr.Update(0f, 0f);
            mgr.Update(0f, 0f);

            Assert.AreEqual(3, fake.SendCount, "应发送 1 次 + 重试 2 次");
            Assert.IsNotNull(final);
            Assert.IsTrue(final.IsSuccess);
            Assert.AreEqual(200, final.StatusCode);
        }

        [Test]
        public void Send_RetryExhausted_ReturnsLastFailure()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Protocol(500, null, "e1"));
            fake.Enqueue(HttpResponse.Protocol(500, null, "e2"));
            fake.Enqueue(HttpResponse.Protocol(500, null, "e3"));   // MaxRetries=2 → 总共 3 次发送后停

            var mgr = new HttpManager(fake)
            {
                RetryPolicy = new HttpRetryPolicy { MaxRetries = 2, BaseDelaySeconds = 0f, MaxDelaySeconds = 0f },
            };

            HttpResponse final = null;
            mgr.Get("https://api.example.com/data", r => final = r);
            mgr.Update(0f, 0f);
            mgr.Update(0f, 0f);

            Assert.AreEqual(3, fake.SendCount, "1 次首发 + 2 次重试");
            Assert.IsNotNull(final);
            Assert.IsFalse(final.IsSuccess);
            Assert.AreEqual(500, final.StatusCode);
        }

        [Test]
        public void Send_NoRetryOn404()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Protocol(404, null, "not found"));

            var mgr = new HttpManager(fake)
            {
                RetryPolicy = new HttpRetryPolicy { MaxRetries = 3, BaseDelaySeconds = 0f, MaxDelaySeconds = 0f },
            };

            HttpResponse final = null;
            mgr.Get("https://api.example.com/missing", r => final = r);

            Assert.AreEqual(1, fake.SendCount, "4xx 不重试");
            Assert.AreEqual(404, final.StatusCode);
        }

        [Test]
        public void Send_RetryWaitsForConfiguredBackoff()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Protocol(503, null, "unavailable"));
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake)
            {
                RetryPolicy = new HttpRetryPolicy
                {
                    MaxRetries = 1,
                    BaseDelaySeconds = 0.5f,
                    MaxDelaySeconds = 0.5f,
                },
            };

            HttpResponse final = null;
            mgr.Get("https://api.example.com/data", response => final = response);

            Assert.AreEqual(1, fake.SendCount, "首个失败回调内不能递归重发。 ");
            Assert.IsNull(final, "等待退避期间请求尚未完成。 ");

            mgr.Update(0f, 0.49f);
            Assert.AreEqual(1, fake.SendCount);
            mgr.Update(0f, 0.01f);

            Assert.AreEqual(2, fake.SendCount);
            Assert.IsNotNull(final);
            Assert.IsTrue(final.IsSuccess);
        }

        [Test]
        public void Send_PostDoesNotRetryByDefault()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Protocol(503, null, "unavailable"));
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake)
            {
                RetryPolicy = new HttpRetryPolicy
                {
                    MaxRetries = 3,
                    BaseDelaySeconds = 0f,
                    MaxDelaySeconds = 0f,
                },
            };

            HttpResponse final = null;
            mgr.PostJson("https://api.example.com/order", "{}", response => final = response);
            mgr.Update(0f, 1f);

            Assert.AreEqual(1, fake.SendCount,
                "POST 默认不具备幂等性，不能因瞬时错误自动重复下单。 ");
            Assert.IsNotNull(final);
            Assert.AreEqual(503, final.StatusCode);
        }

        [Test]
        public void Send_PostRetriesOnlyWhenExplicitlyOptedIn()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Protocol(503, null, "unavailable"));
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake)
            {
                RetryPolicy = new HttpRetryPolicy
                {
                    MaxRetries = 1,
                    BaseDelaySeconds = 0f,
                    MaxDelaySeconds = 0f,
                },
            };
            HttpRequest request = HttpRequest.PostJson("https://api.example.com/idempotent-command", "{}");
            request.RetryNonIdempotent = true;

            HttpResponse final = null;
            mgr.Send(request, response => final = response);
            Assert.AreEqual(1, fake.SendCount);
            mgr.Update(0f, 0f);

            Assert.AreEqual(2, fake.SendCount);
            Assert.IsNotNull(final);
            Assert.IsTrue(final.IsSuccess);
        }

        [Test]
        public void Send_NoClient_ReturnsNetworkError()
        {
            var mgr = new HttpManager((IHttpClient)null);

            HttpResponse final = null;
            mgr.Get("https://api.example.com/x", r => final = r);

            Assert.IsNotNull(final);
            Assert.IsTrue(final.IsNetworkError);
            Assert.AreEqual("No IHttpClient set.", final.Error);
        }

        [Test]
        public void PostJson_SetsJsonBodyAndContentType()
        {
            var fake = new FakeHttpClient();
            fake.Enqueue(HttpResponse.Success(200, null));
            var mgr = new HttpManager(fake) { BaseUrl = "https://api.example.com" };

            // HttpRequest.PostJson 工厂：方法 / Content-Type / UTF8 体。
            var req = HttpRequest.PostJson("https://api.example.com/submit", "{\"a\":1}");
            Assert.AreEqual(HttpMethods.Post, req.Method);
            Assert.AreEqual("application/json", req.Headers["Content-Type"]);
            Assert.AreEqual("{\"a\":1}", System.Text.Encoding.UTF8.GetString(req.Body));

            mgr.PostJson("/submit", "{\"a\":1}", _ => { });
            Assert.AreEqual("https://api.example.com/submit", fake.SentUrls[0]);
        }
    }
}
