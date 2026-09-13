//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.UI
{
    /// <summary>每个 UIGroup 独立维护的无每次 Push 闭包分配导航栈。</summary>
    public sealed class UIStack : IDisposable
    {
        public readonly struct StackEntry
        {
            internal StackEntry(
                int formDefId,
                int serialId,
                IUIForm form,
                object userData,
                bool isValid)
            {
                FormDefId = formDefId;
                SerialId = serialId;
                Form = form;
                UserData = userData;
                IsValid = isValid;
            }

            public int FormDefId { get; }
            public int SerialId { get; }
            public IUIForm Form { get; }
            public object UserData { get; }
            public bool IsValid { get; }
        }

        private struct StackRecord
        {
            public int FormDefId;
            public int SerialId;
            public IUIForm Form;
            public object UserData;
        }

        private const int DefaultCapacity = 8;
        private readonly List<StackRecord> m_Stack = new List<StackRecord>(DefaultCapacity);
        private readonly UIController m_Controller;
        private readonly string m_GroupName;
        private bool m_Disposed;

        public UIStack(UIController controller, string groupName)
        {
            m_Controller = controller;
            m_GroupName = groupName;
            m_Controller.FormOpened += OnFormOpened;
            m_Controller.FormOpenFailed += OnFormOpenFailed;
        }

        public string GroupName { get { return m_GroupName; } }
        public int Count { get { return m_Stack.Count; } }
        public StackEntry Top
        {
            get
            {
                return m_Stack.Count > 0
                    ? Snapshot(m_Stack[m_Stack.Count - 1])
                    : default;
            }
        }

        public StackEntry Push(int formDefId, object userData = null)
        {
            ThrowIfDisposed();
            UIFormDef def = m_Controller.Registry.Get(formDefId);
            if (!string.Equals(def.GroupName, m_GroupName, StringComparison.Ordinal))
                throw new FrameworkException(Utility.Text.Format(
                    "UIFormDef '{0}' belongs to group '{1}' but pushed into stack '{2}'.",
                    formDefId, def.GroupName, m_GroupName));

            if (!def.AllowMultiple)
            {
                int existingIndex = FindIndexByFormDefId(formDefId);
                if (existingIndex >= 0)
                {
                    StackRecord existing = m_Stack[existingIndex];
                    if (existing.Form != null) m_Controller.RefocusUIForm(existing.Form);
                    return Snapshot(existing);
                }
            }

            int predictedSerial = m_Controller.Manager.PeekNextOpenUIFormSerial();
            var record = new StackRecord
            {
                FormDefId = formDefId,
                SerialId = predictedSerial,
                UserData = userData,
            };
            m_Stack.Add(record);

            int serial;
            try
            {
                serial = m_Controller.OpenByDef(def, userData);
            }
            catch
            {
                RemoveRecordBySerialId(predictedSerial, false);
                throw;
            }

            if (serial != predictedSerial)
            {
                RemoveRecordBySerialId(predictedSerial, false);
                throw new FrameworkException(Utility.Text.Format(
                    "UI serial changed during stack push. Expected '{0}', actual '{1}'.",
                    predictedSerial, serial));
            }

            int index = FindIndexBySerialId(serial);
            return index >= 0 ? Snapshot(m_Stack[index]) : Snapshot(record);
        }

        public bool Pop()
        {
            ThrowIfDisposed();
            if (m_Stack.Count == 0) return false;
            int index = m_Stack.Count - 1;
            StackRecord top = m_Stack[index];
            m_Stack.RemoveAt(index);

            if (top.SerialId >= 0) m_Controller.CloseUIForm(top.SerialId);
            RefocusTop();
            return true;
        }

        public int Back(int steps = 1)
        {
            int popped = 0;
            for (int i = 0; i < steps; i++)
            {
                if (!Pop()) break;
                popped++;
            }
            return popped;
        }

        public void Replace(int formDefId, object userData = null)
        {
            Pop();
            Push(formDefId, userData);
        }

        public int PopUntil(int formDefId)
        {
            int popped = 0;
            while (m_Stack.Count > 0 && m_Stack[m_Stack.Count - 1].FormDefId != formDefId)
            {
                if (!Pop()) break;
                popped++;
            }
            return popped;
        }

        public void ClearAll()
        {
            while (m_Stack.Count > 0) Pop();
        }

        public StackEntry FindByFormDefId(int formDefId)
        {
            int index = FindIndexByFormDefId(formDefId);
            return index >= 0 ? Snapshot(m_Stack[index]) : default;
        }

        internal bool RemoveBySerialId(int serialId)
        {
            return RemoveRecordBySerialId(serialId, true);
        }

        internal void CollectLoadingSerialIds(List<int> serialIds)
        {
            for (int i = 0; i < m_Stack.Count; i++)
            {
                StackRecord entry = m_Stack[i];
                if (entry.Form == null && entry.SerialId >= 0) serialIds.Add(entry.SerialId);
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Controller.FormOpened -= OnFormOpened;
            m_Controller.FormOpenFailed -= OnFormOpenFailed;
            m_Stack.Clear();
        }

        private void OnFormOpened(int serialId)
        {
            int index = FindIndexBySerialId(serialId);
            if (index < 0) return;
            StackRecord record = m_Stack[index];
            record.Form = m_Controller.GetUIForm(serialId);
            m_Stack[index] = record;
        }

        private void OnFormOpenFailed(int serialId, string error)
        {
            RemoveRecordBySerialId(serialId, false);
        }

        private bool RemoveRecordBySerialId(int serialId, bool refocus)
        {
            int index = FindIndexBySerialId(serialId);
            if (index < 0) return false;
            bool wasTop = index == m_Stack.Count - 1;
            m_Stack.RemoveAt(index);
            if (refocus && wasTop) RefocusTop();
            return true;
        }

        private int FindIndexByFormDefId(int formDefId)
        {
            for (int i = 0; i < m_Stack.Count; i++)
                if (m_Stack[i].FormDefId == formDefId) return i;
            return -1;
        }

        private int FindIndexBySerialId(int serialId)
        {
            for (int i = 0; i < m_Stack.Count; i++)
                if (m_Stack[i].SerialId == serialId) return i;
            return -1;
        }

        private void RefocusTop()
        {
            if (m_Stack.Count == 0) return;
            IUIForm form = m_Stack[m_Stack.Count - 1].Form;
            if (form != null) m_Controller.RefocusUIForm(form);
        }

        private static StackEntry Snapshot(StackRecord record)
        {
            return new StackEntry(
                record.FormDefId,
                record.SerialId,
                record.Form,
                record.UserData,
                true);
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(UIStack));
        }
    }
}
