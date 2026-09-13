//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Text;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// zip 中央目录解析器：在不解压、不读取条目内容的前提下，定位某个 <b>未压缩（STORED）</b> 条目
    /// 在 zip 文件中的数据起始偏移与长度。
    ///
    /// 设计理由：
    ///   • Android 上 StreamingAssets 实际就是 APK（zip）里的一段。若条目是 STORED 存储的，它在 APK 文件中
    ///     是一段连续的原始字节，可以直接对 APK 按偏移做只读内存映射拿到，完全跳过"解压到沙盒再读"的
    ///     一次全量拷贝与一份磁盘副本——这正是 <see cref="ApkOffsetBlobSource"/> 的前提。
    ///   • 因此本类只解析目录结构（EOCD → 中央目录 → 本地文件头），不含任何解压逻辑：
    ///     遇到非 STORED 条目直接失败并给出可执行的修复建议，而不是悄悄退化成慢路径。
    ///
    /// 限制：不支持 ZIP64（条目/目录偏移达到 0xFFFFFFFF 时抛异常）。APK 中的配置条目远达不到该量级。
    ///
    /// 线程契约：纯静态方法，不持有状态；调用方需保证传入的 Stream 不被并发使用。
    /// </summary>
    public static class ZipEntryLocator
    {
        private const uint EndOfCentralDirectorySignature = 0x06054B50;
        private const uint CentralDirectoryHeaderSignature = 0x02014B50;
        private const uint LocalFileHeaderSignature = 0x04034B50;

        private const int EndOfCentralDirectoryMinSize = 22;
        private const int CentralDirectoryHeaderMinSize = 46;
        private const int LocalFileHeaderMinSize = 30;

        /// <summary>zip 注释最大长度为 65535，EOCD 因此最多距文件尾 65535 + 22 字节。</summary>
        private const int MaxEndOfCentralDirectorySearch = 65535 + EndOfCentralDirectoryMinSize;

        /// <summary>未压缩存储的压缩方法编号。</summary>
        private const ushort CompressionMethodStored = 0;

        /// <summary>
        /// 定位 zip 文件中某个条目的数据区间。
        /// </summary>
        /// <param name="zipPath">zip（或 APK）文件路径。</param>
        /// <param name="entryName">条目名（zip 内路径，使用 '/' 分隔，大小写敏感）。</param>
        /// <param name="dataOffset">输出：条目原始数据在文件中的起始偏移。</param>
        /// <param name="length">输出：条目原始数据长度。</param>
        /// <returns>找到且为 STORED 返回 true；未找到返回 false。找到但非 STORED 抛异常。</returns>
        public static bool TryLocate(string zipPath, string entryName, out long dataOffset, out long length)
        {
            if (string.IsNullOrEmpty(zipPath))
            {
                throw new FrameworkException("ZipEntryLocator：zipPath 不能为空。");
            }

            using (FileStream stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return TryLocate(stream, entryName, out dataOffset, out length);
            }
        }

        /// <summary>
        /// 定位可随机访问的 zip 流中某个条目的数据区间。流的读游标会被移动。
        /// </summary>
        /// <param name="stream">可 Seek 的 zip 流。</param>
        /// <param name="entryName">条目名（zip 内路径，使用 '/' 分隔，大小写敏感）。</param>
        /// <param name="dataOffset">输出：条目原始数据在流中的起始偏移。</param>
        /// <param name="length">输出：条目原始数据长度。</param>
        /// <returns>找到且为 STORED 返回 true；未找到返回 false。找到但非 STORED 抛异常。</returns>
        public static bool TryLocate(Stream stream, string entryName, out long dataOffset, out long length)
        {
            dataOffset = 0L;
            length = 0L;

            if (stream == null)
            {
                throw new FrameworkException("ZipEntryLocator：stream 不能为 null。");
            }

            if (!stream.CanSeek)
            {
                throw new FrameworkException("ZipEntryLocator：stream 必须可 Seek。");
            }

            if (string.IsNullOrEmpty(entryName))
            {
                throw new FrameworkException("ZipEntryLocator：entryName 不能为空。");
            }

            byte[] entryNameUtf8 = Encoding.UTF8.GetBytes(entryName.Replace('\\', '/'));

            long centralDirectoryOffset;
            int centralDirectorySize;
            int entryCount;
            ReadEndOfCentralDirectory(stream, out centralDirectoryOffset, out centralDirectorySize, out entryCount);

            byte[] directory = new byte[centralDirectorySize];
            stream.Seek(centralDirectoryOffset, SeekOrigin.Begin);
            ReadFully(stream, directory, 0, centralDirectorySize, "中央目录");

            int cursor = 0;
            for (int i = 0; i < entryCount; i++)
            {
                if (cursor + CentralDirectoryHeaderMinSize > directory.Length)
                {
                    throw new FrameworkException("ZipEntryLocator：中央目录被截断，文件已损坏。");
                }

                if (ReadUInt32(directory, cursor) != CentralDirectoryHeaderSignature)
                {
                    throw new FrameworkException(Utility.Text.Format("ZipEntryLocator：中央目录第 {0} 项签名不合法，文件已损坏。", i));
                }

                ushort compressionMethod = ReadUInt16(directory, cursor + 10);
                uint compressedSize = ReadUInt32(directory, cursor + 20);
                int nameLength = ReadUInt16(directory, cursor + 28);
                int extraLength = ReadUInt16(directory, cursor + 30);
                int commentLength = ReadUInt16(directory, cursor + 32);
                uint localHeaderOffset = ReadUInt32(directory, cursor + 42);
                int nameOffset = cursor + CentralDirectoryHeaderMinSize;

                if (nameOffset + nameLength > directory.Length)
                {
                    throw new FrameworkException("ZipEntryLocator：中央目录条目名越界，文件已损坏。");
                }

                if (Matches(directory, nameOffset, nameLength, entryNameUtf8))
                {
                    if (compressionMethod != CompressionMethodStored)
                    {
                        throw new FrameworkException(Utility.Text.Format(
                            "ZipEntryLocator：条目 \"{0}\" 是压缩存储的（compressionMethod={1}），无法按偏移直接映射。" +
                            "请在构建配置中把该文件的扩展名加入 noCompress（Unity: PlayerSettings/Android 的未压缩扩展名列表，" +
                            "或打包脚本的 noCompress 配置），使其以 STORED 方式打入 APK。",
                            entryName, compressionMethod));
                    }

                    if (compressedSize == uint.MaxValue || localHeaderOffset == uint.MaxValue)
                    {
                        throw new FrameworkException(Utility.Text.Format("ZipEntryLocator：条目 \"{0}\" 使用了 ZIP64 扩展，当前实现不支持。", entryName));
                    }

                    dataOffset = ResolveLocalDataOffset(stream, localHeaderOffset, entryName);
                    length = compressedSize;
                    return true;
                }

                cursor = nameOffset + nameLength + extraLength + commentLength;
            }

            return false;
        }

        /// <summary>
        /// 从文件尾向前搜索 EOCD 记录，取出中央目录的位置、大小与条目数。
        /// </summary>
        private static void ReadEndOfCentralDirectory(Stream stream, out long centralDirectoryOffset, out int centralDirectorySize, out int entryCount)
        {
            long fileLength = stream.Length;
            if (fileLength < EndOfCentralDirectoryMinSize)
            {
                throw new FrameworkException("ZipEntryLocator：文件过小，不是合法的 zip。");
            }

            int searchLength = (int)Math.Min(fileLength, MaxEndOfCentralDirectorySearch);
            byte[] tail = new byte[searchLength];
            stream.Seek(fileLength - searchLength, SeekOrigin.Begin);
            ReadFully(stream, tail, 0, searchLength, "EOCD 搜索区");

            // 从后往前找，命中最后一个 EOCD 签名：注释里也可能出现同样的 4 字节，
            // 但只有真正的 EOCD 之后剩余长度才与注释长度字段自洽，所以额外做一致性校验。
            for (int i = searchLength - EndOfCentralDirectoryMinSize; i >= 0; i--)
            {
                if (ReadUInt32(tail, i) != EndOfCentralDirectorySignature)
                {
                    continue;
                }

                int commentLength = ReadUInt16(tail, i + 20);
                if (i + EndOfCentralDirectoryMinSize + commentLength != searchLength)
                {
                    continue;
                }

                entryCount = ReadUInt16(tail, i + 10);
                uint size = ReadUInt32(tail, i + 12);
                uint offset = ReadUInt32(tail, i + 16);
                if (size == uint.MaxValue || offset == uint.MaxValue || entryCount == ushort.MaxValue)
                {
                    throw new FrameworkException("ZipEntryLocator：该 zip 使用了 ZIP64 扩展，当前实现不支持。");
                }

                if (offset + (long)size > fileLength)
                {
                    throw new FrameworkException("ZipEntryLocator：中央目录位置超出文件范围，文件已损坏。");
                }

                centralDirectoryOffset = offset;
                centralDirectorySize = (int)size;
                return;
            }

            throw new FrameworkException("ZipEntryLocator：未找到 EOCD 记录，不是合法的 zip 文件。");
        }

        /// <summary>
        /// 读本地文件头，跳过其中的文件名与扩展字段，得到真正的数据起始偏移。
        /// 中央目录里的名字/扩展长度与本地头可以不同，必须以本地头为准。
        /// </summary>
        private static long ResolveLocalDataOffset(Stream stream, long localHeaderOffset, string entryName)
        {
            if (localHeaderOffset + LocalFileHeaderMinSize > stream.Length)
            {
                throw new FrameworkException(Utility.Text.Format("ZipEntryLocator：条目 \"{0}\" 的本地文件头越界，文件已损坏。", entryName));
            }

            byte[] header = new byte[LocalFileHeaderMinSize];
            stream.Seek(localHeaderOffset, SeekOrigin.Begin);
            ReadFully(stream, header, 0, LocalFileHeaderMinSize, "本地文件头");

            if (ReadUInt32(header, 0) != LocalFileHeaderSignature)
            {
                throw new FrameworkException(Utility.Text.Format("ZipEntryLocator：条目 \"{0}\" 的本地文件头签名不合法，文件已损坏。", entryName));
            }

            int nameLength = ReadUInt16(header, 26);
            int extraLength = ReadUInt16(header, 28);
            return localHeaderOffset + LocalFileHeaderMinSize + nameLength + extraLength;
        }

        private static bool Matches(byte[] buffer, int offset, int length, byte[] expected)
        {
            if (length != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < length; i++)
            {
                if (buffer[offset + i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void ReadFully(Stream stream, byte[] buffer, int offset, int count, string what)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, offset + read, count - read);
                if (n <= 0)
                {
                    throw new FrameworkException(Utility.Text.Format("ZipEntryLocator：读取{0}时数据提前结束。", what));
                }

                read += n;
            }
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }
    }
}
