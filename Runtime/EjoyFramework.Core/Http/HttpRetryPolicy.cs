//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Http
{
    /// <summary>
    /// HTTP 重试策略（纯值类型，无 Unity 依赖，便于单元测试）。
    ///
    /// 语义：
    ///   - 在<b>网络错误 / 超时 / 5xx 服务器错误</b>上重试；
    ///   - <b>不</b>在 4xx 客户端错误（如 400/401/404）上重试 —— 重试无意义；
    ///   - 退避序列与 <see cref="EjoyFramework.Core.Network.ReconnectBackoff"/> 一致：
    ///     base, base*2, base*4 ... 钳制到 <see cref="MaxDelaySeconds"/>；attempt 从 0 开始（attempt 0 == base）。
    /// </summary>
    public struct HttpRetryPolicy
    {
        /// <summary>最大重试次数（首发不计；&lt;=0 表示不重试）。</summary>
        public int MaxRetries;

        /// <summary>首次退避秒数（attempt 0）。</summary>
        public float BaseDelaySeconds;

        /// <summary>退避上限秒数。</summary>
        public float MaxDelaySeconds;

        /// <summary>默认策略：最多重试 3 次，0.5s 起，10s 封顶。</summary>
        public static HttpRetryPolicy Default => new HttpRetryPolicy
        {
            MaxRetries = 3,
            BaseDelaySeconds = 0.5f,
            MaxDelaySeconds = 10f,
        };

        /// <summary>不重试策略。</summary>
        public static HttpRetryPolicy None => new HttpRetryPolicy
        {
            MaxRetries = 0,
            BaseDelaySeconds = 0f,
            MaxDelaySeconds = 0f,
        };

        /// <summary>
        /// 判断在给定 <paramref name="attempt"/>（从 0 开始的“已尝试发送次数 - 1”，即下一次将是第 attempt 次重试）
        /// 与上一次响应 <paramref name="response"/> 下是否应当重试。
        ///
        /// 规则：
        ///   - attempt &gt;= MaxRetries → false（次数耗尽）；
        ///   - response 为 null → 视为可重试的瞬时失败 → true（在次数内）；
        ///   - 超时 / 网络错误 → true；
        ///   - 5xx → true；
        ///   - 其余（含 2xx 成功、4xx 客户端错误）→ false。
        /// </summary>
        public bool ShouldRetry(int attempt, HttpResponse response)
        {
            if (attempt < 0) attempt = 0;
            if (attempt >= MaxRetries) return false;

            // 没有响应对象：当作瞬时传输失败，允许在次数内重试。
            if (response == null) return true;

            // 成功不重试。
            if (response.IsSuccess) return false;

            // 超时 / 网络错误：重试。
            if (response.IsTimeout || response.IsNetworkError) return true;

            // 5xx 服务器错误：重试；4xx 等其余状态：不重试。
            return response.StatusCode >= 500 && response.StatusCode < 600;
        }

        /// <summary>
        /// 计算第 <paramref name="attempt"/> 次重试前的等待秒数（指数退避并钳制到 <see cref="MaxDelaySeconds"/>）。
        /// attempt 0 返回 <see cref="BaseDelaySeconds"/>；非法入参做安全钳制，永不抛异常、永不返回负值。
        /// </summary>
        public float NextDelaySeconds(int attempt)
        {
            if (attempt < 0) attempt = 0;
            float baseDelay = BaseDelaySeconds < 0f ? 0f : BaseDelaySeconds;
            float maxDelay = MaxDelaySeconds < baseDelay ? baseDelay : MaxDelaySeconds;

            // 2^attempt 用 double 计算避免溢出；attempt 较大时直接钳到 maxDelay。
            double factor = attempt >= 30 ? double.PositiveInfinity : (1L << attempt);
            double delay = baseDelay * factor;
            if (double.IsNaN(delay) || delay > maxDelay) delay = maxDelay;
            if (delay < 0d) delay = 0d;
            return (float)delay;
        }
    }
}
