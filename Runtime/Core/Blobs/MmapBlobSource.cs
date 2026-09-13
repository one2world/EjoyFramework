//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// 以只读内存映射（mmap）方式提供 blob 字节的来源实现。
    ///
    /// 设计理由：
    ///   • 这是零解析方案的性能上限形态：加载不是"读文件 + 解析"，而是建立一次映射，页面由操作系统按需
    ///     调入；只被读到的表才真正产生 I/O，整个配置文件从不进入托管堆。
    ///   • 映射页是干净的只读文件页，内存吃紧时内核可直接丢弃回收（不像堆内存必须换页），
    ///     对移动端内存压力更友好。
    ///   • 只支持真实文件路径。压缩包内的条目走 <see cref="ApkOffsetBlobSource"/>。
    ///
    /// 平台注意：内存映射依赖 <c>System.IO.MemoryMappedFiles</c>，在编辑器/PC/主机稳定可用；
    /// 若目标平台不支持（构造抛异常），调用方应回退到 <see cref="NativeAllocBlobSource"/>。
    ///
    /// 线程契约：构造与 Dispose 在主线程；构造完成后可任意线程并发读。
    /// </summary>
    public sealed unsafe class MmapBlobSource : IConfigBlobSource
    {
        private FileStream m_Stream;
        private MemoryMappedFile m_File;
        private MemoryMappedViewAccessor m_View;
        private byte* m_Pointer;
        private int m_Length;
        private bool m_PointerAcquired;

        /// <summary>
        /// 只读映射整个文件。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        public MmapBlobSource(string filePath)
            : this(filePath, 0L, 0L)
        {
        }

        /// <summary>
        /// 只读映射文件的一段区间。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        /// <param name="offset">映射起始偏移（字节）。</param>
        /// <param name="length">映射长度（字节）；传 0 表示"从 offset 到文件末尾"。</param>
        public MmapBlobSource(string filePath, long offset, long length)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new FrameworkException("MmapBlobSource：filePath 不能为空。");
            }

            if (offset < 0L || length < 0L)
            {
                throw new FrameworkException("MmapBlobSource：offset/length 不能为负数。");
            }

            // 文件不存在时 FileInfo.Length 抛的是 FileNotFoundException，与本模块"损坏/缺失一律
            // FrameworkException + 可执行提示"的契约不一致，这里统一掉。
            FileInfo info = new FileInfo(filePath);
            if (!info.Exists)
            {
                throw new FrameworkException(Utility.Text.Format("MmapBlobSource：文件不存在：{0}", filePath));
            }

            long fileLength = info.Length;
            if (length == 0L)
            {
                length = fileLength - offset;
            }

            if (offset + length > fileLength)
            {
                throw new FrameworkException(Utility.Text.Format("MmapBlobSource：映射区间超出文件范围，offset={0}，length={1}，文件长度={2}。文件：{3}", offset, length, fileLength, filePath));
            }

            if (length > int.MaxValue)
            {
                throw new FrameworkException(Utility.Text.Format("MmapBlobSource：映射长度过大（{0} 字节），ConfigBlob 仅支持 2GB 以内。文件：{1}", length, filePath));
            }

            if (length <= 0L)
            {
                throw new FrameworkException(Utility.Text.Format("MmapBlobSource：映射长度必须大于 0。文件：{0}", filePath));
            }

            try
            {
                // 显式自建 FileStream 以明确用 FileShare.Read 打开（CreateFromFile(path,...) 的部分重载
                // 在 Mono 上对 FileShare 处理不一致），允许构建流程同时读取同一文件。
                // 关键：leaveOpen 传 false 只在 CreateFromFile 成功接管之后才有意义；若它自身抛出
                // （Android/IL2CPP 上最容易发生），这个 FileStream 就没人关闭了。所以把它提为字段，
                // 由 Dispose 统一负责——即使所有权已转移给 MemoryMappedFile，重复 Dispose 也是安全的。
                m_Stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                m_File = MemoryMappedFile.CreateFromFile(m_Stream, null, 0L, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
                m_View = m_File.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);

                byte* basePointer = null;
                m_View.SafeMemoryMappedViewHandle.AcquirePointer(ref basePointer);
                m_PointerAcquired = true;

                // 映射视图的起点会被向下对齐到系统分配粒度，PointerOffset 是"请求的 offset 相对视图起点"的差值，
                // 必须叠加上去才是真正的数据首字节。漏掉它会读到文件里错位的内容。
                m_Pointer = basePointer + m_View.PointerOffset;
                m_Length = (int)length;
            }
            catch (Exception e)
            {
                Dispose();
                if (e is FrameworkException)
                {
                    throw;
                }

                throw new FrameworkException(Utility.Text.Format("MmapBlobSource：内存映射失败（文件：{0}）。若当前平台不支持内存映射，请回退到 NativeAllocBlobSource。", filePath), e);
            }
        }

        /// <inheritdoc />
        public byte* Pointer
        {
            get { return m_Pointer; }
        }

        /// <inheritdoc />
        public int Length
        {
            get { return m_Length; }
        }

        /// <summary>
        /// 释放映射与文件句柄。可重入。
        ///
        /// 本类<b>刻意不实现终结器</b>，请勿添加：Dispose 会触碰
        /// <see cref="MemoryMappedViewAccessor"/>、<see cref="SafeMemoryMappedViewHandle"/>、
        /// <see cref="MemoryMappedFile"/>、<see cref="FileStream"/> 四个托管对象，而终结顺序没有任何保证——
        /// 在终结器线程上对已终结的 SafeHandle 调 ReleasePointer 会抛异常，终结器线程上的异常直接杀进程。
        /// 这四个对象各自都带 SafeHandle 兜底，忘记 Dispose 最多是句柄延迟回收，不会泄漏到进程结束，
        /// 因此这里的终结器纯粹是风险没有收益。
        /// （<see cref="NativeAllocBlobSource"/> 的终结器是另一回事：它只碰 IntPtr 与 FreeHGlobal，
        /// 没有托管对象依赖，且不加就会真的泄漏堆外内存。）
        /// </summary>
        public void Dispose()
        {
            if (m_PointerAcquired && m_View != null)
            {
                // AcquirePointer/ReleasePointer 是引用计数的，漏掉 Release 会让映射句柄永远无法关闭。
                m_View.SafeMemoryMappedViewHandle.ReleasePointer();
                m_PointerAcquired = false;
            }

            m_Pointer = null;
            m_Length = 0;

            if (m_View != null)
            {
                m_View.Dispose();
                m_View = null;
            }

            if (m_File != null)
            {
                m_File.Dispose();
                m_File = null;
            }

            if (m_Stream != null)
            {
                // MemoryMappedFile 成功接管时（leaveOpen:false）已经关过一次，这里是幂等的二次关闭；
                // CreateFromFile 失败时这里才是唯一的关闭点。
                m_Stream.Dispose();
                m_Stream = null;
            }
        }
    }
}
