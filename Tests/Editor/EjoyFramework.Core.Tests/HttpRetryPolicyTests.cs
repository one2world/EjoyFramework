//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Http;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// HttpRetryPolicy 纯逻辑单测：ShouldRetry 的状态分类（5xx/超时/网络错误 vs 2xx/4xx），
    /// 次数封顶，以及 NextDelaySeconds 的指数退避与钳制。
    /// </summary>
    public class HttpRetryPolicyTests
    {
        private static HttpRetryPolicy Policy(int maxRetries, float baseDelay = 0.5f, float maxDelay = 10f)
        {
            return new HttpRetryPolicy { MaxRetries = maxRetries, BaseDelaySeconds = baseDelay, MaxDelaySeconds = maxDelay };
        }

        // ===== ShouldRetry：状态分类 =====

        [Test]
        public void ShouldRetry_ServerError500_True()
        {
            var p = Policy(3);
            var resp = HttpResponse.Protocol(500, null, "server error");
            Assert.IsTrue(p.ShouldRetry(0, resp));
        }

        [Test]
        public void ShouldRetry_ServerError503_True()
        {
            var p = Policy(3);
            var resp = HttpResponse.Protocol(503, null, "unavailable");
            Assert.IsTrue(p.ShouldRetry(0, resp));
        }

        [Test]
        public void ShouldRetry_Timeout_True()
        {
            var p = Policy(3);
            var resp = HttpResponse.Timeout("timed out");
            Assert.IsTrue(p.ShouldRetry(0, resp));
        }

        [Test]
        public void ShouldRetry_NetworkError_True()
        {
            var p = Policy(3);
            var resp = HttpResponse.NetworkError("connection refused");
            Assert.IsTrue(p.ShouldRetry(0, resp));
        }

        [Test]
        public void ShouldRetry_NullResponse_TreatedAsTransientFailure_True()
        {
            var p = Policy(3);
            Assert.IsTrue(p.ShouldRetry(0, null));
        }

        [Test]
        public void ShouldRetry_Success200_False()
        {
            var p = Policy(3);
            var resp = HttpResponse.Success(200, null);
            Assert.IsFalse(p.ShouldRetry(0, resp));
        }

        [Test]
        public void ShouldRetry_ClientError404_False()
        {
            var p = Policy(3);
            var resp = HttpResponse.Protocol(404, null, "not found");
            Assert.IsFalse(p.ShouldRetry(0, resp));
        }

        [Test]
        public void ShouldRetry_ClientError400_False()
        {
            var p = Policy(3);
            var resp = HttpResponse.Protocol(400, null, "bad request");
            Assert.IsFalse(p.ShouldRetry(0, resp));
        }

        [Test]
        public void ShouldRetry_Unauthorized401_False()
        {
            var p = Policy(3);
            var resp = HttpResponse.Protocol(401, null, "unauthorized");
            Assert.IsFalse(p.ShouldRetry(0, resp));
        }

        // ===== ShouldRetry：次数封顶 =====

        [Test]
        public void ShouldRetry_AtMaxRetries_False()
        {
            var p = Policy(3);
            var resp = HttpResponse.Protocol(500, null, "server error");
            // attempt 0,1,2 可重试；attempt == MaxRetries(3) 起耗尽。
            Assert.IsTrue(p.ShouldRetry(2, resp));
            Assert.IsFalse(p.ShouldRetry(3, resp));
            Assert.IsFalse(p.ShouldRetry(4, resp));
        }

        [Test]
        public void ShouldRetry_ZeroMaxRetries_NeverRetries()
        {
            var p = Policy(0);
            var resp = HttpResponse.Timeout("timed out");
            Assert.IsFalse(p.ShouldRetry(0, resp));
        }

        [Test]
        public void ShouldRetry_NegativeAttempt_ClampedToZero()
        {
            var p = Policy(3);
            var resp = HttpResponse.Protocol(500, null, "server error");
            Assert.IsTrue(p.ShouldRetry(-5, resp));
        }

        // ===== NextDelaySeconds：指数退避 + 钳制 =====

        [Test]
        public void NextDelaySeconds_Attempt0_ReturnsBase()
        {
            var p = Policy(5, baseDelay: 0.5f, maxDelay: 100f);
            Assert.AreEqual(0.5f, p.NextDelaySeconds(0), 1e-4f);
        }

        [Test]
        public void NextDelaySeconds_Exponential()
        {
            var p = Policy(5, baseDelay: 1f, maxDelay: 1000f);
            Assert.AreEqual(1f, p.NextDelaySeconds(0), 1e-4f);
            Assert.AreEqual(2f, p.NextDelaySeconds(1), 1e-4f);
            Assert.AreEqual(4f, p.NextDelaySeconds(2), 1e-4f);
            Assert.AreEqual(8f, p.NextDelaySeconds(3), 1e-4f);
        }

        [Test]
        public void NextDelaySeconds_ClampedToMax()
        {
            var p = Policy(20, baseDelay: 1f, maxDelay: 5f);
            // 1, 2, 4, then clamp at 5.
            Assert.AreEqual(5f, p.NextDelaySeconds(3), 1e-4f);   // raw 8 -> clamp 5
            Assert.AreEqual(5f, p.NextDelaySeconds(10), 1e-4f);
        }

        [Test]
        public void NextDelaySeconds_LargeAttempt_NoOverflow_ClampedToMax()
        {
            var p = Policy(100, baseDelay: 0.5f, maxDelay: 30f);
            Assert.AreEqual(30f, p.NextDelaySeconds(40), 1e-4f);
            Assert.AreEqual(30f, p.NextDelaySeconds(1000), 1e-4f);
        }

        [Test]
        public void NextDelaySeconds_NegativeInputs_Safe()
        {
            var p = new HttpRetryPolicy { MaxRetries = 3, BaseDelaySeconds = -1f, MaxDelaySeconds = -5f };
            float d = p.NextDelaySeconds(-3);
            Assert.GreaterOrEqual(d, 0f);
        }
    }
}
