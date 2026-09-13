//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// ConfigBlob 的打开门面：把"选哪种字节来源"的判断收拢到一处，业务侧只面对两个入口。
    ///
    /// <list type="bullet">
    /// <item><see cref="OpenFile"/> —— 数据是磁盘上的独立文件（编辑器 / PC / iOS / 主机的
    ///   StreamingAssets，或热更后的沙盒目录）。默认优先 mmap（按页惰性加载、不进托管堆），
    ///   映射失败自动回退整块读入堆外内存并 Warning 一次。</item>
    /// <item><see cref="OpenZipEntry"/> —— 数据是 zip 包内的 STORED 条目（Android 上 StreamingAssets
    ///   实际是 APK 内的一段）。定位偏移后对包文件做区间映射，零拷贝零解压；映射不可用时回退
    ///   "按偏移整块读入"。条目被<b>压缩</b>存储不走回退：那是构建配置错误（noCompress 缺失），
    ///   定位阶段就会抛出并附修复指引，悄悄解压兜底只会把它永远藏起来。</item>
    /// </list>
    ///
    /// 本类是纯 C# 的：路径从哪来（Application.streamingAssetsPath / dataPath）由 Unity 侧封装决定，
    /// 见 EjoyFramework.Core.Unity 的 ConfigBlobStreamingAssets。
    ///
    /// 线程契约：与 <see cref="ConfigBlob.Open"/> 一致，本类方法在主线程调用。
    /// </summary>
    public static class ConfigBlobFile
    {
        /// <summary>
        /// 打开一个独立文件形态的 blob。
        /// </summary>
        /// <param name="filePath">blob 文件路径。</param>
        /// <param name="expectedSchemaHash">生成代码侧的 schemaHash（ConfigBlobSchema.SchemaHash）。</param>
        /// <param name="allowMemoryMapping">
        /// 是否允许内存映射。默认 true。编辑器侧的工具/校验代码应传 false：映射会以"拒绝写共享"的方式
        /// 一直锁住文件（mmap 来源刻意没有终结器，泄漏的映射在进程退出前不会解锁），一次未 Release 的
        /// 泄漏就足以让后续对同一文件的导出/覆盖全部失败。
        /// </param>
        /// <returns>已打开的 ConfigBlob，引用计数为 1。</returns>
        public static ConfigBlob OpenFile(string filePath, ulong expectedSchemaHash, bool allowMemoryMapping = true)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new FrameworkException("ConfigBlobFile.OpenFile：filePath 不能为空。");
            }

            if (!File.Exists(filePath))
            {
                throw new FrameworkException(Utility.Text.Format(
                    "ConfigBlobFile.OpenFile：文件不存在：{0}。请确认已执行配置导出（菜单 EjoyFramework/Core/Config/Export ConfigBlob）且文件随包分发。", filePath));
            }

            IConfigBlobSource source;
            if (!allowMemoryMapping)
            {
                source = new NativeAllocBlobSource(filePath);
            }
            else
            {
                try
                {
                    source = new MmapBlobSource(filePath);
                }
                catch (Exception e)
                {
                    // 宽 catch 是有意的：MmapBlobSource 只把映射阶段的异常统一成 FrameworkException，
                    // 之前的 FileInfo/Length 探测可能抛裸 IOException / UnauthorizedAccessException 等；
                    // 本块的意图是"任何映射失败都优雅降级"，文件真不可读时下面的回退会再次响亮失败。
                    FrameworkLog.Warning(Utility.Text.Format(
                        "ConfigBlobFile.OpenFile：内存映射失败，回退到整块读入（文件：{0}）。原因：{1}", filePath, e.Message));
                    source = OpenBuffered(filePath, e);
                }
            }

            return ConfigBlob.Open(source, expectedSchemaHash);
        }

        /// <summary>
        /// 打开 zip（APK）内一个 STORED 条目形态的 blob。区间映射失败时回退"按偏移整块读入"。
        /// </summary>
        /// <param name="zipPath">zip / APK 文件路径。</param>
        /// <param name="entryName">条目名（zip 内路径，如 <c>assets/ConfigTables.ejcb</c>，'/' 分隔，大小写敏感）。</param>
        /// <param name="expectedSchemaHash">生成代码侧的 schemaHash（ConfigBlobSchema.SchemaHash）。</param>
        /// <param name="allowMemoryMapping">
        /// 是否允许内存映射。默认 true。与 <see cref="OpenFile"/> 的参数同义：编辑器工具链、
        /// 或业务侧已知映射不可靠的设备（自维护 denylist）可传 false 直接走整块读入。
        /// </param>
        /// <returns>已打开的 ConfigBlob，引用计数为 1。</returns>
        public static ConfigBlob OpenZipEntry(string zipPath, string entryName, ulong expectedSchemaHash, bool allowMemoryMapping = true)
        {
            // 定位阶段的失败（不是 zip、条目被压缩存储、目录损坏）原样上抛：这些都不是"换种读法"能解决的。
            long offset;
            long length;
            if (!ZipEntryLocator.TryLocate(zipPath, entryName, out offset, out length))
            {
                throw new FrameworkException(Utility.Text.Format(
                    "ConfigBlobFile.OpenZipEntry：包内不存在条目 \"{0}\"（包：{1}）。若使用了 Play Asset Delivery / OBB 分包，" +
                    "配置不在 base APK 内，请先落地到沙盒后改用 OpenFile 打开。", entryName, zipPath));
            }

            if (length > int.MaxValue)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "ConfigBlobFile.OpenZipEntry：条目过大（{0} 字节），ConfigBlob 仅支持 2GB 以内。条目：{1}", length, entryName));
            }

            IConfigBlobSource source;
            if (!allowMemoryMapping)
            {
                source = ReadZipSlice(zipPath, entryName, offset, (int)length, null);
            }
            else
            {
                try
                {
                    source = new ApkOffsetBlobSource(zipPath, offset, length);
                }
                catch (Exception e)
                {
                    // Android 恰是 mmap 最不可靠的平台（部分设备/IL2CPP 组合映射不可用），
                    // 这里必须有回退，否则游戏在那些设备上根本起不来。
                    FrameworkLog.Warning(Utility.Text.Format(
                        "ConfigBlobFile.OpenZipEntry：区间映射失败，回退到整块读入（包：{0}，条目：{1}）。原因：{2}", zipPath, entryName, e.Message));
                    source = ReadZipSlice(zipPath, entryName, offset, (int)length, e);
                }
            }

            return ConfigBlob.Open(source, expectedSchemaHash);
        }

        /// <summary>
        /// 按偏移把 zip 内一段原始字节整块读入堆外内存。作为映射的回退（<paramref name="mmapReason"/> 非空）
        /// 或显式的非映射读法（null）。失败时把两个原因合并抛出，理由同 <see cref="OpenBuffered"/>。
        /// </summary>
        private static IConfigBlobSource ReadZipSlice(string zipPath, string entryName, long offset, int length, Exception mmapReason)
        {
            try
            {
                using (FileStream stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    return new NativeAllocBlobSource(stream, length);
                }
            }
            catch (Exception fallbackException)
            {
                if (mmapReason == null)
                {
                    if (fallbackException is FrameworkException)
                    {
                        throw;
                    }

                    throw new FrameworkException(Utility.Text.Format(
                        "ConfigBlobFile.OpenZipEntry：整块读入失败（包：{0}，条目：{1}）。", zipPath, entryName), fallbackException);
                }

                throw new FrameworkException(Utility.Text.Format(
                    "ConfigBlobFile.OpenZipEntry：区间映射与整块读入均失败（包：{0}，条目：{1}）。整块读入失败见内部异常；此前映射失败原因：{2}",
                    zipPath, entryName, mmapReason.Message), fallbackException);
            }
        }

        /// <summary>
        /// mmap 失败后的回退读入。回退也失败时把两个原因合并抛出——运行时日志助手可能尚未注册
        /// （配置加载发生在启动极早期），只靠 Warning 的话映射失败的线索会彻底丢失。
        /// </summary>
        private static IConfigBlobSource OpenBuffered(string filePath, Exception mmapReason)
        {
            try
            {
                return new NativeAllocBlobSource(filePath);
            }
            catch (Exception fallbackException)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "ConfigBlobFile.OpenFile：内存映射与整块读入均失败（文件：{0}）。整块读入失败见内部异常；此前映射失败原因：{1}",
                    filePath, mmapReason.Message), fallbackException);
            }
        }
    }
}
