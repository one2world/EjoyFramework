//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.UI
{
    /// <summary>
    /// UI 高层控制器（业务最常用的入口）。
    /// 职责：
    ///   1) 持有 UIFormRegistry（资源映射 + 元数据）
    ///   2) 为每个 UIGroup 维护一个 UIStack（返回栈语义）
    ///   3) 维护 Modal 栈（一处统一管理"打开 modal 时阻塞下面输入"）
    ///   4) 把 OpenByDef 的失败回调跟 stack 自动 cleanup 联动
    /// 业务调用：
    ///   Open(UIFormId.Pause)   ← 走 stack push
    ///   Back()                  ← 关闭栈顶（最常见的 ESC 行为）
    ///   PopAllModal()           ← 一次性关闭所有 modal
    /// </summary>
    public sealed class UIController : IDisposable
    {
        internal event Action<int> FormOpened;
        internal event Action<int, string> FormOpenFailed;
        internal event Action<int> FormClosing;
        internal event Action<int> FormClosed;

        private readonly IUIManager m_UIManager;
        private readonly UIFormRegistry m_Registry;
        private readonly Dictionary<string, UIStack> m_StacksByGroup =
            new Dictionary<string, UIStack>(8, StringComparer.Ordinal);
        private readonly List<int> m_ModalSerials = new List<int>(8);
        // 通过弱关联：成功事件按 IUIForm 的 SerialId 反查 def
        private readonly Dictionary<int, UIFormDef> m_DefBySerial =
            new Dictionary<int, UIFormDef>(16);

        public UIController(IUIManager uiManager, UIFormRegistry registry)
        {
            m_UIManager = uiManager ?? throw new FrameworkException("UI manager is invalid.");
            m_Registry = registry ?? throw new FrameworkException("UI form registry is invalid.");
            m_UIManager.OpenUIFormSuccess += OnOpenSuccess;
            m_UIManager.OpenUIFormFailure += OnOpenFailure;
            m_UIManager.CloseUIFormComplete += OnCloseComplete;
        }

        /// <summary>
        /// 退订 UIManager 事件并清理内部状态。持有者（UIComponent）销毁时调用，
        /// 否则若 controller 被丢弃而 manager 仍存活，事件订阅会让 controller 无法回收（泄漏）。
        /// </summary>
        public void Dispose()
        {
            m_UIManager.OpenUIFormSuccess -= OnOpenSuccess;
            m_UIManager.OpenUIFormFailure -= OnOpenFailure;
            m_UIManager.CloseUIFormComplete -= OnCloseComplete;
            foreach (UIStack stack in m_StacksByGroup.Values) stack.Dispose();
            m_StacksByGroup.Clear();
            m_ModalSerials.Clear();
            m_DefBySerial.Clear();
        }

        public IUIManager Manager { get { return m_UIManager; } }
        public UIFormRegistry Registry { get { return m_Registry; } }

        /// <summary>获取/创建该 group 的 stack。</summary>
        public UIStack GetStack(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) throw new FrameworkException("Group name is invalid.");
            UIStack s;
            if (!m_StacksByGroup.TryGetValue(groupName, out s))
            {
                s = new UIStack(this, groupName);
                m_StacksByGroup.Add(groupName, s);
            }
            return s;
        }

        /// <summary>业务最常用：根据 def id 自动入对应 group 的栈。</summary>
        public UIStack.StackEntry Open(int formDefId, object userData = null)
        {
            UIFormDef def = m_Registry.Get(formDefId);
            return GetStack(def.GroupName).Push(formDefId, userData);
        }

        /// <summary>关闭某 group 栈顶（业务 ESC 按键最常用）。</summary>
        public bool Back(string groupName)
        {
            UIStack s;
            return m_StacksByGroup.TryGetValue(groupName, out s) && s.Pop();
        }

        /// <summary>
        /// 全局 Back：先关 modal，再按 group depth 倒序找首个非空 stack 的栈顶。
        /// 业务"按 ESC 关最顶层"一行搞定：if (Input.GetKeyDown(KeyCode.Escape)) GameEntry.UI.Back();
        /// </summary>
        public bool Back()
        {
            if (IsAnyModalOpen)
            {
                int top = m_ModalSerials[m_ModalSerials.Count - 1];
                if (m_UIManager.HasUIForm(top))
                {
                    CloseUIForm(top);
                    return true;
                }
            }

            UIStack winner = null;
            int winnerDepth = int.MinValue;
            foreach (var kv in m_StacksByGroup)
            {
                if (kv.Value.Count == 0) continue;
                var group = m_UIManager.GetUIGroup(kv.Key);
                int depth = group != null ? group.Depth : 0;
                if (depth > winnerDepth) { winnerDepth = depth; winner = kv.Value; }
            }
            return winner != null && winner.Pop();
        }

        /// <summary>关闭某 group 内所有 form（切场景前批量清理用）。</summary>
        public void CloseAllInGroup(string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return;
            UIStack s;
            if (m_StacksByGroup.TryGetValue(groupName, out s)) s.ClearAll();

            var all = m_UIManager.GetAllLoadedUIForms();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].UIGroup != null && all[i].UIGroup.Name == groupName)
                {
                    if (m_UIManager.HasUIForm(all[i].SerialId))
                        CloseUIForm(all[i].SerialId);
                }
            }
        }

        /// <summary>关闭所有 modal（同步 Stack 状态）。</summary>
        public void PopAllModal()
        {
            int[] copy = m_ModalSerials.ToArray();
            for (int i = copy.Length - 1; i >= 0; i--)
            {
                int serial = copy[i];
                if (!m_UIManager.HasUIForm(serial)) continue;

                CloseUIForm(serial);
            }
        }

        public bool IsAnyModalOpen { get { return m_ModalSerials.Count > 0; } }

        public IUIForm GetUIForm(int serialId) { return m_UIManager.GetUIForm(serialId); }
        public bool HasUIForm(int serialId) { return m_UIManager.HasUIForm(serialId); }
        public void CloseUIForm(int serialId) { CloseUIForm(serialId, null); }
        public void CloseUIForm(int serialId, object userData)
        {
            RemoveFromStacks(serialId);
            FormClosing?.Invoke(serialId);
            m_UIManager.CloseUIForm(serialId, userData);
        }
        public void CloseAllLoadedUIForms()
        {
            IUIForm[] forms = m_UIManager.GetAllLoadedUIForms();
            for (int i = 0; i < forms.Length; i++) CloseUIForm(forms[i].SerialId);
        }
        public void CloseAllLoadingUIForms()
        {
            var serialIds = new List<int>();
            foreach (UIStack stack in m_StacksByGroup.Values) stack.CollectLoadingSerialIds(serialIds);
            for (int i = 0; i < serialIds.Count; i++) CloseUIForm(serialIds[i]);
            m_UIManager.CloseAllLoadingUIForms();
        }
        public void RefocusUIForm(IUIForm form) { m_UIManager.RefocusUIForm(form); }

        /// <summary>
        /// 内部 API：UIStack 调用此处真正发起 Open。
        /// success/failure 回调是同步的（事件链），但 OpenUIForm 本身可能异步加载。
        /// 这里通过 SerialId 路由回调。同步路径（实例池命中 / 同步资源加载）下，
        /// OpenUIForm 会在调用过程中触发 OnOpenSuccess，所以必须先 Peek serial 并预注册回调。
        /// </summary>
        internal int OpenByDef(UIFormDef def, object userData)
        {
            int predicted = m_UIManager.PeekNextOpenUIFormSerial();
            m_DefBySerial[predicted] = def;

            // 贯通 UIFormDef.Reusable：仅具体 UIManager 暴露带 reusable 的重载（非接口方法）。
            int serial;
            if (m_UIManager is UIManager concrete)
                serial = concrete.OpenUIForm(def.AssetPath, def.GroupName, def.Priority, def.PauseCoveredUIForm, def.Reusable, userData);
            else
                serial = m_UIManager.OpenUIForm(def.AssetPath, def.GroupName, def.Priority, def.PauseCoveredUIForm, userData);

            // EnsureMainThread 保证 PeekNextOpenUIFormSerial → OpenUIForm 之间无其它 Open 介入，
            // 故 serial 必然等于 predicted；预注册的回调键无需再迁移（此前的 re-key 分支不可达）。
            return serial;
        }

        // ===== 事件回调 =====

        private void OnOpenSuccess(object sender, OpenUIFormSuccessEventArgs e)
        {
            int serial = e.UIForm.SerialId;
            UIFormDef def;
            if (m_DefBySerial.TryGetValue(serial, out def))
            {
                if (def.Modal && !m_ModalSerials.Contains(serial)) m_ModalSerials.Add(serial);
            }

            FormOpened?.Invoke(serial);
        }

        private void OnOpenFailure(object sender, OpenUIFormFailureEventArgs e)
        {
            int serial = e.SerialId;
            m_DefBySerial.Remove(serial);

            FormOpenFailed?.Invoke(serial, e.ErrorMessage);
        }

        private void OnCloseComplete(object sender, CloseUIFormCompleteEventArgs e)
        {
            RemoveFromStacks(e.SerialId);
            m_ModalSerials.Remove(e.SerialId);
            m_DefBySerial.Remove(e.SerialId);
            FormClosed?.Invoke(e.SerialId);
        }

        private bool RemoveFromStacks(int serialId)
        {
            foreach (UIStack stack in m_StacksByGroup.Values)
            {
                if (stack.RemoveBySerialId(serialId)) return true;
            }
            return false;
        }
    }
}
