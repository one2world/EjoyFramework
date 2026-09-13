//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 已预载资产缓存（Pin 表）。
    ///
    /// 设计理由：
    ///   框架的加载管线全是异步回调，业务层在"战斗中生成子弹"这类热点路径上无法接受一次 LoadAsset 回调往返。
    ///   AssetBatch 在关卡进入前把整批资产预载完成，并把成功的资产 Pin 进本表；此后业务层可以用
    ///   IResourceManager.GetCachedAsset&lt;T&gt; 同步取到实例，零等待、零回调、零分配。
    ///
    /// 引用计数契约（关键，改动前务必读完）：
    ///   **一次 Pin 恰好对应一次 loader 侧的成功加载，因此也恰好需要一次 UnloadAsset。**
    ///   loader（AssetBundleLoader）对同一 asset 的每次成功加载都会把自己的 RefCount +1，
    ///   只有 RefCount 归零才会卸 bundle。所以本表不能"多次 Pin 只还一次"——那样 loader 的计数会永久卡在
    ///   高位，bundle 永不卸载。Unpin 每次调用都交出资产要求调用方卸载；PinCount 只用于决定
    ///   "何时把条目移出字典"（即何时让同步 Get 失效），不用于决定"是否卸载"。
    ///
    /// 线程契约：仅主线程。本类不加锁，调用方（ResourceManager / AssetBatch）已由 Framework.EnsureMainThread 守卫。
    /// </summary>
    internal sealed class AssetCache
    {
        /// <summary>
        /// 单条缓存记录：资产实例 + 当前 Pin 计数。
        /// </summary>
        private sealed class PinnedEntry
        {
            public object Asset;
            public int PinCount;
        }

        private readonly Dictionary<string, PinnedEntry> m_Entries = new Dictionary<string, PinnedEntry>(StringComparer.Ordinal);
        private bool m_Sealed;

        /// <summary>
        /// 当前被 Pin 住的资产数量。
        /// </summary>
        public int Count { get { return m_Entries.Count; } }

        /// <summary>
        /// 缓存是否已封存（ResourceManager 已 Shutdown）。封存后不再接受任何 Pin。
        /// </summary>
        public bool IsSealed { get { return m_Sealed; } }

        /// <summary>
        /// Pin 一份资产。
        /// </summary>
        /// <param name="redundantAsset">
        /// 需要由调用方立即 UnloadAsset 的实例；返回 true 时恒为 null。
        /// </param>
        /// <returns>
        /// true  = 本次成功增加了一个 Pin，调用方今后必须配对调用一次 Unpin。
        /// false = 本次没有增加 Pin（缓存已封存，或同名资产已缓存了另一个实例），
        ///         调用方**不得**记录 Pin，并需把 redundantAsset 卸载掉。
        /// </returns>
        public bool Pin(string assetName, object asset, out object redundantAsset)
        {
            if (string.IsNullOrEmpty(assetName)) throw new FrameworkException("Asset name is invalid.");
            if (asset == null) throw new FrameworkException("Cannot pin a null asset.");

            redundantAsset = null;

            // 封存后（Shutdown 之后）到达的迟到资产没人会再来解 Pin，直接退回给调用方卸载，
            // 否则它会永远留在表里，而 Shutdown 已经走过的卸载流程不会再跑第二次。
            if (m_Sealed)
            {
                redundantAsset = asset;
                return false;
            }

            PinnedEntry entry;
            if (m_Entries.TryGetValue(assetName, out entry))
            {
                // 同名但不同实例：loader 未做并发合并，这是两份独立的 loader 引用。
                // 保留先到者（已发出去的引用必须继续有效），后到者当场退还，且不计入 PinCount——
                // 否则调用方的 Unpin 会对先到者多还一次，造成过度卸载。
                if (!ReferenceEquals(entry.Asset, asset))
                {
                    redundantAsset = asset;
                    return false;
                }

                entry.PinCount++;
                return true;
            }

            m_Entries.Add(assetName, new PinnedEntry { Asset = asset, PinCount = 1 });
            return true;
        }

        /// <summary>
        /// 解 Pin。与 Pin 严格 1:1 配对。
        /// </summary>
        /// <param name="releasedAsset">
        /// 本次需要由调用方 UnloadAsset 的资产实例。**每次成功解 Pin 都会交出资产**（见类型注释的引用计数契约），
        /// 而不是只在计数归零时交出。
        /// </param>
        /// <returns>找到并解 Pin 了该资产则为 true；表中没有该条目则为 false（此时 releasedAsset 为 null）。</returns>
        public bool Unpin(string assetName, out object releasedAsset)
        {
            releasedAsset = null;
            if (string.IsNullOrEmpty(assetName)) return false;

            PinnedEntry entry;
            if (!m_Entries.TryGetValue(assetName, out entry)) return false;

            entry.PinCount--;
            releasedAsset = entry.Asset;

            // 计数归零：条目移出字典，同步 Get 从此失效。资产本身已在上面交给调用方卸载。
            if (entry.PinCount <= 0) m_Entries.Remove(assetName);
            return true;
        }

        /// <summary>
        /// 同步取已预载资产。未预载或类型不匹配返回 false。
        /// </summary>
        public bool TryGet<T>(string assetName, out T asset) where T : class
        {
            asset = null;
            if (string.IsNullOrEmpty(assetName)) return false;

            PinnedEntry entry;
            if (!m_Entries.TryGetValue(assetName, out entry)) return false;

            asset = entry.Asset as T;
            return asset != null;
        }

        /// <summary>
        /// 同步取已预载资产。未预载或类型不匹配抛 FrameworkException。
        /// </summary>
        public T Get<T>(string assetName) where T : class
        {
            if (string.IsNullOrEmpty(assetName)) throw new FrameworkException("Asset name is invalid.");

            PinnedEntry entry;
            if (!m_Entries.TryGetValue(assetName, out entry))
            {
                throw new FrameworkException(string.Format(
                    "Asset '{0}' is not preloaded. Add it to an AssetBatch and wait for the batch to complete before calling GetCachedAsset.",
                    assetName));
            }

            T typed = entry.Asset as T;
            if (typed == null)
            {
                throw new FrameworkException(string.Format(
                    "Preloaded asset '{0}' is of type '{1}', which cannot be cast to '{2}'.",
                    assetName,
                    entry.Asset != null ? entry.Asset.GetType().FullName : "<null>",
                    typeof(T).FullName));
            }

            return typed;
        }

        /// <summary>
        /// 查询指定资产当前的 Pin 计数（0 表示未缓存）。统计/测试用。
        /// </summary>
        public int GetPinCount(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return 0;
            PinnedEntry entry;
            return m_Entries.TryGetValue(assetName, out entry) ? entry.PinCount : 0;
        }

        /// <summary>
        /// 清空整张表并封存（Shutdown 用）。
        /// 每条记录按 **PinCount 次** 调用 loader.UnloadAsset，与 loader 自身的引用计数对齐；
        /// 封存后一切 Pin 都会被拒绝（迟到资产直接退回调用方卸载）。
        /// </summary>
        public void ClearAll(IResourceLoader loader)
        {
            if (loader != null)
            {
                foreach (KeyValuePair<string, PinnedEntry> kv in m_Entries)
                {
                    PinnedEntry entry = kv.Value;
                    if (entry.Asset == null) continue;
                    for (int i = 0; i < entry.PinCount; i++)
                    {
                        try { loader.UnloadAsset(entry.Asset); }
                        catch (Exception ex) { FrameworkLog.Warning("AssetCache.ClearAll unload threw for '{0}': {1}", kv.Key, ex); }
                    }
                }
            }

            m_Entries.Clear();
            m_Sealed = true;
        }
    }
}
