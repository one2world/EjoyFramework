//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 本次进程运行的会话标识与设备摘要。遥测（<see cref="TelemetryComponent"/>）与崩溃采集（DiagnosticsComponent）共用同一个 id，
    /// 后台可以把"这个会话的帧时间 / 内存曲线"和"它最后停在哪"对上。进入播放模式时重置（兼容关闭域重载）。
    /// 必须在主线程首次访问（设备摘要读 SystemInfo / Screen）。
    /// </summary>
    public static class AppSession
    {
        private static string s_Id;
        private static string s_DeviceSummary;

        /// <summary>会话 id（32 位十六进制）。</summary>
        public static string Id
        {
            get
            {
                if (s_Id == null) s_Id = Guid.NewGuid().ToString("N");
                return s_Id;
            }
        }

        /// <summary>设备摘要："型号|系统|GPU|内存MB|CPUx核数|分辨率"。</summary>
        public static string DeviceSummary
        {
            get
            {
                if (s_DeviceSummary == null)
                {
                    s_DeviceSummary = SystemInfo.deviceModel + "|" + SystemInfo.operatingSystem + "|" + SystemInfo.graphicsDeviceName + "|"
                                      + SystemInfo.systemMemorySize + "MB|" + SystemInfo.processorType + "x" + SystemInfo.processorCount
                                      + "|" + Screen.width + "x" + Screen.height;
                }

                return s_DeviceSummary;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            s_Id = null;
            s_DeviceSummary = null;
        }
    }
}
