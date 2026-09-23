//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Serialization;

namespace EjoyFramework.Core.Telemetry
{
    /// <summary>
    /// 性能遥测：把运行期的性能与运行状况采样成**定长二进制记录**，按会话攒成批次，离线排队，经后端上报。
    /// 这是"上线后知道玩家设备上到底跑得怎么样"的底座；崩溃/ANR（WS4-M2）与设备分级（WS4-M3）共用它的会话与上传通道。
    ///
    /// 模型：
    ///   • <b>会话</b>：Start(deviceInfo) → 周期 Record → Flush/End。会话 id 与设备信息只在批次头写一次。
    ///   • <b>记录</b>：<see cref="TelemetryRecord"/>（16 个 float/int 槽位的定长 struct），不同种类
    ///     （帧时间窗口 / 加载耗时 / 内存快照 / 自定义）共用一种布局——上报端不需要 schema 演进就能解析。
    ///   • <b>采样率</b>：<see cref="SampleRate"/>（0~1）按会话决定采还是不采；<see cref="Enabled"/> 是隐私/设置总开关。
    ///   • <b>批次</b>：记录攒到 <see cref="BatchSize"/> 或 <see cref="FlushIntervalSeconds"/> 到期就封成一个 <see cref="TelemetryBatch"/>
    ///     交给 <see cref="ITelemetryBackend"/>；后端异步完成后回报成功/失败，失败的批次进离线队列重试（有上限，超出丢最旧）。
    ///   • 零分配：记录是 struct，批次缓冲复用；只有封批时按批次大小租一次 BufferPool。
    ///
    /// 线程契约：Record 主线程；后端回调可能在任意线程（用 <see cref="ITelemetryBackend"/> 文档约定），
    /// 管理器内部用锁保护队列。
    /// </summary>
    public interface ITelemetryManager
    {
        /// <summary>总开关（隐私 / 设置）。关闭时 Record 直接丢弃，未发送批次保留。</summary>
        bool Enabled { get; set; }

        /// <summary>采样率 0~1；会话开始时掷一次骰子决定本会话是否采样。</summary>
        float SampleRate { get; set; }

        /// <summary>每批最多记录数。默认 256。</summary>
        int BatchSize { get; set; }

        /// <summary>批次最长攒多久（秒），到期强制封批。默认 30。</summary>
        float FlushIntervalSeconds { get; set; }

        /// <summary>离线队列最多保留的批次数（超出丢最旧）。默认 32。</summary>
        int MaxQueuedBatches { get; set; }

        /// <summary>设置后端；未设置时批次只进离线队列。</summary>
        void SetBackend(ITelemetryBackend backend);

        /// <summary>开始会话。返回本会话是否被采样（采样率掷骰）。</summary>
        bool StartSession(string sessionId, string deviceInfo, string appVersion);

        /// <summary>结束会话：强制封批并尝试上报。</summary>
        void EndSession();

        /// <summary>当前是否在采样中的会话内。</summary>
        bool IsSampling { get; }

        /// <summary>写一条记录（会话未采样/未启用时直接丢弃）。</summary>
        void Record(ref TelemetryRecord record);

        /// <summary>立即封批并尝试上报。</summary>
        void Flush();

        /// <summary>待上报批次数（含正在上报的）。</summary>
        int QueuedBatchCount { get; }

        /// <summary>当前批次已攒记录数。</summary>
        int PendingRecordCount { get; }

        long TotalRecords { get; }
        long TotalBatchesSent { get; }
        long TotalBatchesFailed { get; }
        long TotalBatchesDropped { get; }
    }

    /// <summary>记录种类（<see cref="TelemetryRecord.Kind"/>）；业务自定义从 1000 起。</summary>
    public static class TelemetryKind
    {
        /// <summary>帧时间窗口：F0=avgMs F1=p95Ms F2=p99Ms F3=maxMs I0=frames I1=spikes I2=level</summary>
        public const int FrameWindow = 1;

