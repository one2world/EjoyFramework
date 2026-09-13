//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core
{
    /// <summary>
    /// 游戏框架模块抽象类。public 方便业务层定义自定义 Manager 并通过 Framework.GetModule 访问。
    /// </summary>
    public abstract class FrameworkModule
    {
        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        /// <remarks>优先级较高的模块会优先轮询，并且关闭操作会后进行。</remarks>
        public virtual int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 该模块是否要求外部配置（如注入 Helper / Loader）才能正常工作。
        /// </summary>
        /// <remarks>默认 false，保持非破坏性：现有模块无需改动。需要 Helper/Loader 的模块覆写为 true。</remarks>
        public virtual bool RequiresConfiguration
        {
            get { return false; }
        }

        /// <summary>
        /// 该模块当前是否已完成必需配置。
        /// </summary>
        /// <remarks>默认 true（视为已配置）。覆写 <see cref="RequiresConfiguration"/> 的模块应据真实字段返回，例如 m_Helper != null。</remarks>
        public virtual bool IsModuleConfigured
        {
            get { return true; }
        }

        /// <summary>
        /// 当模块未配置时给出的修复提示（例如 "Call SetHelper(...) before use."）。
        /// </summary>
        /// <remarks>仅用于 Editor 下的一次性诊断日志，默认空串。</remarks>
        public virtual string ConfigurationHint
        {
            get { return string.Empty; }
        }

        // Framework 在 Editor 下首帧轮询时做一次"必需配置缺失"诊断；置位后不再重复检查，保证至多告警一次。
        internal bool m_ConfigChecked;

        /// <summary>
        /// 游戏框架模块轮询。
        /// </summary>
        public abstract void Update(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 游戏框架模块后期轮询（对应 Unity LateUpdate，在所有 Update 之后调用）。
        /// 默认空实现，需要后处理/跟随/IK 顺序的模块可覆写。
        /// </summary>
        public virtual void LateUpdate(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 游戏框架模块固定步长轮询（对应 Unity FixedUpdate，物理步长）。
        /// 默认空实现，物理耦合模块可覆写。
        /// </summary>
        public virtual void FixedUpdate(float fixedElapseSeconds, float realFixedElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理游戏框架模块。
        /// </summary>
        public abstract void Shutdown();
    }
}
