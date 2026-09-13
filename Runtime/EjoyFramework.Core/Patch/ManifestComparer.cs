//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core.Patch
{
    /// <summary>
    /// 比较本地 / 远程 manifest 计算待下载 bundle 集合。
    ///
    /// 规则：
    ///   - 远程有，本地没有 → 下载（new）
    ///   - 远程 md5 ≠ 本地 md5 → 下载（changed）
    ///   - 本地有，远程没有 → 可选清理（这里返回结果中不包含；业务可自行处理）
    /// </summary>
    public static class ManifestComparer
    {
        public static PatchCheckResult Compare(AssetManifest local, AssetManifest remote)
        {
            var result = new PatchCheckResult
            {
                LocalVersion = local != null ? local.Version : 0,
                RemoteVersion = remote != null ? remote.Version : 0,
            };
            if (remote == null || remote.Bundles == null || remote.Bundles.Count == 0)
                return result;

            // BundleInfo.Name → entry 字典快速 lookup local
            var localByName = new Dictionary<string, BundleInfo>();
            if (local != null && local.Bundles != null)
            {
                foreach (var b in local.Bundles)
                {
                    if (!string.IsNullOrEmpty(b.Name)) localByName[b.Name] = b;
                }
            }

            var pending = new List<PatchBundleInfo>();
            long totalSize = 0L;
            foreach (var rb in remote.Bundles)
            {
                if (string.IsNullOrEmpty(rb.Name)) continue;
                bool needDownload;
                if (!localByName.TryGetValue(rb.Name, out var lb))
                {
                    needDownload = true;   // 新增
                }
                else
                {
                    needDownload = !string.Equals(lb.Hash, rb.Hash, System.StringComparison.OrdinalIgnoreCase);
                }
                if (needDownload)
                {
                    pending.Add(new PatchBundleInfo
                    {
                        BundleName = rb.Name,
                        RelativePath = rb.RelativePath,
                        Md5 = rb.Md5,   // 真实文件 MD5（旧 manifest 为空时 PatchManager 跳过校验）；此前误用 rb.Hash(Unity AB hash) 导致校验恒失败
                        SizeBytes = rb.Size,
                    });
                    totalSize += rb.Size;
                }
            }

            result.PendingBundles = pending.ToArray();
            result.TotalSizeBytes = totalSize;
            result.HasUpdate = pending.Count > 0 || result.RemoteVersion > result.LocalVersion;
            return result;
        }
    }
}
