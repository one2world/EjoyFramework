//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Diagnostics;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 日志工具类（Unity 层简化 API）。Debug 走 [Conditional] 在 Release 中编译期裁剪。
    /// </summary>
    public static class Log
    {
        // ===== Debug =====
        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(object message) { FrameworkLog.Debug(message); }

        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string format, object arg0) { FrameworkLog.Debug(format, arg0); }

        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string format, object arg0, object arg1) { FrameworkLog.Debug(format, arg0, arg1); }

        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string format, object arg0, object arg1, object arg2) { FrameworkLog.Debug(format, arg0, arg1, arg2); }

        [Conditional("DEBUG"), Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string format, params object[] args) { FrameworkLog.Debug(format, args); }

        // ===== Info =====
        public static void Info(object message) { FrameworkLog.Info(message); }
        public static void Info(string format, object arg0) { FrameworkLog.Info(format, arg0); }
        public static void Info(string format, object arg0, object arg1) { FrameworkLog.Info(format, arg0, arg1); }
        public static void Info(string format, object arg0, object arg1, object arg2) { FrameworkLog.Info(format, arg0, arg1, arg2); }
        public static void Info(string format, params object[] args) { FrameworkLog.Info(format, args); }

        // ===== Warning =====
        public static void Warning(object message) { FrameworkLog.Warning(message); }
        public static void Warning(string format, object arg0) { FrameworkLog.Warning(format, arg0); }
        public static void Warning(string format, object arg0, object arg1) { FrameworkLog.Warning(format, arg0, arg1); }
        public static void Warning(string format, object arg0, object arg1, object arg2) { FrameworkLog.Warning(format, arg0, arg1, arg2); }
        public static void Warning(string format, params object[] args) { FrameworkLog.Warning(format, args); }

        // ===== Error =====
        public static void Error(object message) { FrameworkLog.Error(message); }
        public static void Error(string format, object arg0) { FrameworkLog.Error(format, arg0); }
        public static void Error(string format, object arg0, object arg1) { FrameworkLog.Error(format, arg0, arg1); }
        public static void Error(string format, object arg0, object arg1, object arg2) { FrameworkLog.Error(format, arg0, arg1, arg2); }
        public static void Error(string format, params object[] args) { FrameworkLog.Error(format, args); }

        // ===== Fatal =====
        public static void Fatal(object message) { FrameworkLog.Fatal(message); }
        public static void Fatal(string format, object arg0) { FrameworkLog.Fatal(format, arg0); }
        public static void Fatal(string format, object arg0, object arg1) { FrameworkLog.Fatal(format, arg0, arg1); }
        public static void Fatal(string format, object arg0, object arg1, object arg2) { FrameworkLog.Fatal(format, arg0, arg1, arg2); }
        public static void Fatal(string format, params object[] args) { FrameworkLog.Fatal(format, args); }
    }
}
