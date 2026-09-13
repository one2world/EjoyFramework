//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.Core.Setting
{
    /// <summary>
    /// 游戏配置持久化 helper 接口。
    /// 由 Unity 层注入具体实现（PlayerPrefs / 加密本地文件 / 云存档 ...）。
    /// </summary>
    public interface ISettingHelper
    {
        int Count { get; }
        bool Has(string name);
        bool TryGet(string name, out string value);
        void Set(string name, string value);
        bool Remove(string name);
        void RemoveAll();
        void Save();
        IEnumerable<string> Keys();
    }
}
