//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 静态资源检查的构建门禁 + CI 入口。
    ///   • 构建前（callbackOrder=-900，晚于平台设置 -1000、早于 AB 构建）跑检查；
    ///     存在 Error 且 <see cref="AssetCheckConfig.FailBuildOnError"/> 时抛 BuildFailedException 阻断。
    ///   • <see cref="RunCheckCi"/> 供 -executeMethod 调用：batch 模式有 Error 即 Exit(1)。
    /// </summary>
    public sealed class AssetCheckBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -900;

        public void OnPreprocessBuild(BuildReport report)
        {
            AssetCheckConfig config = AssetCheckConfig.FindExisting();
            if (config == null || !config.FailBuildOnError) return;   // 未配置门禁 → 不阻断

            AssetCheckReport result = AssetChecker.RunAll(config);
            Debug.Log("[AssetCheck] Build gate — " + result);
            if (!result.HasErrors) return;

            var sb = new StringBuilder();
            sb.AppendLine("静态资源检查失败（" + result.ErrorCount + " 个 Error）：");
            foreach (AssetIssue issue in result.Issues)
            {
                if (issue.Severity != AssetCheckSeverity.Error) continue;
                sb.AppendLine("  - [" + issue.RuleId + "] " + issue.AssetPath + " : " + issue.Message);
            }
            throw new BuildFailedException(sb.ToString());
        }
    }

    /// <summary>CI / 命令行入口（仿 FrameworkBuildPipeline.BuildAllCi）。</summary>
    public static class AssetCheckCi
    {
        /// <summary>-executeMethod 目标：跑检查，batch 下有 Error → Exit(1)。</summary>
        public static void RunCheckCi()
        {
            AssetCheckConfig config = AssetCheckConfig.GetOrCreate();
            AssetCheckReport report = AssetChecker.RunAll(config);

            LogReport(report);

            if (report.HasErrors && Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        internal static void LogReport(AssetCheckReport report)
        {
            Debug.Log("[AssetCheck] " + report);
            for (int i = 0; i < report.Issues.Count; i++)
            {
                AssetIssue issue = report.Issues[i];
                string line = "[AssetCheck][" + issue.Severity + "][" + issue.RuleId + "] " + issue.AssetPath + " : " + issue.Message;
                switch (issue.Severity)
                {
                    case AssetCheckSeverity.Error: Debug.LogError(line); break;
                    case AssetCheckSeverity.Warning: Debug.LogWarning(line); break;
                    default: Debug.Log(line); break;
                }
            }
        }
    }
}
