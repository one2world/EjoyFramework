//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 游戏框架日志类。
    /// 易用：每个等级提供 (string)/(format,args)/(format,arg0)/(format,arg0,arg1)/(format,arg0,arg1,arg2) 重载，
    /// 避免 params 数组装箱。Debug/Info 通过 [Conditional] 在 Release 构建中被编译期裁剪，
    /// 业务层的字符串拼接调用也随之消失。
    /// </summary>
    public static class FrameworkLog
    {
        /// <summary>
        /// 日志辅助器接口。
        /// </summary>
        public interface ILogHelper
        {
            void Log(LogLevel level, object message);
        }

        // volatile 保证多线程下 SetLogHelper 的可见性。
        private static volatile ILogHelper s_LogHelper = null;
        // 仅打印不低于此等级的日志，运行时可调。
        private static volatile LogLevel s_MinLevel = LogLevel.Debug;

        public static LogLevel MinLevel
        {
            get { return s_MinLevel; }
            set { s_MinLevel = value; }
        }

        public static void SetLogHelper(ILogHelper logHelper)
        {
            s_LogHelper = logHelper;
        }

        // ===== Debug =====
        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(object message)
        {
            Write(LogLevel.Debug, message);
        }

        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string format, object arg0)
        {
            Write(LogLevel.Debug, format, arg0);
        }

        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string format, object arg0, object arg1)
        {
            Write(LogLevel.Debug, format, arg0, arg1);
        }

        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string format, object arg0, object arg1, object arg2)
        {
            Write(LogLevel.Debug, format, arg0, arg1, arg2);
        }

        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string format, params object[] args)
        {
            Write(LogLevel.Debug, format, args);
        }

        // ===== Info =====
        public static void Info(object message)
        {
            Write(LogLevel.Info, message);
        }

        public static void Info(string format, object arg0)
        {
            Write(LogLevel.Info, format, arg0);
        }

        public static void Info(string format, object arg0, object arg1)
        {
            Write(LogLevel.Info, format, arg0, arg1);
        }

        public static void Info(string format, object arg0, object arg1, object arg2)
        {
            Write(LogLevel.Info, format, arg0, arg1, arg2);
        }

        public static void Info(string format, params object[] args)
        {
            Write(LogLevel.Info, format, args);
        }

        // ===== Warning =====
        public static void Warning(object message) { Write(LogLevel.Warning, message); }
        public static void Warning(string format, object arg0) { Write(LogLevel.Warning, format, arg0); }
        public static void Warning(string format, object arg0, object arg1) { Write(LogLevel.Warning, format, arg0, arg1); }
        public static void Warning(string format, object arg0, object arg1, object arg2) { Write(LogLevel.Warning, format, arg0, arg1, arg2); }
        public static void Warning(string format, params object[] args) { Write(LogLevel.Warning, format, args); }

        // ===== Error =====
        public static void Error(object message) { Write(LogLevel.Error, message); }
        public static void Error(string format, object arg0) { Write(LogLevel.Error, format, arg0); }
        public static void Error(string format, object arg0, object arg1) { Write(LogLevel.Error, format, arg0, arg1); }
        public static void Error(string format, object arg0, object arg1, object arg2) { Write(LogLevel.Error, format, arg0, arg1, arg2); }
        public static void Error(string format, params object[] args) { Write(LogLevel.Error, format, args); }

        // ===== Fatal =====
        public static void Fatal(object message) { Write(LogLevel.Fatal, message); }
        public static void Fatal(string format, object arg0) { Write(LogLevel.Fatal, format, arg0); }
        public static void Fatal(string format, object arg0, object arg1) { Write(LogLevel.Fatal, format, arg0, arg1); }
        public static void Fatal(string format, object arg0, object arg1, object arg2) { Write(LogLevel.Fatal, format, arg0, arg1, arg2); }
        public static void Fatal(string format, params object[] args) { Write(LogLevel.Fatal, format, args); }

        // ===== 内部分发（不阻塞业务线程，每条日志 helper 异常自我隔离） =====
        private static void Write(LogLevel level, object message)
        {
            ILogHelper helper = s_LogHelper;
            if (helper == null || level < s_MinLevel)
            {
                return;
            }

            try { helper.Log(level, message); }
            catch (Exception ex) { FallbackTrace(level, ex, message); }
        }

        private static void Write(LogLevel level, string format, object arg0)
        {
            ILogHelper helper = s_LogHelper;
            if (helper == null || level < s_MinLevel)
            {
                return;
            }

            try { helper.Log(level, Utility.Text.Format(format, arg0)); }
            catch (Exception ex) { FallbackTrace(level, ex, format); }
        }

        private static void Write(LogLevel level, string format, object arg0, object arg1)
        {
            ILogHelper helper = s_LogHelper;
            if (helper == null || level < s_MinLevel)
            {
                return;
            }

            try { helper.Log(level, Utility.Text.Format(format, arg0, arg1)); }
            catch (Exception ex) { FallbackTrace(level, ex, format); }
        }

        private static void Write(LogLevel level, string format, object arg0, object arg1, object arg2)
        {
            ILogHelper helper = s_LogHelper;
            if (helper == null || level < s_MinLevel)
            {
                return;
            }

            try { helper.Log(level, Utility.Text.Format(format, arg0, arg1, arg2)); }
            catch (Exception ex) { FallbackTrace(level, ex, format); }
        }

        private static void Write(LogLevel level, string format, object[] args)
        {
            ILogHelper helper = s_LogHelper;
            if (helper == null || level < s_MinLevel)
            {
                return;
            }

            try { helper.Log(level, Utility.Text.Format(format, args)); }
            catch (Exception ex) { FallbackTrace(level, ex, format); }
        }

        // helper 抛异常时，避免日志吞掉日志本身（用 System.Console 兜底，永不二次抛）。
        private static void FallbackTrace(LogLevel level, Exception ex, object original)
        {
            try
            {
                System.Console.Error.WriteLine(string.Format("[FrameworkLog Helper failure] level={0}, original={1}, ex={2}", level, original, ex));
            }
            catch
            {
                // 最后的兜底：彻底吞掉，避免日志路径成为崩溃源。
            }
        }
    }
}
