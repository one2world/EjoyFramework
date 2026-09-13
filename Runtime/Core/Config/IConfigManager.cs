//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Config
{
    /// <summary>
    /// 全局配置管理器接口。
    /// </summary>
    public interface IConfigManager
    {
        /// <summary>
        /// 获取全局配置项数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 加载配置成功事件。
        /// </summary>
        event EventHandler<LoadConfigSuccessEventArgs> LoadConfigSuccess;

        /// <summary>
        /// 加载配置失败事件。
        /// </summary>
        event EventHandler<LoadConfigFailureEventArgs> LoadConfigFailure;

        /// <summary>
        /// 设置配置 helper（提供文本解析能力）。
        /// </summary>
        void SetConfigHelper(IConfigHelper helper);

        /// <summary>
        /// 异步加载配置资源（路径下应为 UTF-8 文本）。
        /// </summary>
        void LoadConfig(string configAssetName, int priority, object userData);

        /// <summary>
        /// 解析单段文本（同步），用于业务自有 TextAsset 或单测。
        /// </summary>
        bool ParseConfig(string text, object userData);

        /// <summary>
        /// 检查是否存在指定全局配置项。
        /// </summary>
        bool HasConfig(string configName);

        /// <summary>
        /// 从指定全局配置项中读取布尔值。
        /// </summary>
        bool GetBool(string configName);

        /// <summary>
        /// 从指定全局配置项中读取整数值。
        /// </summary>
        int GetInt(string configName);

        /// <summary>
        /// 从指定全局配置项中读取浮点数值。
        /// </summary>
        float GetFloat(string configName);

        /// <summary>
        /// 从指定全局配置项中读取字符串值。
        /// </summary>
        string GetString(string configName);

        /// <summary>
        /// 尝试读取布尔型配置项；存在返回 true 并经由 <paramref name="value"/> 输出，缺失返回 false 且 <paramref name="value"/> 为默认值。
        /// 用于区分“键不存在”与“键存在但值为 false”。
        /// </summary>
        bool TryGetBool(string configName, out bool value);

        /// <summary>
        /// 尝试读取整型配置项；存在返回 true 并经由 <paramref name="value"/> 输出，缺失返回 false 且 <paramref name="value"/> 为默认值。
        /// 用于区分“键不存在”与“键存在但值为 0”。
        /// </summary>
        bool TryGetInt(string configName, out int value);

        /// <summary>
        /// 尝试读取浮点型配置项；存在返回 true 并经由 <paramref name="value"/> 输出，缺失返回 false 且 <paramref name="value"/> 为默认值。
        /// 用于区分“键不存在”与“键存在但值为 0”。
        /// </summary>
        bool TryGetFloat(string configName, out float value);

        /// <summary>
        /// 尝试读取字符串型配置项；存在返回 true 并经由 <paramref name="value"/> 输出，缺失返回 false 且 <paramref name="value"/> 为 null。
        /// 用于区分“键不存在”与“键存在但值为空字符串”。
        /// </summary>
        bool TryGetString(string configName, out string value);

        /// <summary>
        /// 增加指定全局配置项。原始字符串以五元组传入。
        /// </summary>
        bool AddConfig(string configName, string configValue, string boolValue, string intValue, string floatValue);

        /// <summary>
        /// 移除指定全局配置项。
        /// </summary>
        bool RemoveConfig(string configName);

        /// <summary>
        /// 清空所有全局配置项。
        /// </summary>
        void RemoveAllConfigs();
    }

    /// <summary>
    /// 配置文本解析 helper。
    /// 文本格式由具体 helper 自定义；默认实现见 EjoyFramework.Core.Unity.DefaultConfigHelper。
    /// </summary>
    public interface IConfigHelper
    {
        /// <summary>
        /// 从加载得到的资源（Unity 中为 TextAsset，编辑器模拟下也可能是 string）中读取并填入 manager。
        /// </summary>
        bool ReadData(IConfigManager manager, string assetName, object asset, object userData);

        /// <summary>
        /// 解析纯文本到 manager。
        /// </summary>
        bool ParseData(IConfigManager manager, string text, object userData);

        /// <summary>
        /// 释放资源（通常通过 ResourceManager.UnloadAsset 回收 bundle 引用）。
        /// </summary>
        void ReleaseDataAsset(object asset);
    }

    /// <summary>
    /// 加载配置成功事件。
    /// </summary>
    public sealed class LoadConfigSuccessEventArgs : FrameworkEventArgs
    {
        public string ConfigAssetName { get; private set; }
        public float Duration { get; private set; }
        public object UserData { get; private set; }

        public override void Clear()
        {
            ConfigAssetName = null;
            Duration = 0f;
            UserData = null;
        }

        public static LoadConfigSuccessEventArgs Create(string assetName, float duration, object userData)
        {
            var e = ReferencePool.Acquire<LoadConfigSuccessEventArgs>();
            e.ConfigAssetName = assetName;
            e.Duration = duration;
            e.UserData = userData;
            return e;
        }
    }

    /// <summary>
    /// 加载配置失败事件。
    /// </summary>
    public sealed class LoadConfigFailureEventArgs : FrameworkEventArgs
    {
        public string ConfigAssetName { get; private set; }
        public string ErrorMessage { get; private set; }
        public object UserData { get; private set; }

        public override void Clear()
        {
            ConfigAssetName = null;
            ErrorMessage = null;
            UserData = null;
        }

        public static LoadConfigFailureEventArgs Create(string assetName, string errorMessage, object userData)
        {
            var e = ReferencePool.Acquire<LoadConfigFailureEventArgs>();
            e.ConfigAssetName = assetName;
            e.ErrorMessage = errorMessage;
            e.UserData = userData;
            return e;
        }
    }
}
