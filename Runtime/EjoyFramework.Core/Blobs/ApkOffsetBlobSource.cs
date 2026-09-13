//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// Android 专用来源：对 APK（zip）整体做只读内存映射，取其中某个 <b>STORED（未压缩）</b> 条目对应的切片。
    ///
    /// 设计理由：
    ///   • Android 上 StreamingAssets 里的文件并不是磁盘上的独立文件，而是 APK 内的一段。常规做法是
    ///     UnityWebRequest 读出来再落地/入堆，等于把整份配置拷贝一到两次。
    ///   • 只要该条目以 STORED 方式打包，它在 APK 文件里就是连续的原始字节，可以直接映射 APK 并按偏移取用：
    ///     零拷贝、零解压、按页惰性加载，与桌面端 mmap 路径行为一致。
    ///   • 前提是构建时必须把配置文件的扩展名加入 noCompress；否则 <see cref="ZipEntryLocator"/> 会在定位
    ///     阶段就明确报错，而不是退化成隐蔽的慢路径。
    ///
    /// 实现上直接复用 <see cref="MmapBlobSource"/> 的区间映射能力，本类只负责"把条目名解析成偏移"。
    ///
    /// 线程契约：构造与 Dispose 在主线程；构造完成后可任意线程并发读。
    /// </summary>
    public sealed unsafe class ApkOffsetBlobSource : IConfigBlobSource
    {
        private MmapBlobSource m_Inner;

        /// <summary>
        /// 按已知偏移与长度映射 APK 内的一段（偏移通常由 <see cref="ZipEntryLocator"/> 得到）。
        /// </summary>
        /// <param name="apkPath">APK（zip）文件路径。</param>
        /// <param name="entryOffset">条目原始数据在 APK 文件中的起始偏移。</param>
        /// <param name="length">条目原始数据长度。</param>
        public ApkOffsetBlobSource(string apkPath, long entryOffset, long length)
        {
            if (string.IsNullOrEmpty(apkPath))
            {
                throw new FrameworkException("ApkOffsetBlobSource：apkPath 不能为空。");
            }

            m_Inner = new MmapBlobSource(apkPath, entryOffset, length);
        }

        /// <summary>
        /// 按条目名在 APK 中定位并映射。条目必须是 STORED 未压缩存储，否则抛异常并给出修复建议。
        /// </summary>
        /// <param name="apkPath">APK（zip）文件路径。</param>
        /// <param name="entryName">条目名，例如 <c>assets/Configs/config.ejcb</c>。</param>
        /// <returns>映射好的来源。</returns>
        public static ApkOffsetBlobSource Open(string apkPath, string entryName)
        {
            long offset;
            long length;
            if (!ZipEntryLocator.TryLocate(apkPath, entryName, out offset, out length))
            {
                throw new FrameworkException(Utility.Text.Format("ApkOffsetBlobSource：APK 中不存在条目 \"{0}\"。APK：{1}", entryName, apkPath));
            }

            return new ApkOffsetBlobSource(apkPath, offset, length);
        }

        /// <inheritdoc />
        public byte* Pointer
        {
            get { return m_Inner != null ? m_Inner.Pointer : null; }
        }

        /// <inheritdoc />
        public int Length
        {
            get { return m_Inner != null ? m_Inner.Length : 0; }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_Inner != null)
            {
                m_Inner.Dispose();
                m_Inner = null;
            }
        }
    }
}
