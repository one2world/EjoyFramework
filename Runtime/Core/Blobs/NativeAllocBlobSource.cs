//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// 把 blob 一次性读入堆外内存（<see cref="Marshal.AllocHGlobal(int)"/>）的来源实现。
    ///
    /// 设计理由：
    ///   • 全平台兜底：内存映射在部分平台/沙盒（尤其 IL2CPP + 某些 Android 设备、WebGL）不可用或有坑，
    ///     本实现只依赖最基础的文件读取与非托管分配，任何平台都能跑。
    ///   • 用堆外内存而不是 byte[]：托管数组要 pin 住才能取指针，长期 pin 大数组会给 GC 制造碎片；
    ///     堆外内存天然地址固定，且配置数据本来就是"整个进程生命周期常驻"，不需要 GC 参与。
    ///   • 代价是一次完整拷贝与常驻物理内存，因此仅作兜底或小体量配置使用；大体量优先
    ///     <see cref="MmapBlobSource"/>。
    ///
    /// 线程契约：构造与 Dispose 在主线程；构造完成后可任意线程并发读。
    /// </summary>
    public sealed unsafe class NativeAllocBlobSource : IConfigBlobSource
    {
        private IntPtr m_Native;
        private int m_Length;

        /// <summary>
        /// 从字节数组构造（内容会被拷贝一份到堆外内存，调用方之后可自由处置 <paramref name="data"/>）。
        /// </summary>
        /// <param name="data">blob 字节。</param>
        public NativeAllocBlobSource(byte[] data)
            : this(data, 0, data != null ? data.Length : 0)
        {
        }

        /// <summary>
        /// 从字节数组的一段区间构造（内容会被拷贝）。
        /// </summary>
        /// <param name="data">源字节数组。</param>
        /// <param name="offset">起始偏移。</param>
        /// <param name="length">长度。</param>
        public NativeAllocBlobSource(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                throw new FrameworkException("NativeAllocBlobSource：data 不能为 null。");
            }

            // 用 long 比较，避免 offset+length 在 int 上溢出后绕回小值而通过检查。
            if (offset < 0 || length < 0 || (long)offset + length > data.Length)
            {
                throw new FrameworkException(Utility.Text.Format("NativeAllocBlobSource：区间越界，offset={0}，length={1}，数组长度={2}。", offset, length, data.Length));
            }

            Allocate(length);
            try
            {
                if (length > 0)
                {
                    Marshal.Copy(data, offset, m_Native, length);
                }
            }
            catch
            {
                // 分配之后的任何失败都必须先归还堆外内存：构造函数抛出后调用方拿不到实例，
                // 也就没人能再调 Dispose，泄漏将持续到进程结束。
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 从文件一次性读入。文件长度超过 <see cref="int.MaxValue"/> 时抛异常。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        public NativeAllocBlobSource(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new FrameworkException("NativeAllocBlobSource：filePath 不能为空。");
            }

            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    long fileLength = stream.Length;
                    if (fileLength > int.MaxValue)
                    {
                        throw new FrameworkException(Utility.Text.Format("NativeAllocBlobSource：文件过大（{0} 字节），ConfigBlob 仅支持 2GB 以内。文件：{1}", fileLength, filePath));
                    }

                    int length = (int)fileLength;
                    Allocate(length);
                    ReadFully(stream, (byte*)m_Native, length, filePath);
                }
            }
            catch (Exception e)
            {
                // 文件被截断、读到一半失败等情况都会走到这里，此时堆外内存可能已分配。
                Dispose();
                if (e is FrameworkException)
                {
                    throw;
                }

                throw new FrameworkException(Utility.Text.Format("NativeAllocBlobSource：读取文件失败：{0}", filePath), e);
            }
        }

        /// <summary>
        /// 从任意可读流一次性读入指定长度。
        /// </summary>
        /// <param name="stream">源流（从当前位置开始读）。</param>
        /// <param name="length">要读取的字节数。</param>
        public NativeAllocBlobSource(Stream stream, int length)
        {
            if (stream == null)
            {
                throw new FrameworkException("NativeAllocBlobSource：stream 不能为 null。");
            }

            if (length < 0)
            {
                throw new FrameworkException("NativeAllocBlobSource：length 不能为负数。");
            }

            Allocate(length);
            try
            {
                ReadFully(stream, (byte*)m_Native, length, "<stream>");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 兜底释放。正常路径应由持有方（<see cref="ConfigBlob.Release"/>）显式 Dispose，
        /// 那条路径会 <see cref="GC.SuppressFinalize"/> 掉本终结器；这里只防"实例被遗忘"时
        /// 堆外内存泄漏到进程结束。
        ///
        /// 终结器在本类是安全的，因为 <see cref="Dispose"/> 只触碰 <see cref="IntPtr"/> 与
        /// <see cref="Marshal.FreeHGlobal"/>，不依赖任何可能已被先行终结的托管对象。
        /// 需要托管对象协作的来源（如 <see cref="MmapBlobSource"/>）刻意不加终结器，原因见该类 Dispose 的注释。
        /// </summary>
        ~NativeAllocBlobSource()
        {
            Dispose();
        }

        /// <inheritdoc />
        public byte* Pointer
        {
            get { return (byte*)m_Native; }
        }

        /// <inheritdoc />
        public int Length
        {
            get { return m_Length; }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // 用 Interlocked 取走指针再释放：终结器线程与主线程可能同时进来，
            // 裸的"判空 → 释放 → 置空"会导致 double free。
            IntPtr native = Interlocked.Exchange(ref m_Native, IntPtr.Zero);
            if (native != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(native);
            }

            m_Length = 0;

            // 显式释放之后就不必再进终结队列：省掉一次终结器排队与一个 GC 世代的存活延长。
            GC.SuppressFinalize(this);
        }

        private void Allocate(int length)
        {
            // AllocHGlobal(0) 的返回值在各运行时上语义不统一，长度为 0 时统一分配 1 字节，
            // 保证 Pointer 永远是可判空的有效地址，读侧的边界检查会拦住任何实际访问。
            m_Native = Marshal.AllocHGlobal(length > 0 ? length : 1);
            m_Length = length;
        }

        private static void ReadFully(Stream stream, byte* destination, int length, string what)
        {
            if (length <= 0)
            {
                return;
            }

            byte[] chunk = new byte[Math.Min(length, 64 * 1024)];
            int written = 0;
            while (written < length)
            {
                int want = Math.Min(chunk.Length, length - written);
                int read = stream.Read(chunk, 0, want);
                if (read <= 0)
                {
                    throw new FrameworkException(Utility.Text.Format("NativeAllocBlobSource：数据提前结束，期望 {0} 字节，实际 {1} 字节。来源：{2}", length, written, what));
                }

                Marshal.Copy(chunk, 0, (IntPtr)(destination + written), read);
                written += read;
            }
        }
    }
}
