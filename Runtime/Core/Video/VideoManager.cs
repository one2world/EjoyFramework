//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Video
{
    /// <summary>
    /// 视频播放管理器（FrameworkModule 实现）。
    ///
    /// 职责：
    ///   1) 持有底层 <see cref="IVideoPlayerHelper"/>，统一播放 / 暂停 / 恢复 / 停止 / 跳过控制；
    ///   2) 把辅助器事件（Prepared / Completed / ErrorOccurred）转译为自身事件
    ///      （<see cref="OnVideoStarted"/> / <see cref="OnVideoCompleted"/> / <see cref="OnVideoError"/>）；
    ///   3) 维护单次播放注册的 onCompleted / onError 回调，完成 / 出错后触发并随即清空（避免跨次串扰）。
    ///
    /// 容错：未设置辅助器时所有控制方法都<b>优雅失败</b>（记日志 + 触发 OnVideoError / onError），绝不抛出。
    /// 非法入参（source 为空）走 <see cref="ArgumentException"/>。
    /// </summary>
    public sealed class VideoManager : FrameworkModule, IVideoManager
    {
        private IVideoPlayerHelper m_Helper;
        private bool m_Skippable = true;

        // 当前播放上下文。m_HasActive 标识是否存在"进行中且尚未结算（完成/出错/停止）"的播放，
        // 用于把辅助器事件与单次回调正确归属到本次播放，并防止重复结算。
        private bool m_HasActive;
        private string m_ActiveSource;
        private Action m_ActiveOnCompleted;
        private Action<string> m_ActiveOnError;

        /// <summary>构造管理器（公共无参，供工厂创建）。</summary>
        public VideoManager()
        {
        }

        // Priority 0：业务模块，不参与依赖图排序。
        public override int Priority { get { return 0; } }

        /// <summary>需要外部注入 Helper 才能工作。</summary>
        public override bool RequiresConfiguration { get { return true; } }

        /// <summary>是否已注入 Helper。</summary>
        public override bool IsModuleConfigured { get { return m_Helper != null; } }

        /// <summary>未配置时的修复提示。</summary>
        public override string ConfigurationHint
        {
            get { return "Call SetHelper(IVideoPlayerHelper) before use (e.g. add a VideoComponent)."; }
        }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            // 转发给辅助器（多数实现自驱，可留空）；辅助器缺失时无事可做。
            m_Helper?.Update(elapseSeconds, realElapseSeconds);
        }

        public override void Shutdown()
        {
            // 停止播放并清理：先尽力停掉底层，再解绑事件、清空回调与辅助器引用。
            if (m_Helper != null)
            {
                try { m_Helper.Stop(); }
                catch (Exception ex) { FrameworkLog.Warning("VideoManager.Shutdown: helper.Stop threw: {0}", ex.Message); }
                DetachHelper(m_Helper);
            }

            m_Helper = null;
            ClearActive();

            OnVideoStarted = null;
            OnVideoCompleted = null;
            OnVideoError = null;
        }

        public bool IsPlaying
        {
            get { return m_Helper != null && m_Helper.IsPlaying; }
        }

        public bool Skippable
        {
            get { return m_Skippable; }
            set { m_Skippable = value; }
        }

        public void SetHelper(IVideoPlayerHelper helper)
        {
            Framework.EnsureMainThread(nameof(SetHelper));

            if (ReferenceEquals(m_Helper, helper))
            {
                return;
            }

            // 切换辅助器前结算/丢弃进行中的播放，避免旧辅助器的事件落到新上下文。
            if (m_Helper != null)
            {
                DetachHelper(m_Helper);
            }
            ClearActive();

            m_Helper = helper;
            if (m_Helper != null)
            {
                AttachHelper(m_Helper);
            }
        }

        public void Play(string source, bool loop = false, Action onCompleted = null, Action<string> onError = null)
        {
            Framework.EnsureMainThread(nameof(Play));

            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException("Video source is null or empty.", nameof(source));
            }

            if (m_Helper == null)
            {
                // 优雅失败：记日志 + 触发错误回调，绝不抛出。
                const string message = "No IVideoPlayerHelper set. Call SetHelper(...) first.";
                FrameworkLog.Error("VideoManager.Play('{0}') failed: {1}", source, message);
                onError?.Invoke(message);
                RaiseError(source, message);
                return;
            }

            // 新播放覆盖旧播放：丢弃旧的单次回调，不为其触发完成/错误（视为被打断）。
            ClearActive();

            m_HasActive = true;
            m_ActiveSource = source;
            m_ActiveOnCompleted = onCompleted;
            m_ActiveOnError = onError;

            try
            {
                m_Helper.Play(source, loop);
            }
            catch (Exception ex)
            {
                string message = "helper.Play threw: " + ex.Message;
                FrameworkLog.Error("VideoManager.Play('{0}') failed: {1}", source, message);
                FinishWithError(source, message);
                return;
            }

            RaiseStarted(source);
        }

        public void Stop()
        {
            Framework.EnsureMainThread(nameof(Stop));

            if (m_Helper == null)
            {
                ClearActive();
                return;
            }

            try { m_Helper.Stop(); }
            catch (Exception ex) { FrameworkLog.Warning("VideoManager.Stop: helper.Stop threw: {0}", ex.Message); }

            // 主动停止：不触发完成事件，也不触发单次 onCompleted，仅丢弃上下文。
            ClearActive();
        }

        public void Pause()
        {
            Framework.EnsureMainThread(nameof(Pause));

            if (m_Helper == null)
            {
                FrameworkLog.Warning("VideoManager.Pause ignored: no helper set.");
                return;
            }

            try { m_Helper.Pause(); }
            catch (Exception ex) { FrameworkLog.Warning("VideoManager.Pause: helper.Pause threw: {0}", ex.Message); }
        }

        public void Resume()
        {
            Framework.EnsureMainThread(nameof(Resume));

            if (m_Helper == null)
            {
                FrameworkLog.Warning("VideoManager.Resume ignored: no helper set.");
                return;
            }

            try { m_Helper.Resume(); }
            catch (Exception ex) { FrameworkLog.Warning("VideoManager.Resume: helper.Resume threw: {0}", ex.Message); }
        }

        public bool Skip()
        {
            Framework.EnsureMainThread(nameof(Skip));

            // 仅在允许跳过、且确有进行中的播放时生效。
            if (!m_Skippable || m_Helper == null || !m_HasActive)
            {
                return false;
            }

            string source = m_ActiveSource;

            try { m_Helper.Stop(); }
            catch (Exception ex) { FrameworkLog.Warning("VideoManager.Skip: helper.Stop threw: {0}", ex.Message); }

            // 跳过按"已完成"处理：触发完成事件与单次 onCompleted。
            FinishWithCompleted(source);
            return true;
        }

        // ===== 辅助器事件绑定 =====

        private void AttachHelper(IVideoPlayerHelper helper)
        {
            helper.Prepared += HandlePrepared;
            helper.Completed += HandleCompleted;
            helper.ErrorOccurred += HandleError;
        }

        private void DetachHelper(IVideoPlayerHelper helper)
        {
            helper.Prepared -= HandlePrepared;
            helper.Completed -= HandleCompleted;
            helper.ErrorOccurred -= HandleError;
        }

        // Prepared 暂不对外暴露事件（接口未定义）；保留处理点便于将来扩展 / 调试日志。
        private void HandlePrepared(IVideoPlayerHelper helper)
        {
            FrameworkLog.Debug("VideoManager: helper prepared for '{0}'.", m_ActiveSource);
        }

        private void HandleCompleted(IVideoPlayerHelper helper)
        {
            if (!m_HasActive)
            {
                return;
            }
            // 循环播放不应收到 Completed；若收到也按完成结算一次以免悬挂。
            FinishWithCompleted(m_ActiveSource);
        }

        private void HandleError(IVideoPlayerHelper helper, string message)
        {
            if (!m_HasActive)
            {
                // 无活动播放时的底层错误仅记录，不污染外部状态。
                FrameworkLog.Warning("VideoManager: helper error with no active playback: {0}", message);
                return;
            }
            FinishWithError(m_ActiveSource, message ?? "Unknown video error.");
        }

        // ===== 结算 =====

        // 完成结算：先快照并清空上下文，再触发事件与单次回调（保证回调内可安全发起下一次播放）。
        private void FinishWithCompleted(string source)
        {
            Action onCompleted = m_ActiveOnCompleted;
            ClearActive();

            RaiseCompleted(source);
            onCompleted?.Invoke();
        }

        // 错误结算：先快照并清空上下文，再触发事件与单次回调。
        private void FinishWithError(string source, string message)
        {
            Action<string> onError = m_ActiveOnError;
            ClearActive();

            RaiseError(source, message);
            onError?.Invoke(message);
        }

        private void ClearActive()
        {
            m_HasActive = false;
            m_ActiveSource = null;
            m_ActiveOnCompleted = null;
            m_ActiveOnError = null;
        }

        // ===== 事件触发（隔离订阅方异常，避免一个订阅者崩溃影响其余流程） =====

        private void RaiseStarted(string source)
        {
            try { OnVideoStarted?.Invoke(this, source); }
            catch (Exception ex) { FrameworkLog.Error("VideoManager.OnVideoStarted subscriber threw: {0}", ex.Message); }
        }

        private void RaiseCompleted(string source)
        {
            try { OnVideoCompleted?.Invoke(this, source); }
            catch (Exception ex) { FrameworkLog.Error("VideoManager.OnVideoCompleted subscriber threw: {0}", ex.Message); }
        }

        private void RaiseError(string source, string message)
        {
            try { OnVideoError?.Invoke(this, source, message); }
            catch (Exception ex) { FrameworkLog.Error("VideoManager.OnVideoError subscriber threw: {0}", ex.Message); }
        }

        /// <inheritdoc />
        public event Action<IVideoManager, string> OnVideoStarted;

        /// <inheritdoc />
        public event Action<IVideoManager, string> OnVideoCompleted;

        /// <inheritdoc />
        public event Action<IVideoManager, string, string> OnVideoError;
    }
}