        /// <summary>内存快照：I0=managedMB I1=nativeMB I2=graphicsMB I3=residentBundleMB I4=totalReservedMB</summary>
        public const int Memory = 2;

        /// <summary>加载耗时：I0=loadKindHash I1=itemHash F0=seconds I2=success(0/1)</summary>
        public const int LoadTiming = 3;

        /// <summary>设备热/电：I0=thermalState F0=batteryLevel I1=batteryStatus</summary>
        public const int Thermal = 4;

        /// <summary>崩溃 / ANR（WS4-M2）：I0=kind(1 crash / 2 anr / 3 exception) I1=messageHash I2=stackHash F0=hangSeconds</summary>
        public const int Crash = 5;

        /// <summary>设备分级（WS4-M3）：I0=tier I1=reason F0=score</summary>
        public const int Tier = 6;

        /// <summary>业务自定义起点。</summary>
        public const int Custom = 1000;
    }

    /// <summary>定长遥测记录：8 个 float + 8 个 int 槽位。</summary>
    public struct TelemetryRecord
    {
        public int Kind;
        public float Timestamp;   // 会话内秒
        public float F0, F1, F2, F3, F4, F5, F6, F7;
        public int I0, I1, I2, I3, I4, I5, I6, I7;

        public const int SerializedSize = 4 + 4 + 8 * 4 + 8 * 4;

        public void WriteTo(ByteBuffer buffer)
        {
            buffer.WriteInt(Kind);
            buffer.WriteFloat(Timestamp);
            buffer.WriteFloat(F0); buffer.WriteFloat(F1); buffer.WriteFloat(F2); buffer.WriteFloat(F3);
            buffer.WriteFloat(F4); buffer.WriteFloat(F5); buffer.WriteFloat(F6); buffer.WriteFloat(F7);
            buffer.WriteInt(I0); buffer.WriteInt(I1); buffer.WriteInt(I2); buffer.WriteInt(I3);
            buffer.WriteInt(I4); buffer.WriteInt(I5); buffer.WriteInt(I6); buffer.WriteInt(I7);
        }

        public void ReadFrom(ByteBuffer buffer)
        {
            Kind = buffer.ReadInt();
            Timestamp = buffer.ReadFloat();
            F0 = buffer.ReadFloat(); F1 = buffer.ReadFloat(); F2 = buffer.ReadFloat(); F3 = buffer.ReadFloat();
            F4 = buffer.ReadFloat(); F5 = buffer.ReadFloat(); F6 = buffer.ReadFloat(); F7 = buffer.ReadFloat();
            I0 = buffer.ReadInt(); I1 = buffer.ReadInt(); I2 = buffer.ReadInt(); I3 = buffer.ReadInt();
            I4 = buffer.ReadInt(); I5 = buffer.ReadInt(); I6 = buffer.ReadInt(); I7 = buffer.ReadInt();
        }
    }

    /// <summary>
    /// 封好的批次：头（magic、版本、sessionId、deviceInfo、appVersion、记录数）+ 定长记录数组。
    /// <see cref="Payload"/> 的 [0, Length) 有效；后端上报完成后必须调用 <see cref="ITelemetryManager"/> 的回报（经 <see cref="ITelemetryBackend"/> 回调）。
    /// </summary>
    public sealed class TelemetryBatch
    {
        public const uint Magic = 0x4D544A45;   // 'EJTM'
        public const int FormatVersion = 1;

        public int BatchId;
        public string SessionId;
        public int RecordCount;
        public byte[] Payload;
        public int Length;
        public int Attempts;
    }

    /// <summary>
    /// 上报后端（HTTP / 文件 / 第三方 SDK）。<see cref="Send"/> 在主线程调用，完成后在任意线程调用 <paramref name="onComplete"/>(batch, success)。
    /// 后端不得保留对 Payload 的引用超过回调：回调后缓冲归还 BufferPool。
    /// </summary>
    public interface ITelemetryBackend
    {
        void Send(TelemetryBatch batch, Action<TelemetryBatch, bool> onComplete);
    }
}
