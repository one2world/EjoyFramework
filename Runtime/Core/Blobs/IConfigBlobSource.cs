//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// ConfigBlob 的字节来源抽象。把"blob 数据从哪来、怎么释放"与"怎么读"彻底分开：
    /// <see cref="ConfigBlob"/> 只认一段裸内存（指针 + 长度），来源可以是堆内存、内存映射文件、
    /// 甚至 APK 内未压缩条目的一段切片。
    ///
    /// 设计理由：
    ///   • 零解析的前提是"加载 = 拿到一段可随机访问的只读内存"。不同平台拿到这段内存的手段不同
    ///     （PC/编辑器可 mmap 真实文件、Android 需按偏移映射 APK 内条目、极端兜底就整块读进堆外内存），
    ///     抽象成接口后 <see cref="ConfigBlob"/> 无需知道差异。
    ///   • 返回裸指针而不是 byte[]，是为了让 mmap 路径能真正做到"按页惰性加载、不占托管堆、不产生一次
    ///     大数组分配"，这是本方案相对文本配置的主要收益之一。
    ///
    /// 实现约定（务必遵守，<see cref="ConfigBlob"/> 依赖这些不变量）：
    ///   • <see cref="Pointer"/> 在构造成功之后到 <see cref="IDisposable.Dispose"/> 之前必须始终有效且不变。
    ///   • 指向的内存必须至少有 <see cref="Length"/> 个可读字节，且在生命周期内内容只读不变。
    ///   • <see cref="IDisposable.Dispose"/> 必须可重入（重复调用不抛异常）。
    ///
    /// 线程契约：构造与 Dispose 在主线程；构造完成后 Pointer/Length 可被任意线程并发读取。
    /// </summary>
    public unsafe interface IConfigBlobSource : IDisposable
    {
        /// <summary>
        /// blob 数据首字节的指针。Dispose 之后取用是未定义行为。
        /// </summary>
        byte* Pointer { get; }

        /// <summary>
        /// blob 数据的字节长度。
        /// </summary>
        int Length { get; }
    }
}
