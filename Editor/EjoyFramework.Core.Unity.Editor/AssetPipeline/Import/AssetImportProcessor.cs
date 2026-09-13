//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEditor;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 资源导入后处理器：仅在资产<b>首次导入</b>（尚无 .meta，<c>importSettingsMissing == true</c>）时，
    /// 按 <see cref="AssetImportRuleConfig"/> 自动套用导入预设——不覆盖开发者事后的手动改动，
    /// 重导入也不会二次套用。需要强制统一时走菜单批量（<see cref="AssetImportRulesApplier"/>）。
    ///
    /// 性能关键：导入回调是热路径（全量重导入时对每个资产触发），且在 AssetDatabase 操作期间
    /// <b>禁止</b>调用 <c>FindAssets</c>（会扫描全库、且导入期不安全）。因此这里只用固定路径
    /// <c>LoadAssetAtPath</c> 加载一次配置并缓存（含"无配置"判定缓存），由 <see cref="Invalidate"/>
    /// 在配置变更时失效。配置不在默认路径时，自动套用关闭——仍可用菜单批量 Apply。
    /// </summary>
    public sealed class AssetImportProcessor : AssetPostprocessor
    {
        private static bool s_Checked;
        private static AssetImportRuleConfig s_Config;
        private static AssetImportRuleResolver s_Resolver;

        /// <summary>配置变更后调用，令缓存失效（窗口/批量工具/导入到默认路径时触发）。</summary>
        public static void Invalidate()
        {
            s_Checked = false;
            s_Config = null;
            s_Resolver = null;
        }

        // 仅固定路径加载，绝不 FindAssets；结果（含 null）缓存，避免热路径重复磁盘/数据库访问。
        private static AssetImportRuleResolver GetResolver()
        {
            if (!s_Checked)
            {
                s_Checked = true;
                s_Config = AssetDatabase.LoadAssetAtPath<AssetImportRuleConfig>(AssetImportRuleConfig.DefaultAssetPath);
                s_Resolver = s_Config != null ? new AssetImportRuleResolver(s_Config) : null;
            }
            return s_Resolver;
        }

        private bool ShouldApply(out AssetImportRuleResolver resolver)
        {
            resolver = null;
            // 仅首次导入：已有序列化导入设置（.meta）的资产不自动改写。
            if (!assetImporter.importSettingsMissing) return false;
            resolver = GetResolver();
            if (resolver == null || s_Config == null || !s_Config.EnableAutoOnImport) return false;
            return true;
        }

        private void OnPreprocessTexture()
        {
            if (!ShouldApply(out AssetImportRuleResolver resolver)) return;
            AssetImportRule rule = resolver.Resolve(assetPath, AssetImportKind.Texture);
            if (rule != null) AssetImportApplyOps.ApplyTexture((TextureImporter)assetImporter, rule.Texture);
        }

        private void OnPreprocessAudio()
        {
            if (!ShouldApply(out AssetImportRuleResolver resolver)) return;
            AssetImportRule rule = resolver.Resolve(assetPath, AssetImportKind.Audio);
            if (rule != null) AssetImportApplyOps.ApplyAudio((AudioImporter)assetImporter, rule.Audio);
        }

        private void OnPreprocessModel()
        {
            if (!ShouldApply(out AssetImportRuleResolver resolver)) return;
            AssetImportRule rule = resolver.Resolve(assetPath, AssetImportKind.Model);
            if (rule != null) AssetImportApplyOps.ApplyModel((ModelImporter)assetImporter, rule.Model);
        }

        // 当配置资产被导入/修改时令缓存失效（仅检查路径相等，开销极小）。
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (HasConfigPath(imported) || HasConfigPath(deleted) || HasConfigPath(moved)) Invalidate();
        }

        private static bool HasConfigPath(string[] paths)
        {
            if (paths == null) return false;
            for (int i = 0; i < paths.Length; i++)
                if (paths[i] == AssetImportRuleConfig.DefaultAssetPath) return true;
            return false;
        }
    }
}
