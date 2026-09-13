//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.RemoteConfig
{
    /// <summary>
    /// 远程配置管理器接口。
    /// 提供类型化的取值入口（string/int/bool/float），统一支持本地默认值与可注入的远程 Provider。
    /// 取值优先级：Provider 命中 &gt; 本地默认值 &gt; 调用方传入的 def。解析失败时回退到 def。
    /// </summary>
    public interface IRemoteConfigManager
    {
        /// <summary>
        /// 取字符串。未命中时返回 def。
        /// </summary>
        string GetString(string key, string def = "");

        /// <summary>
        /// 取整型。未命中或解析失败时返回 def。
        /// </summary>
        int GetInt(string key, int def = 0);

        /// <summary>
        /// 取布尔。未命中或解析失败时返回 def。
        /// </summary>
        bool GetBool(string key, bool def = false);

        /// <summary>
        /// 取浮点。未命中或解析失败时返回 def。
        /// </summary>
        float GetFloat(string key, float def = 0f);

        /// <summary>
        /// 设置远程配置 Provider。传 null 表示清除（回到仅本地默认值模式）。
        /// </summary>
        void SetProvider(IRemoteConfigProvider provider);

        /// <summary>
        /// 是否存在指定键（Provider 命中或本地默认值存在）。
        /// </summary>
        bool HasKey(string key);
    }

    /// <summary>
    /// 远程配置 Provider 接口。Unity 层或业务层实现，桥接具体远程配置 SDK（如 Firebase Remote Config）。
    /// </summary>
    public interface IRemoteConfigProvider
    {
        /// <summary>
        /// 尝试取出某键的原始字符串值。命中返回 true 并通过 rawValue 输出。
        /// </summary>
        bool TryGet(string key, out string rawValue);
    }
}
