//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认文本辅助器。
    /// </summary>
    public class DefaultTextHelper : Utility.Text.ITextHelper
    {
        public string Format(string format, params object[] args) { return string.Format(format, args); }
        public string Format(string format, object arg0) { return string.Format(format, arg0); }
        public string Format(string format, object arg0, object arg1) { return string.Format(format, arg0, arg1); }
        public string Format(string format, object arg0, object arg1, object arg2) { return string.Format(format, arg0, arg1, arg2); }
    }
}
