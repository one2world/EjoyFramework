//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Input;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Input")]
    public sealed class InputComponent : GameFrameworkComponent
    {
        private IInputManager m_InputManager;
        public IInputManager Manager => m_InputManager;

        // 可选覆盖：可在 Awake 前后设置，以强制使用指定后端（如测试或平台特化）。
        private IInputHelper m_HelperOverride;

        protected override void Awake()
        {
            base.Awake();
            m_InputManager = Framework.GetModule<IInputManager>();
            if (m_InputManager == null) { Log.Fatal("Input manager is invalid."); return; }
            ConfigureManager();
        }

        private void ConfigureManager()
        {
            if (m_InputManager == null) return;
            m_InputManager.SetHelper(m_HelperOverride ?? CreateDefaultHelper());
        }

        /// <summary>覆盖默认后端；Awake 后调用会立即重新配置 InputManager。</summary>
        public void SetHelperOverride(IInputHelper helper)
        {
            m_HelperOverride = helper;
            if (m_InputManager != null) ConfigureManager();
        }

        private static IInputHelper CreateDefaultHelper()
        {
#if EJOY_INPUTSYSTEM
            // 安装了 New Input System：首选新后端。
            return new NewInputSystemHelper();
#else
            // 未安装新输入包：回退到旧版 UnityEngine.Input。
            return new LegacyInputHelper();
#endif
        }

        public void SetBinding(string actionName, InputBinding binding) => m_InputManager.SetBinding(actionName, binding);
        public float GetAxis(string actionName) => m_InputManager.GetAxis(actionName);
        public bool GetButton(string actionName) => m_InputManager.GetButton(actionName);
        public bool GetButtonDown(string actionName) => m_InputManager.GetButtonDown(actionName);
        public bool GetButtonUp(string actionName) => m_InputManager.GetButtonUp(actionName);
        public void PushContext(InputContext ctx) => m_InputManager.PushContext(ctx);
        public void PopContext() => m_InputManager.PopContext();
    }
}
