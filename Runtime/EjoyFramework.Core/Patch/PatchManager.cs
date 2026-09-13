//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Security.Cryptography;
using EjoyFramework.Core.Resource;
// ReSharper disable RedundantUsingDirective

namespace EjoyFramework.Core.Patch
{
    /// <summary>
    /// 资源热更管理器实现。串行下载（避免移动设备带宽抖动），按 PendingBundles 顺序逐个下载。
    ///
    /// 写入语义（原子 + 校验 + 提交）：
    ///   1) 每个 bundle 先下载到 &lt;dst&gt;.tmp，避免半截文件污染正式路径；
    ///   2) 完成后校验大小（SizeBytes&gt;0 时）与 MD5（Md5 非空时）；任一不符即视为失败、删除 tmp、不提交；
    ///   3) 校验通过后用 File.Replace 原子替换已有文件；首次落地则直接 Move；
    ///   4) 全部 bundle 成功后，把远程 manifest 写入 ReadWritePath/manifest.json（committed state），
    ///      下次启动 ManifestComparer 读取的是已落地的本地 manifest，避免重复下载/加载陈旧条目。
    /// </summary>
    internal sealed class PatchManager : FrameworkModule, IPatchManager
    {
        private const string ManifestFileName = "manifest.json";

        private IDownloadHelper m_DownloadHelper;
        private string m_ReadWritePath;
        private PatchProgress m_Progress;

        // 安全开关：
        //   m_RequireIntegrity（默认 true）：清单条目缺少完整性元数据（Md5 为空或 Size<=0）时判校验失败，
        //     防止“空 Md5/0 Size”这类降级攻击悄悄关闭完整性校验。设为 false 仅供受控测试 / 直写场景。
        //   m_AllowInsecureHttp（默认 false）：base URL 必须为 https://，否则拒绝下载；
        //     设为 true 仅供本地 / 局域网调试，会打印醒目告警。
        private bool m_RequireIntegrity = true;
        private bool m_AllowInsecureHttp = false;

        /// <summary>
        /// 无参构造函数。<b>必须保留</b>：本类型作为 FrameworkModule 经
        /// <c>Framework.GetModule&lt;IPatchManager&gt;()</c> 由 <c>Activator.CreateInstance</c> 解析，
        /// 而 Activator 仅识别真正的零参构造函数（带默认值的单参构造函数不算）。默认 m_RequireIntegrity=true。
        /// </summary>
        public PatchManager() : this(true)
        {
        }

        /// <summary>
        /// 构造函数。<paramref name="requireIntegrity"/> 默认 true：强制每个 bundle 的清单条目带有完整性元数据
        /// （非空 Md5 且 Size&gt;0），缺失即判失败。仅在受控测试 / helper 自负落地与校验的场景下传 false。
        /// </summary>
        public PatchManager(bool requireIntegrity = true)
        {
            m_RequireIntegrity = requireIntegrity;
        }

        // Priority 15：在 Resource (20) 之后、业务模块 (0) 之前，便于 ProcedureCheckVersion 调用。
        public override int Priority { get { return 15; } }
        public override void Update(float a, float b) { }
        public override void Shutdown() { }

        // 必需配置自检：依赖外部注入的 IDownloadHelper 才能执行任何下载（与 ResourceManager 对 IResourceLoader 的处理一致）。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_DownloadHelper != null; } }
        public override string ConfigurationHint { get { return "Call SetDownloadHelper(...) before use."; } }

        public PatchProgress CurrentProgress { get { return m_Progress; } }

        /// <summary>
        /// 是否要求清单条目带有完整性元数据（非空 Md5 且 Size&gt;0）。默认 true。
        /// 设为 false 会跳过“元数据缺失”这一硬性失败，并在校验时打印醒目告警；仅供受控测试 / 直写场景。
        /// </summary>
        public void SetRequireIntegrity(bool requireIntegrity)
        {
            Framework.EnsureMainThread(nameof(SetRequireIntegrity));
            m_RequireIntegrity = requireIntegrity;
        }

