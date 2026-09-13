//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.IO;
using EjoyFramework.Core.Blobs;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 从 StreamingAssets 打开 ConfigBlob 的平台封装：调用方只给"文件名 + schemaHash"，
    /// 平台差异（Android 上 StreamingAssets 是 APK 内条目、其余平台是真实文件）在这里消化。
    ///
    /// 平台矩阵：
    /// <list type="bullet">
    /// <item>PC / iOS / 主机 —— <c>Application.streamingAssetsPath</c> 下的真实文件，
    ///   走 <see cref="ConfigBlobFile.OpenFile"/>（mmap 优先，自动回退）。</item>
    /// <item>编辑器 —— 同上，但禁用 mmap：映射会以拒绝写共享的方式锁住导出目标文件，
    ///   播放模式下一次未 Release 的泄漏就会让后续"导出 / 出包"全部失败；编辑器内也不需要按页惰性的收益。</item>
    /// <item>Android 真机 —— StreamingAssets 打进 APK 的 <c>assets/</c> 目录，
    ///   走 <see cref="ConfigBlobFile.OpenZipEntry"/> 对 APK 做偏移映射（零拷贝零解压），
    ///   映射不可用的设备自动回退整块读入。Unity 的 Gradle 模板默认把 StreamingAssets 内出现的
    ///   扩展名并入 noCompress，因此条目是 STORED 存储；若自定义模板破坏了这一点，打开时会明确报错并附修复指引。</item>
    /// <item>Android + Play Asset Delivery / OBB 分包 —— 条目不在 base APK 内，需要业务侧先把数据
    ///   落到沙盒后再走 <see cref="ConfigBlobFile.OpenFile"/>。</item>
    /// <item>WebGL —— 不支持（没有本地文件系统），本类直接抛出明确错误；请先把 blob 下载进内存后用
    ///   <see cref="ConfigBlob.Open"/> + <see cref="NativeAllocBlobSource"/> 打开。</item>
    /// </list>
    ///
    /// 线程契约：仅主线程调用（内部读 <c>Application.dataPath</c> / <c>streamingAssetsPath</c>，
    /// 二者均为主线程 API；<see cref="ConfigBlob.Open"/> 本身也约定主线程）。
    /// </summary>
    public static class ConfigBlobStreamingAssets
    {
        /// <summary>
        /// 打开 StreamingAssets 下的 blob 文件。
        /// </summary>
        /// <param name="fileName">相对 StreamingAssets 根的文件名，如 <c>ConfigTables.ejcb</c>；不接受绝对路径。</param>
        /// <param name="expectedSchemaHash">生成代码侧的 schemaHash（ConfigBlobSchema.SchemaHash）。</param>
        /// <returns>已打开的 ConfigBlob，引用计数为 1，用完 <see cref="ConfigBlob.Release"/>。</returns>
        public static ConfigBlob Open(string fileName, ulong expectedSchemaHash)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new FrameworkException("ConfigBlobStreamingAssets.Open：fileName 不能为空。");
            }

            // 统一成 '/'、去掉引导分隔符：Android 分支是字符串拼接（"assets/" + fileName），
            // 引导 '/' 会拼出 "assets//x" 而 Path.Combine 分支却能容忍甚至绝对化——同一实参必须两分支同义。
            string normalized = fileName.Replace('\\', '/').TrimStart('/');
            if (Path.IsPathRooted(fileName) || normalized.Length == 0)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "ConfigBlobStreamingAssets.Open：fileName 必须是相对 StreamingAssets 根的相对路径，实际为 \"{0}\"。", fileName));
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            throw new FrameworkException(
                "ConfigBlobStreamingAssets.Open：WebGL 没有本地文件系统（streamingAssetsPath 是 URL），" +
                "请先把 blob 下载进内存，再用 ConfigBlob.Open(new NativeAllocBlobSource(bytes), schemaHash) 打开。");
#elif UNITY_ANDROID && !UNITY_EDITOR
            // Application.dataPath 在 Android 真机上就是 base APK 的路径；
            // StreamingAssets 的内容位于 APK 内的 assets/ 前缀之下。
            return ConfigBlobFile.OpenZipEntry(Application.dataPath, "assets/" + normalized, expectedSchemaHash);
#elif UNITY_EDITOR
            return ConfigBlobFile.OpenFile(
                Path.Combine(Application.streamingAssetsPath, normalized), expectedSchemaHash, allowMemoryMapping: false);
#else
            return ConfigBlobFile.OpenFile(Path.Combine(Application.streamingAssetsPath, normalized), expectedSchemaHash);
#endif
        }
    }
}
