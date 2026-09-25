//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// ConfigBlob 使用的 FNV1a-64 哈希。表名 → nameHash 的唯一实现，构建期（codegen）与运行期必须共用本类，
    /// 任何一侧自行实现都会导致查表失败。
    ///
    /// 设计理由：
    ///   • 表名在 blob 中不落字符串，只存 64 位哈希，查表因此是纯整数比较，不触碰字符串内存。
    ///   • FNV1a-64 无外部依赖、实现极短、字节序无关（逐字节处理 UTF-8），跨平台结果一致；
    ///     碰撞概率对"一个项目几百张表"的规模完全可忽略，且 <see cref="ConfigBlobWriter"/> 在构建期
    ///     显式检测同哈希不同名，真出现碰撞会构建失败而不是静默串表。
    ///
    /// 线程契约：纯函数，任意线程安全。
    /// </summary>
    public static class ConfigBlobHash
    {
        private const ulong Fnv1a64Offset = 14695981039346656037UL;
        private const ulong Fnv1a64Prime = 1099511628211UL;

        /// <summary>
        /// 计算字符串的 FNV1a-64 哈希（按 UTF-8 字节逐字节计算）。
        /// </summary>
        /// <param name="value">待哈希的字符串，不可为 null。</param>
        /// <returns>64 位哈希值。</returns>
        public static ulong Compute(string value)
        {
            if (value == null)
            {
                throw new FrameworkException("ConfigBlobHash.Compute：待哈希字符串不能为 null。");
            }

            // 与 StringHash.Compute64 是同一个函数（FNV1a-64 over UTF-8，就地编码非 ASCII，零分配）。
            return StringHash.Compute64(value.AsSpan());
        }

        /// <summary>
        /// 计算字节序列的 FNV1a-64 哈希。
        /// </summary>
        /// <param name="bytes">字节数组。</param>
        /// <param name="offset">起始偏移。</param>
        /// <param name="length">长度。</param>
        /// <returns>64 位哈希值。</returns>
        public static ulong Compute(byte[] bytes, int offset, int length)
        {
            if (bytes == null)
            {
                throw new FrameworkException("ConfigBlobHash.Compute：字节数组不能为 null。");
            }

            if (offset < 0 || length < 0 || offset + length > bytes.Length)
            {
                throw new FrameworkException("ConfigBlobHash.Compute：offset/length 越界。");
            }

            ulong hash = Fnv1a64Offset;
            int end = offset + length;
            for (int i = offset; i < end; i++)
            {
                hash ^= bytes[i];
                hash *= Fnv1a64Prime;
            }

            return hash;
        }

        /// <summary>
        /// 把若干名称与版本号合并成 schemaHash，用于校验"生成代码"与"数据文件"是否同源。
        /// codegen 侧按同样顺序传入所有表名与字段签名即可。
        /// </summary>
        /// <param name="parts">参与哈希的字符串（顺序敏感）。</param>
        /// <returns>schemaHash。</returns>
        public static ulong ComputeSchemaHash(params string[] parts)
        {
            if (parts == null)
            {
                throw new FrameworkException("ConfigBlobHash.ComputeSchemaHash：parts 不能为 null。");
            }

            ulong hash = Fnv1a64Offset;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i] ?? string.Empty;
                for (int j = 0; j < part.Length; j++)
                {
                    char c = part[j];
                    hash ^= (byte)(c & 0xFF);
                    hash *= Fnv1a64Prime;
                    hash ^= (byte)((c >> 8) & 0xFF);
                    hash *= Fnv1a64Prime;
                }

                // 分隔符，避免 ("ab","c") 与 ("a","bc") 撞同一个哈希。
                hash ^= 0x1F;
                hash *= Fnv1a64Prime;
            }

            return hash;
        }
    }
}
