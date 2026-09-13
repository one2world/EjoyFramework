//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认日志辅助器。
    /// </summary>
    public class DefaultLogHelper : FrameworkLog.ILogHelper
    {
        /// <summary>
        /// 记录日志。
        /// </summary>
        /// <param name="level">游戏框架日志等级。</param>
        /// <param name="message">日志内容。</param>
        public void Log(LogLevel level, object message)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    UnityEngine.Debug.Log(Utility.Text.Format("<color=#888888>[DEBUG] {0}</color>", message));
                    break;
                case LogLevel.Info:
                    UnityEngine.Debug.Log(Utility.Text.Format("[INFO] {0}", message));
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(Utility.Text.Format("[WARNING] {0}", message));
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(Utility.Text.Format("[ERROR] {0}", message));
                    break;
                case LogLevel.Fatal:
                    UnityEngine.Debug.LogError(Utility.Text.Format("<color=#FF0000>[FATAL] {0}</color>", message));
                    break;
                default:
                    throw new FrameworkException(message.ToString());
            }
        }
    }
}
