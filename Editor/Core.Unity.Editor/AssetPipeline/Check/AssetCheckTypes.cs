//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>问题严重度。仅 <see cref="AssetCheckSeverity.Error"/> 会阻断构建。</summary>
    public enum AssetCheckSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    /// <summary>检查类别（与四类规则对应）。</summary>
    public enum AssetCheckCategory
    {
        ImportSettings = 0,
        Naming = 1,
        Reference = 2,
        Size = 3,
    }

    /// <summary>单条检查问题。</summary>
    public struct AssetIssue
    {
        public AssetCheckSeverity Severity;
        public AssetCheckCategory Category;
        public string RuleId;
        public string AssetPath;
        public string Message;

        public static AssetIssue Make(AssetCheckSeverity severity, AssetCheckCategory category,
            string ruleId, string assetPath, string message)
        {
            return new AssetIssue
            {
                Severity = severity,
                Category = category,
                RuleId = ruleId,
                AssetPath = assetPath,
                Message = message,
            };
        }
    }

    /// <summary>一次检查的汇总报告。</summary>
    public sealed class AssetCheckReport
    {
        public readonly List<AssetIssue> Issues = new List<AssetIssue>();
        public int ScannedAssetCount;

        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public int InfoCount { get; private set; }

        public void Add(AssetIssue issue)
        {
            Issues.Add(issue);
            switch (issue.Severity)
            {
                case AssetCheckSeverity.Error: ErrorCount++; break;
                case AssetCheckSeverity.Warning: WarningCount++; break;
                default: InfoCount++; break;
            }
        }

        public bool HasErrors => ErrorCount > 0;

        public override string ToString()
        {
            return string.Format("{0} scanned — {1} error(s), {2} warning(s), {3} info",
                ScannedAssetCount, ErrorCount, WarningCount, InfoCount);
        }
    }

    /// <summary>检查规则契约。由 <see cref="AssetChecker"/> 反射发现（无参构造）。</summary>
    public interface IAssetCheckRule
    {
        string Id { get; }
        string DisplayName { get; }
        AssetCheckCategory Category { get; }
        IEnumerable<AssetIssue> Check(AssetCheckContext context, AssetCheckConfig config);
    }
}
