//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core
{
    public static partial class Utility
    {
        /// <summary>
        /// 字符相关的实用函数。
        /// 修复：
        ///   - 删除未使用的 [ThreadStatic] StringBuilder 死代码。
        ///   - s_TextHelper 改 volatile，多线程切换 helper 立即可见。
        ///   - ITextHelper 接口加 1/2/3 参重载，避免单参/双参调用经 params 装箱。
        /// </summary>
        public static class Text
        {
            public interface ITextHelper
            {
                string Format(string format, params object[] args);
                string Format(string format, object arg0);
                string Format(string format, object arg0, object arg1);
                string Format(string format, object arg0, object arg1, object arg2);
            }

            private static volatile ITextHelper s_TextHelper = null;

            public static void SetTextHelper(ITextHelper textHelper)
            {
                s_TextHelper = textHelper;
            }

            public static string Format(string format, params object[] args)
            {
                if (format == null) throw new FrameworkException("Format is invalid.");
                if (args == null || args.Length == 0) return format;
                ITextHelper helper = s_TextHelper;
                return helper != null ? helper.Format(format, args) : string.Format(format, args);
            }

            public static string Format(string format, object arg0)
            {
                if (format == null) throw new FrameworkException("Format is invalid.");
                ITextHelper helper = s_TextHelper;
                return helper != null ? helper.Format(format, arg0) : string.Format(format, arg0);
            }

            public static string Format(string format, object arg0, object arg1)
            {
                if (format == null) throw new FrameworkException("Format is invalid.");
                ITextHelper helper = s_TextHelper;
                return helper != null ? helper.Format(format, arg0, arg1) : string.Format(format, arg0, arg1);
            }

            public static string Format(string format, object arg0, object arg1, object arg2)
            {
                if (format == null) throw new FrameworkException("Format is invalid.");
                ITextHelper helper = s_TextHelper;
                return helper != null ? helper.Format(format, arg0, arg1, arg2) : string.Format(format, arg0, arg1, arg2);
            }

            public static string GetFullName<T>(string name)
            {
                return GetFullName(typeof(T), name);
            }

            public static string GetFullName(Type type, string name)
            {
                if (type == null)
                {
                    throw new FrameworkException("Type is invalid.");
                }

                string typeName = type.FullName;
                return string.IsNullOrEmpty(name) ? typeName : Format("{0}.{1}", typeName, name);
            }
        }
    }
}
