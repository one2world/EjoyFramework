//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

#if UNITY_2023_1_OR_NEWER   // UnityEngine.Awaitable 自 2023.1 起可用
using System;
using System.Threading;
using UnityEngine;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 异步现代化：把框架的回调 / handle 风格加载桥接为可 <c>await</c> 的 Unity <see cref="Awaitable"/>，并支持 <see cref="CancellationToken"/>。
    ///
    /// 设计：
    ///   - <b>附加式、不破坏</b>：纯扩展方法，不改任何 core 接口；旧回调 API 原样保留。
    ///   - <b>真取消</b>：token 取消时调用 <see cref="IAssetLoadHandle.Cancel"/>，把取消传导到底层 loader（不只是放弃等待）。
    ///   - <b>低 GC</b>：<see cref="Awaitable{T}"/> 由 Unity 池化、在主线程 PlayerLoop 上恢复，无 SynchronizationContext 开销；
    ///     冷路径（资源/场景/存档加载，低频）下 async 状态机的残留分配可忽略（热路径不应进入异步层）。
    ///
    /// 用法：
    /// <code>
    /// var res = Framework.GetModule&lt;IResourceManager&gt;();
    /// object asset = await res.LoadAssetAsync("Assets/UI/MainMenu.prefab", priority: 0, ct);
    /// </code>
    /// </summary>
    public static class AwaitableExtensions
    {
        /// <summary>
        /// 把资源加载 handle 包装成 <see cref="Awaitable{Object}"/>：
        /// Done → result(asset)，Cancelled → canceled，Failed → exception。
        /// </summary>
        public static Awaitable<object> ToAwaitable(this IAssetLoadHandle handle, CancellationToken cancellationToken = default)
        {
            if (handle == null) throw new FrameworkException("Asset load handle is null.");

            var source = new AwaitableCompletionSource<object>();
            CancellationTokenRegistration registration = default;   // default 上 Dispose 为 no-op

            void OnCompleted(IAssetLoadHandle h)
            {
                registration.Dispose();
                switch (h.Status)
                {
                    case LoadAssetStatus.Done:
                        source.TrySetResult(h.Asset);
                        break;
                    case LoadAssetStatus.Cancelled:
                        source.TrySetCanceled();
                        break;
                    default:
                        source.TrySetException(new FrameworkException(
                            Utility.Text.Format("Load '{0}' failed: {1} {2}", h.AssetName, h.FailureStatus, h.ErrorMessage)));
                        break;
                }
            }

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    try { handle.Cancel(); }
                    catch (Exception ex) { FrameworkLog.Warning("IAssetLoadHandle.Cancel on token threw: {0}", ex); }
                    source.TrySetCanceled();
                });
            }

            // 同步完成（如 EditorSimulationLoader）：立即落定，避免错过已触发的 Completed。
            if (handle.IsDone) OnCompleted(handle);
            else handle.Completed += OnCompleted;

            return source.Awaitable;
        }

        /// <summary>await 风格加载资源（取消会真正取消底层加载）。</summary>
        public static Awaitable<object> LoadAssetAsync(this IResourceManager resourceManager,
            string assetName, int priority = 0, CancellationToken cancellationToken = default)
        {
            if (resourceManager == null) throw new FrameworkException("Resource manager is null.");
            IAssetLoadHandle handle = resourceManager.LoadAssetWithHandle(assetName, priority, null);
            return handle.ToAwaitable(cancellationToken);
        }
    }
}
#endif
