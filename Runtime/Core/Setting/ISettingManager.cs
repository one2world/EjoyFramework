//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Setting
{
    /// <summary>
    /// 游戏配置管理器接口。
    /// </summary>
    public interface ISettingManager
    {
        /// <summary>
        /// 获取游戏配置项数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 检查是否存在指定游戏配置项。
        /// </summary>
        bool HasSetting(string settingName);

        /// <summary>
        /// 从指定游戏配置项中读取布尔值。
        /// </summary>
        bool GetBool(string settingName);

        /// <summary>
        /// 从指定游戏配置项中读取布尔值。
        /// </summary>
        bool GetBool(string settingName, bool defaultValue);

        /// <summary>
        /// 向指定游戏配置项写入布尔值。
        /// </summary>
        void SetBool(string settingName, bool value);

        /// <summary>
        /// 从指定游戏配置项中读取整数值。
        /// </summary>
        int GetInt(string settingName);

        /// <summary>
        /// 从指定游戏配置项中读取整数值。
        /// </summary>
        int GetInt(string settingName, int defaultValue);

        /// <summary>
        /// 向指定游戏配置项写入整数值。
        /// </summary>
        void SetInt(string settingName, int value);

        /// <summary>
        /// 从指定游戏配置项中读取浮点数值。
        /// </summary>
        float GetFloat(string settingName);

        /// <summary>
        /// 从指定游戏配置项中读取浮点数值。
        /// </summary>
        float GetFloat(string settingName, float defaultValue);

        /// <summary>
        /// 向指定游戏配置项写入浮点数值。
        /// </summary>
        void SetFloat(string settingName, float value);

        /// <summary>
        /// 从指定游戏配置项中读取字符串值。
        /// </summary>
        string GetString(string settingName);

        /// <summary>
        /// 从指定游戏配置项中读取字符串值。
        /// </summary>
        string GetString(string settingName, string defaultValue);

        /// <summary>
        /// 向指定游戏配置项写入字符串值。
        /// </summary>
        void SetString(string settingName, string value);

        /// <summary>
        /// 从指定游戏配置项中读取对象。
        /// </summary>
        T GetObject<T>(string settingName);

        /// <summary>
        /// 向指定游戏配置项写入对象。
        /// </summary>
        void SetObject<T>(string settingName, T obj);

        /// <summary>
        /// 移除指定游戏配置项。
        /// </summary>
        bool RemoveSetting(string settingName);

        /// <summary>
        /// 清空所有游戏配置项。
        /// </summary>
        void RemoveAllSettings();

        /// <summary>
        /// 保存游戏配置。成功返回 true；失败返回 false 且触发 SaveFailed 事件（实现类公开）。
        /// </summary>
        bool Save();
    }
}
