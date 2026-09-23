//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>崩溃报告种类。数值写入报告文件与遥测记录（<c>TelemetryKind.Crash</c> 的 I0），不可改动已有值。</summary>
    public enum CrashKind : byte
    {
        /// <summary>未处理 / 已记录的托管异常（非致命）。</summary>
        Exception = 1,

        /// <summary>致命异常（进程即将终止，例如 AppDomain.UnhandledException IsTerminating）。</summary>
        Fatal = 2,

        /// <summary>主线程卡死（ANR）：心跳停顿超过阈值；恢复后记录实际时长。</summary>
        Hang = 3,

        /// <summary>上次会话未正常结束（原生崩溃 / 被杀），且没有更具体的线索。</summary>
        AbnormalExit = 4,

        /// <summary>上次会话在后台期间被系统结束（通常不是 bug，但计入留存分析）。</summary>
        BackgroundKill = 5,

        /// <summary>上次会话收到低内存警告后未正常结束（疑似 OOM 被杀）。</summary>
        LowMemoryKill = 6,

        /// <summary>上次会话在主线程卡死期间结束（ANR 被杀 / 用户强退）。</summary>
        HangKill = 7,
    }

    /// <summary>
    /// 一份崩溃报告。落盘格式（小端，<see cref="BinaryWriter"/>，字符串为 7-bit 变长长度前缀 + UTF-8）：
    /// <code>
    /// uint Magic('EJCR') | int FormatVersion | string ReportId | byte Kind | string SessionId | string AppVersion |
    /// string DeviceInfo | long UnixMillis | float SessionSeconds | string Message | string StackTrace |
    /// uint Fingerprint | string Phase | float HangSeconds | int Occurrences |
    /// int breadcrumbCount, string[] | int userKeyCount, (string key, string value)[]
    /// </code>
    /// null 字符串写成空串。解析端校验魔数与版本，任何截断/越界都视为损坏。
    /// </summary>
    public sealed class CrashReport
    {
        public const uint Magic = 0x52434A45;   // 'EJCR'
        public const int FormatVersion = 1;

        public string ReportId;
        public CrashKind Kind;
        public string SessionId;
        public string AppVersion;
        public string DeviceInfo;
        public long UnixMillis;
        public float SessionSeconds;
        public string Message;
        public string StackTrace;
        public uint Fingerprint;
        public string Phase;
        public float HangSeconds;
        public int Occurrences = 1;
        public string[] Breadcrumbs = Array.Empty<string>();
        public KeyValuePair<string, string>[] UserKeys = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>落盘路径（不序列化；未落盘为 null）。</summary>
        internal string StoragePath;

        /// <summary>已写入遥测的累计次数（不序列化）。</summary>
        internal int TelemetryOccurrences;

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(ReportId ?? string.Empty);
            writer.Write((byte)Kind);
            writer.Write(SessionId ?? string.Empty);
            writer.Write(AppVersion ?? string.Empty);
            writer.Write(DeviceInfo ?? string.Empty);
            writer.Write(UnixMillis);
            writer.Write(SessionSeconds);
            writer.Write(Message ?? string.Empty);
            writer.Write(StackTrace ?? string.Empty);
            writer.Write(Fingerprint);
            writer.Write(Phase ?? string.Empty);
            writer.Write(HangSeconds);
            writer.Write(Occurrences);
            string[] crumbs = Breadcrumbs ?? Array.Empty<string>();
            writer.Write(crumbs.Length);
            for (int i = 0; i < crumbs.Length; i++) writer.Write(crumbs[i] ?? string.Empty);
            KeyValuePair<string, string>[] keys = UserKeys ?? Array.Empty<KeyValuePair<string, string>>();
            writer.Write(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                writer.Write(keys[i].Key ?? string.Empty);
                writer.Write(keys[i].Value ?? string.Empty);
            }
        }

        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream(512))
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
            {
                WriteTo(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>解析报告字节；格式不符 / 截断返回 false（不抛）。</summary>
        public static bool TryParse(byte[] data, out CrashReport report)
        {
            report = null;
            if (data == null || data.Length < 8) return false;
            try
            {
                using (MemoryStream ms = new MemoryStream(data, false))
                using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (r.ReadUInt32() != Magic) return false;
                    if (r.ReadInt32() != FormatVersion) return false;
                    CrashReport c = new CrashReport();
                    c.ReportId = r.ReadString();
                    c.Kind = (CrashKind)r.ReadByte();
                    c.SessionId = r.ReadString();
                    c.AppVersion = r.ReadString();
                    c.DeviceInfo = r.ReadString();
                    c.UnixMillis = r.ReadInt64();
                    c.SessionSeconds = r.ReadSingle();
                    c.Message = r.ReadString();
                    c.StackTrace = r.ReadString();
                    c.Fingerprint = r.ReadUInt32();
                    c.Phase = r.ReadString();
                    c.HangSeconds = r.ReadSingle();
                    c.Occurrences = r.ReadInt32();
                    int crumbCount = r.ReadInt32();
                    if (crumbCount < 0 || crumbCount > data.Length) return false;
                    c.Breadcrumbs = new string[crumbCount];
                    for (int i = 0; i < crumbCount; i++) c.Breadcrumbs[i] = r.ReadString();
                    int keyCount = r.ReadInt32();
                    if (keyCount < 0 || keyCount > data.Length) return false;
                    c.UserKeys = new KeyValuePair<string, string>[keyCount];
                    for (int i = 0; i < keyCount; i++) c.UserKeys[i] = new KeyValuePair<string, string>(r.ReadString(), r.ReadString());
                    if (ms.Position != ms.Length) return false;
                    report = c;
                    return true;
                }
            }
            catch (EndOfStreamException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }   // 非法 UTF-8 / 负长度
            catch (FormatException) { return false; }     // 7-bit 长度前缀损坏
        }
    }

    /// <summary>
    /// 崩溃指纹：同一个 bug 在不同设备、不同构建行号下得到同一个值，用于会话内去重与后台聚合。
    /// 规则：异常类型（消息里第一个 ':' 之前）+ 前 <see cref="MaxFrames"/> 个栈帧（去掉 "(at 文件:行)"、
    /// IL 偏移 "[0x..]"、" in &lt;..&gt;:0" 等随构建变化的部分）做 FNV-1a；无栈时退化为"去数字的消息"哈希
    /// （消息里常带实例 id / 坐标，去数字后才可聚合）。
    /// </summary>
    public static class CrashFingerprint
    {
        public const int MaxFrames = 12;

        private const uint FnvOffset = 2166136261u;
        private const uint FnvPrime = 16777619u;

        public static uint Compute(string message, string stackTrace)
        {
            uint hash = FnvOffset;
            string msg = message ?? string.Empty;
            int colon = msg.IndexOf(':');
            if (string.IsNullOrEmpty(stackTrace))
            {
                return HashDigitless(hash, msg, 0, msg.Length);
            }

            hash = HashDigitless(hash, msg, TypeTokenStart(msg, colon), colon >= 0 ? colon : Math.Min(msg.Length, 64));
            int frames = 0;
            int pos = 0;
            while (pos < stackTrace.Length && frames < MaxFrames)
            {
                int end = stackTrace.IndexOf('\n', pos);
                if (end < 0) end = stackTrace.Length;
                int start = pos;
                pos = end + 1;

                int cut = NormalizedEnd(stackTrace, start, end);
                while (start < cut && char.IsWhiteSpace(stackTrace[start])) start++;
                if (start >= cut) continue;
                if (StartsWith(stackTrace, start, cut, "at ")) start += 3;
                hash = Hash(hash, "\n", 0, 1);
                hash = Hash(hash, stackTrace, start, cut);
                frames++;
            }

            return hash;
        }

        /// <summary>只对消息求指纹（去数字），用于遥测里"同类消息"聚合。</summary>
        public static uint ComputeMessage(string message)
        {
            string msg = message ?? string.Empty;
            return HashDigitless(FnvOffset, msg, 0, msg.Length);
        }

        // 异常类型 token 起点：':' 之前最后一个空格 / '|' / '.' 之后——
        // "UnhandledException | System.IO.IOException: x"、"System.IO.IOException: x"、"IOException: x" 都落到 "IOException"
        private static int TypeTokenStart(string msg, int colon)
        {
            if (colon <= 0) return 0;
            for (int i = colon - 1; i >= 0; i--)
            {
                char c = msg[i];
                if (c == ' ' || c == '|' || c == '.') return i + 1;
            }

            return 0;
        }

        // 行内第一个随构建变化的片段处截断：" (at "、" [0x"、" in <"；再去掉尾部空白
        private static int NormalizedEnd(string s, int start, int end)
        {
            int cut = end;
            cut = CutAt(s, start, cut, " (at ");
            cut = CutAt(s, start, cut, " [0x");
            cut = CutAt(s, start, cut, " in <");
            while (cut > start && char.IsWhiteSpace(s[cut - 1])) cut--;
            return cut;
        }

        private static int CutAt(string s, int start, int end, string token)
        {
            int idx = s.IndexOf(token, start, end - start, StringComparison.Ordinal);
            return idx >= 0 ? idx : end;
        }

        private static bool StartsWith(string s, int start, int end, string token)
        {
            return end - start >= token.Length && string.CompareOrdinal(s, start, token, 0, token.Length) == 0;
        }

        private static uint Hash(uint hash, string s, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                hash = (hash ^ (byte)c) * FnvPrime;
                hash = (hash ^ (byte)(c >> 8)) * FnvPrime;
            }

            return hash;
        }

        private static uint HashDigitless(uint hash, string s, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') continue;
                hash = (hash ^ (byte)c) * FnvPrime;
                hash = (hash ^ (byte)(c >> 8)) * FnvPrime;
            }

            return hash;
        }
    }

    /// <summary>
    /// 崩溃报告上传器（HTTP / 第三方 SDK）。<see cref="Upload"/> 在主线程调用，完成后在任意线程调用
    /// <paramref name="onComplete"/>(report, success)。成功后管理器删除本地文件；失败保留，下个会话重试。
    /// </summary>
    public interface ICrashUploader
    {
        void Upload(CrashReport report, byte[] payload, Action<CrashReport, bool> onComplete);
    }

    /// <summary>新崩溃报告监听（主线程回调；上次会话的异常退出报告在 BeginSession 后的第一次 Update 里回调）。</summary>
    public interface ICrashReportListener
    {
        void OnCrashReport(CrashReport report);
    }
}
