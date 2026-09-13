//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace EjoyFramework.Core.ObjectPool
{
    /// <summary>
    /// 对象信息。
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct ObjectInfo
    {
        private readonly string m_Name;
        private readonly bool m_Locked;
        private readonly bool m_CustomCanReleaseFlag;
        private readonly int m_Priority;
        private readonly DateTime m_LastUseTime;
        private readonly int m_SpawnCount;

        public ObjectInfo(string name, bool locked, bool customCanReleaseFlag, int priority, DateTime lastUseTime, int spawnCount)
        {
            m_Name = name;
            m_Locked = locked;
            m_CustomCanReleaseFlag = customCanReleaseFlag;
            m_Priority = priority;
            m_LastUseTime = lastUseTime;
            m_SpawnCount = spawnCount;
        }

        public string Name { get { return m_Name; } }
        public bool Locked { get { return m_Locked; } }
        public bool CustomCanReleaseFlag { get { return m_CustomCanReleaseFlag; } }
        public int Priority { get { return m_Priority; } }
        public DateTime LastUseTime { get { return m_LastUseTime; } }
        public int SpawnCount { get { return m_SpawnCount; } }
        public bool IsInUse { get { return m_SpawnCount > 0; } }
    }
}
