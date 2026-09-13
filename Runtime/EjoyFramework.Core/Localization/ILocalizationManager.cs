//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Localization
{
    /// <summary>
    /// 本地化管理器接口。
    /// </summary>
    public interface ILocalizationManager
    {
        Language Language { get; set; }
        int DictionaryCount { get; }

        event EventHandler<LoadDictionarySuccessEventArgs> LoadDictionarySuccess;
        event EventHandler<LoadDictionaryFailureEventArgs> LoadDictionaryFailure;

        void SetLocalizationHelper(ILocalizationHelper helper);
        void LoadDictionary(string dictionaryAssetName, int priority, object userData);
        bool ParseDictionary(string text, object userData);

        string GetString(string key);
        string GetString(string key, params object[] args);
        bool HasRawString(string key);
        string GetRawString(string key);
        bool AddRawString(string key, string value);
        bool AddRawString(string key, string value, bool overwrite);
        bool RemoveRawString(string key);
        void RemoveAllRawStrings();
    }

    /// <summary>
    /// 本地化文本解析 helper。
    /// </summary>
    public interface ILocalizationHelper
    {
        bool ReadData(ILocalizationManager manager, string assetName, object asset, object userData);
        bool ParseData(ILocalizationManager manager, string text, object userData);
        void ReleaseDataAsset(object asset);
    }

    public enum Language : byte
    {
        Unspecified = 0,
        English,
        ChineseSimplified,
        ChineseTraditional,
        Japanese,
        Korean,
        Russian,
    }

    public sealed class LoadDictionarySuccessEventArgs : FrameworkEventArgs
    {
        public string DictionaryAssetName { get; private set; }
        public float Duration { get; private set; }
        public object UserData { get; private set; }

        public override void Clear() { DictionaryAssetName = null; Duration = 0f; UserData = null; }

        public static LoadDictionarySuccessEventArgs Create(string assetName, float duration, object userData)
        {
            var e = ReferencePool.Acquire<LoadDictionarySuccessEventArgs>();
            e.DictionaryAssetName = assetName;
            e.Duration = duration;
            e.UserData = userData;
            return e;
        }
    }

    public sealed class LoadDictionaryFailureEventArgs : FrameworkEventArgs
    {
        public string DictionaryAssetName { get; private set; }
        public string ErrorMessage { get; private set; }
        public object UserData { get; private set; }

        public override void Clear() { DictionaryAssetName = null; ErrorMessage = null; UserData = null; }

        public static LoadDictionaryFailureEventArgs Create(string assetName, string errorMessage, object userData)
        {
            var e = ReferencePool.Acquire<LoadDictionaryFailureEventArgs>();
            e.DictionaryAssetName = assetName;
            e.ErrorMessage = errorMessage;
            e.UserData = userData;
            return e;
        }
    }
}
