//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// 统一代码生成器契约。所有 EjoyFramework.Core 代码生成器实现此接口后，
    /// 由 <see cref="CodeGenHub"/> 统一发现、在 <see cref="CodeGenWindow"/> 列出、并支持"一键全部生成"。
    ///
    /// 实现类应有公共无参构造（供 Hub 反射实例化），并保持 <see cref="Run"/> 幂等
    /// （借助 <see cref="CodeBuilder.WriteIfChanged"/>，内容不变不重写）。
    /// </summary>
    public interface ICodeGenerator
    {
        /// <summary>稳定唯一标识（用于排序/去重/记忆 UI 状态）。</summary>
        string Id { get; }

        /// <summary>面板显示名。</summary>
        string DisplayName { get; }

        /// <summary>面板显示的一句话说明。</summary>
        string Description { get; }

        /// <summary>执行生成。返回写入/未变更/跳过统计与消息。实现不应自行 AssetDatabase.Refresh（由 Hub 收尾统一刷新）。</summary>
        CodeGenResult Run();
    }

    /// <summary>
    /// 一次代码生成的结果汇总。
    /// </summary>
    public struct CodeGenResult
    {
        /// <summary>实际写入磁盘（内容有变更）的文件数。</summary>
        public int Written;

        /// <summary>内容未变更、跳过写入的文件数。</summary>
        public int Unchanged;

        /// <summary>因不支持/不满足条件而跳过的目标数（非错误）。</summary>
        public int Skipped;

        /// <summary>过程中的提示/告警消息（面板展开显示）。</summary>
        public string[] Messages;

        /// <summary>构造一个仅含消息的空结果。</summary>
        public static CodeGenResult Empty(params string[] messages)
        {
            return new CodeGenResult { Written = 0, Unchanged = 0, Skipped = 0, Messages = messages };
        }

        /// <summary>面板/日志用的单行摘要。</summary>
        public override string ToString()
        {
            return string.Format("{0} written, {1} unchanged, {2} skipped", Written, Unchanged, Skipped);
        }
    }
}
