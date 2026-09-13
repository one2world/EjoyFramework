//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Text;

namespace EjoyFramework.GamePlay.Experiments
{
    /// <summary>
    /// 平台确定性（platform-deterministic）的稳定字符串哈希工具，专为 A/B 实验分桶设计。
    ///
    /// 设计要点：
    /// - 采用标准 32 位 FNV-1a 算法（offset basis = 2166136261，prime = 16777619），对字符串的 UTF-8 字节流逐字节混合。
    ///   该算法不含随机种子，跨进程、跨设备、跨平台对同一输入恒定产出相同结果——这是稳定分桶（同一用户恒落同一变体）的前提。
    /// - 刻意不使用 <see cref="string.GetHashCode()"/>：.NET 出于安全考虑会对其做按进程随机化（randomized per process），
    ///   同一字符串在不同进程返回不同值，无法用于跨运行/跨设备稳定分桶。
    /// - <see cref="Normalized(string,string)"/> 把两段输入拼接为 "a:b" 后哈希，并线性映射到 [0,1) 区间，
    ///   供分桶时把哈希值落到累积权重带（cumulative weight band）。
    ///
    /// 纯逻辑、与引擎无关（仅依赖 System.*）。无状态，线程安全。
    /// </summary>
    public static class StableHash
    {
        /// <summary>
        /// FNV-1a 32 位偏移基（offset basis）。
        /// </summary>
        private const uint Fnv32OffsetBasis = 2166136261u;

        /// <summary>
        /// FNV-1a 32 位质数（prime）。
        /// </summary>
        private const uint Fnv32Prime = 16777619u;

        /// <summary>
        /// 32 位 uint 取值空间大小（2^32），用于把哈希线性映射到 [0,1)。
        /// 以 double 常量保存，避免每次调用重复计算且确保精度。
        /// </summary>
        private const double UInt32Range = 4294967296.0;

        /// <summary>
        /// 计算字符串的 32 位 FNV-1a 哈希（对 UTF-8 字节逐字节混合）。
        /// 跨平台确定性：同一输入恒返回同一值，且与 <see cref="string.GetHashCode()"/> 的进程随机化无关。
        /// 约定 <c>Fnv1a("") == 2166136261</c>（仅返回偏移基），<c>Fnv1a("a") == 0xE40C292C</c>。
        /// </summary>
        /// <param name="s">待哈希字符串；为 null 时按空串处理（返回偏移基）。</param>
        /// <returns>32 位无符号哈希值。</returns>
        public static uint Fnv1a(string s)
        {
            uint hash = Fnv32OffsetBasis;

            if (string.IsNullOrEmpty(s))
            {
                return hash;
            }

            // 显式按 UTF-8 编码取字节，确保非 ASCII 字符在所有平台产出一致字节序列。
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= Fnv32Prime;
            }

            return hash;
        }

        /// <summary>
        /// 将两段输入拼接为 <c>a + ":" + b</c> 后做 FNV-1a，并线性映射到 [0,1) 区间。
        /// 用作分桶概率 p：<c>p = Fnv1a(a + ":" + b) / 2^32</c>，恒落 [0,1)（uint 最大值 2^32-1 映射后严格小于 1）。
        /// </summary>
        /// <param name="a">第一段输入（如 userId）；null 按空串处理。</param>
        /// <param name="b">第二段输入（如 experimentId）；null 按空串处理。</param>
        /// <returns>[0,1) 区间内的 double。</returns>
        public static double Normalized(string a, string b)
        {
            string combined = string.Concat(a ?? string.Empty, ":", b ?? string.Empty);
            uint hash = Fnv1a(combined);
            return hash / UInt32Range;
        }
    }
}