        /// <summary>
        /// 是否允许非 https 的 base URL。默认 false（仅允许 https://）。
        /// 设为 true 仅供本地 / 局域网调试，使用时会打印醒目告警。
        /// </summary>
        public void SetAllowInsecureHttp(bool allow)
        {
            Framework.EnsureMainThread(nameof(SetAllowInsecureHttp));
            m_AllowInsecureHttp = allow;
        }

        public void SetDownloadHelper(IDownloadHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetDownloadHelper));
            if (helper == null) throw new FrameworkException("Download helper is invalid.");
            m_DownloadHelper = helper;
        }

        public void SetReadWritePath(string path)
        {
            Framework.EnsureMainThread(nameof(SetReadWritePath));
            if (string.IsNullOrEmpty(path)) throw new FrameworkException("ReadWritePath is invalid.");
            m_ReadWritePath = path;
            if (!Directory.Exists(m_ReadWritePath)) Directory.CreateDirectory(m_ReadWritePath);
        }

        // 缓存最近一次 CheckUpdate 的远程 manifest：DownloadAsync 全部成功后据此提交本地 manifest。
        private AssetManifest m_PendingRemoteManifest;

        public PatchCheckResult CheckUpdate(AssetManifest local, AssetManifest remote)
        {
            Framework.EnsureMainThread(nameof(CheckUpdate));
            // 记住远程 manifest，下载完成后提交为本地 committed manifest。
            m_PendingRemoteManifest = remote;
            return ManifestComparer.Compare(local, remote);
        }

        public void DownloadAsync(string baseUrl, PatchBundleInfo[] bundles,
            Action<PatchProgress> onProgress,
            Action onComplete,
            Action<string> onError)
        {
            Framework.EnsureMainThread(nameof(DownloadAsync));
            if (m_DownloadHelper == null) { onError?.Invoke("PatchManager: download helper not set."); return; }

            // HTTPS 强制：base URL 必须为 https://，除非显式放行（m_AllowInsecureHttp）。
            // 防止热更资源走明文 http 被中间人篡改/替换。放行时打印醒目告警。
            string baseUrlError = ValidateBaseUrl(baseUrl);
            if (baseUrlError != null) { onError?.Invoke(baseUrlError); return; }

            if (bundles == null || bundles.Length == 0) { onComplete?.Invoke(); return; }

            m_Progress = new PatchProgress
            {
                CompletedBundleCount = 0,
                TotalBundleCount = bundles.Length,
                DownloadedBytes = 0L,
                TotalBytes = 0L,
                CurrentBundleProgress = 0f,
                CurrentBundleName = null,
            };
            long totalBytes = 0L;
            foreach (var b in bundles) totalBytes += b.SizeBytes;
            m_Progress.TotalBytes = totalBytes;

            DownloadNext(baseUrl, bundles, 0, onProgress, onComplete, onError);
        }

        private void DownloadNext(string baseUrl, PatchBundleInfo[] bundles, int index,
            Action<PatchProgress> onProgress,
            Action onComplete,
            Action<string> onError)
        {
            if (index >= bundles.Length)
            {
                // 全部 bundle 落地成功后提交本地 manifest，使下次启动看到 committed state。
                CommitLocalManifest(onError);
                onComplete?.Invoke();
                return;
            }
            var bundle = bundles[index];

            // C1 路径遍历防护：远程清单声明的 RelativePath 不可信，先做白名单语法校验，
            // 拒绝 ../、绝对路径、盘符等逃逸；再用 IsContainedIn 兜底确认落地路径仍在 ReadWritePath 内。
            // 命中可疑路径即让本次热更失败、绝不下载，避免写入根目录之外的任意位置。
            if (!Utility.Path.IsSafeRelativePath(bundle.RelativePath))
            {
                FrameworkLog.Error("PatchManager rejected unsafe bundle relative path '{0}' (bundle '{1}').",
                    bundle.RelativePath, bundle.BundleName);
                onError?.Invoke(string.Format("Bundle '{0}' has an unsafe relative path; download aborted.", bundle.BundleName));
                return;
            }

            string url = baseUrl.TrimEnd('/') + "/" + bundle.RelativePath;
            string localPath = Path.Combine(m_ReadWritePath, bundle.RelativePath);

            // belt-and-braces：Path.Combine 后再次确认目标在根目录内（规范化兜底）。
            if (!Utility.Path.IsContainedIn(m_ReadWritePath, localPath))
            {
                FrameworkLog.Error("PatchManager rejected bundle '{0}': combined path '{1}' escapes ReadWritePath '{2}'.",
                    bundle.BundleName, localPath, m_ReadWritePath);
                onError?.Invoke(string.Format("Bundle '{0}' resolves outside the patch directory; download aborted.", bundle.BundleName));
                return;
            }

            string localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir)) Directory.CreateDirectory(localDir);

            // 先下载到 .tmp，校验通过后再原子替换，避免半截文件污染正式路径。
            string tempPath = localPath + ".tmp";
            TryDeleteFile(tempPath);

            m_Progress.CurrentBundleName = bundle.BundleName;
            m_Progress.CurrentBundleProgress = 0f;
            onProgress?.Invoke(m_Progress);

            m_DownloadHelper.DownloadAsync(url, tempPath,
                onProgress: p =>
                {
                    m_Progress.CurrentBundleProgress = p;
                    onProgress?.Invoke(m_Progress);
                },
                onComplete: bytes =>
                {
                    // 校验 + 原子提交。失败则删除 tmp 并报错，绝不留下未校验/半截文件。
                    string commitError = VerifyAndCommitBundle(tempPath, localPath, bundle);
                    if (commitError != null)
                    {
                        TryDeleteFile(tempPath);
                        onError?.Invoke(string.Format("Bundle '{0}' {1}", bundle.BundleName, commitError));
                        return;
                    }

                    m_Progress.CompletedBundleCount = index + 1;
                    m_Progress.DownloadedBytes += bytes;
                    m_Progress.CurrentBundleProgress = 1f;
                    onProgress?.Invoke(m_Progress);
                    DownloadNext(baseUrl, bundles, index + 1, onProgress, onComplete, onError);
                },
                onError: err =>
                {
                    TryDeleteFile(tempPath);
                    onError?.Invoke(string.Format("Bundle '{0}' download failed: {1}", bundle.BundleName, err));
                });
        }

        /// <summary>
        /// 校验下载的临时文件（大小 + MD5），通过后原子替换到正式路径。
        /// 返回 null 表示成功；否则返回错误描述。
        ///
        /// C2 完整性降级防护：当 <see cref="m_RequireIntegrity"/> 为 true（默认）时，清单条目必须同时带有
        /// 非空 Md5 和 Size&gt;0，否则直接判失败（"manifest entry missing integrity metadata"），
        /// 防止远程清单用空 Md5 / 0 Size 悄悄关闭完整性校验。为 false 时仅打印醒目告警后跳过该硬性要求。
        /// </summary>
        private string VerifyAndCommitBundle(string tempPath, string localPath, PatchBundleInfo bundle)
        {
            // C2：先做完整性元数据存在性检查，且必须在 File.Exists 短路之前，
            // 避免“tmp 不存在”路径绕过元数据校验。
            bool hasMd5 = !string.IsNullOrEmpty(bundle.Md5);
            bool hasSize = bundle.SizeBytes > 0;
            if (!hasMd5 || !hasSize)
            {
                if (m_RequireIntegrity)
                {
                    return string.Format(
                        "manifest entry missing integrity metadata (Md5='{0}', Size={1}); rejecting per RequireIntegrity.",
                        bundle.Md5 ?? string.Empty, bundle.SizeBytes);
                }
                FrameworkLog.Warning(
                    "PatchManager INTEGRITY SKIPPED for bundle '{0}': manifest entry missing Md5/Size and RequireIntegrity is OFF. This is INSECURE.",
                    bundle.BundleName);
            }

            // 测试 / 直写场景：helper 未生成 tmp 文件（或直接写入目标）。
            // 此时无可校验文件，且没有显式校验诉求，则跳过校验与替换，交由 helper 负责落地。
            if (!File.Exists(tempPath)) return null;

            try
            {
                // 大小校验（SizeBytes > 0 时才校验）。
                if (bundle.SizeBytes > 0)
                {
                    long actual = new FileInfo(tempPath).Length;
                    if (actual != bundle.SizeBytes)
                        return string.Format("size mismatch: expected {0}, got {1}.", bundle.SizeBytes, actual);
                }

                // MD5 校验（Md5 非空时才校验）。不符即失败，绝不提交。
                if (!string.IsNullOrEmpty(bundle.Md5))
                {
                    string actualMd5 = ComputeMd5(tempPath);
                    if (!string.Equals(actualMd5, bundle.Md5, StringComparison.OrdinalIgnoreCase))
                        return string.Format("MD5 mismatch: expected {0}, got {1}.", bundle.Md5, actualMd5);
                }

                ReplaceFile(tempPath, localPath);
                return null;
            }
            catch (Exception ex)
            {
                return "verify/commit failed: " + ex.Message;
            }
        }

        /// <summary>
        /// 全部 bundle 成功后写入本地 manifest（committed state）。best-effort：
        /// 提交失败不回滚已落地的 bundle，仅记录日志，避免重复下载的同时不丢失已下载数据。
        /// </summary>
        private void CommitLocalManifest(Action<string> onError)
        {
            if (m_PendingRemoteManifest == null) return;
            try
            {
                string json = Utility.Json.ToJson(m_PendingRemoteManifest);
                if (string.IsNullOrEmpty(json)) return;
                WriteAllTextAtomic(Path.Combine(m_ReadWritePath, ManifestFileName), json);
                FrameworkLog.Info("PatchManager committed local manifest: {0}", m_ReadWritePath);
            }
            catch (Exception ex)
            {
                // manifest 提交是“避免重复下载”的优化，不应让整次热更判失败。
                FrameworkLog.Warning("PatchManager commit manifest failed (bundles are already on disk): {0}", ex.Message);
            }
        }

        /// <summary>
        /// 校验 base URL：要求以 https:// 开头；非 https 时除非 m_AllowInsecureHttp 显式放行，否则拒绝。
        /// 返回 null 表示通过；否则返回错误描述（供 onError 上报）。
        /// </summary>
        private string ValidateBaseUrl(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return "PatchManager: base URL is empty.";

            if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;

            if (m_AllowInsecureHttp)
            {
                FrameworkLog.Warning(
                    "PatchManager using INSECURE non-https base URL '{0}' because AllowInsecureHttp is ON. Patches are NOT protected against tampering.",
                    baseUrl);
                return null;
            }

            FrameworkLog.Error("PatchManager rejected non-https base URL '{0}'. Set AllowInsecureHttp to override (debug only).", baseUrl);
            return string.Format("PatchManager: insecure base URL '{0}' rejected (https required).", baseUrl);
        }

        private static string ComputeMd5(string path)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = md5.ComputeHash(stream);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static void WriteAllTextAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            ReplaceFile(temp, path);
        }

        private static void ReplaceFile(string temp, string dst)
        {
            if (File.Exists(dst))
            {
                File.Replace(temp, dst, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, dst);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception ex) { FrameworkLog.Warning("TryDeleteFile '{0}' threw: {1}", path, ex); }
        }
    }
}
