//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI.Mvvm;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// VM ICommand → Button.onClick + Button.interactable。
    /// Button 点击触发 cmd.Execute(parameter)；cmd.CanExecuteChanged 自动同步 interactable。
    /// </summary>
    [RequireComponent(typeof(Button))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Button")]
    public sealed class ButtonBinder : BinderBase
    {
        [SerializeField, Tooltip("传给 cmd.Execute 的参数。若为空，传 null。")]
        private string m_CommandParameter;

        private Button m_Target;
        private ICommand m_BoundCommand;

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<Button>();
            m_Target.onClick.AddListener(OnClick);
        }

        protected override void OnUnbind()
        {
            if (m_Target != null) m_Target.onClick.RemoveListener(OnClick);
            DetachCommand();
        }

        protected override void ApplyValue(object value)
        {
            DetachCommand();
            m_BoundCommand = value as ICommand;
            if (m_Target == null) m_Target = GetComponent<Button>();
            if (m_BoundCommand == null) { m_Target.interactable = false; return; }
            m_BoundCommand.CanExecuteChanged += OnCanExecuteChanged;
            m_Target.interactable = m_BoundCommand.CanExecute(GetParam());
        }

        protected override Type GetSourceType() { return typeof(ICommand); }

        private void OnClick()
        {
            if (m_BoundCommand == null) return;
            try { m_BoundCommand.Execute(GetParam()); }
            catch (Exception ex) { FrameworkLog.Error("[ButtonBinder] command Execute threw: {0}", ex); }
        }

        private void OnCanExecuteChanged()
        {
            if (m_Target == null || m_BoundCommand == null) return;
            m_Target.interactable = m_BoundCommand.CanExecute(GetParam());
        }

        private void DetachCommand()
        {
            if (m_BoundCommand != null) m_BoundCommand.CanExecuteChanged -= OnCanExecuteChanged;
            m_BoundCommand = null;
        }

        private object GetParam() { return string.IsNullOrEmpty(m_CommandParameter) ? null : m_CommandParameter; }
    }
}
