//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Fsm
{
    /// <summary>
    /// 有限状态机管理器。
    /// </summary>
    internal sealed class FsmManager : FrameworkModule, IFsmManager
    {
        private readonly Dictionary<TypeNamePair, FsmBase> m_Fsms;
        private readonly List<FsmBase> m_TempFsms;

        public FsmManager()
        {
            m_Fsms = new Dictionary<TypeNamePair, FsmBase>();
            m_TempFsms = new List<FsmBase>();
        }

        public override int Priority
        {
            // Priority 10：FSM tick 早于业务模块（Procedure 等）。
            get { return 10; }
        }

        public int Count
        {
            get { return m_Fsms.Count; }
        }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_TempFsms.Clear();
            if (m_Fsms.Count <= 0)
            {
                return;
            }

            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in m_Fsms)
            {
                m_TempFsms.Add(fsm.Value);
            }

            for (int i = 0; i < m_TempFsms.Count; i++)
            {
                FsmBase fsm = m_TempFsms[i];
                if (fsm.IsDestroyed) continue;

                try { fsm.Update(elapseSeconds, realElapseSeconds); }
                catch (Exception ex) { FrameworkLog.Error("FSM '{0}' Update threw: {1}", fsm.FullName, ex); }
            }
        }

        public override void Shutdown()
        {
            FsmBase[] toShutdown = new FsmBase[m_Fsms.Count];
            int idx = 0;
            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in m_Fsms)
            {
                toShutdown[idx++] = fsm.Value;
            }

            m_Fsms.Clear();
            m_TempFsms.Clear();

            for (int i = 0; i < toShutdown.Length; i++)
            {
                try { toShutdown[i].Shutdown(); }
                catch (Exception ex) { FrameworkLog.Error("FSM '{0}' Shutdown threw: {1}", toShutdown[i].FullName, ex); }
            }
        }

        public bool HasFsm<T>() where T : class
        {
            return m_Fsms.ContainsKey(new TypeNamePair(typeof(T)));
        }

        public bool HasFsm<T>(string name) where T : class
        {
            return m_Fsms.ContainsKey(new TypeNamePair(typeof(T), name));
        }

        public IFsm<T> GetFsm<T>() where T : class
        {
            return (IFsm<T>)InternalGetFsm(new TypeNamePair(typeof(T)));
        }

        public IFsm<T> GetFsm<T>(string name) where T : class
        {
            return (IFsm<T>)InternalGetFsm(new TypeNamePair(typeof(T), name));
        }

        public FsmBase[] GetAllFsms()
        {
            int index = 0;
            FsmBase[] results = new FsmBase[m_Fsms.Count];
            foreach (KeyValuePair<TypeNamePair, FsmBase> fsm in m_Fsms)
            {
                results[index++] = fsm.Value;
            }

            return results;
        }

        public IFsm<T> CreateFsm<T>(T owner, params FsmState<T>[] states) where T : class
        {
            return CreateFsm(string.Empty, owner, states);
        }

        public IFsm<T> CreateFsm<T>(string name, T owner, params FsmState<T>[] states) where T : class
        {
            Framework.EnsureMainThread(nameof(CreateFsm));
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (m_Fsms.ContainsKey(typeNamePair))
            {
                throw new FrameworkException(Utility.Text.Format("Already exist FSM '{0}'.", typeNamePair));
            }

            Fsm<T> fsm = Fsm<T>.Create(name, owner, states);
            m_Fsms.Add(typeNamePair, fsm);
            return fsm;
        }

        public IFsm<T> CreateFsm<T>(T owner, List<FsmState<T>> states) where T : class
        {
            return CreateFsm(string.Empty, owner, states);
        }

        public IFsm<T> CreateFsm<T>(string name, T owner, List<FsmState<T>> states) where T : class
        {
            Framework.EnsureMainThread(nameof(CreateFsm));
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (m_Fsms.ContainsKey(typeNamePair))
            {
                throw new FrameworkException(Utility.Text.Format("Already exist FSM '{0}'.", typeNamePair));
            }

            Fsm<T> fsm = Fsm<T>.Create(name, owner, states);
            m_Fsms.Add(typeNamePair, fsm);
            return fsm;
        }

        public bool DestroyFsm<T>() where T : class
        {
            return InternalDestroyFsm(new TypeNamePair(typeof(T)));
        }

        public bool DestroyFsm<T>(string name) where T : class
        {
            return InternalDestroyFsm(new TypeNamePair(typeof(T), name));
        }

        public bool DestroyFsm<T>(IFsm<T> fsm) where T : class
        {
            if (fsm == null)
            {
                throw new FrameworkException("FSM is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(typeof(T), fsm.Name));
        }

        public bool DestroyFsm(FsmBase fsm)
        {
            if (fsm == null)
            {
                throw new FrameworkException("FSM is invalid.");
            }

            return InternalDestroyFsm(new TypeNamePair(fsm.OwnerType, fsm.Name));
        }

        private FsmBase InternalGetFsm(TypeNamePair typeNamePair)
        {
            FsmBase fsm = null;
            if (m_Fsms.TryGetValue(typeNamePair, out fsm))
            {
                return fsm;
            }

            return null;
        }

        private bool InternalDestroyFsm(TypeNamePair typeNamePair)
        {
            Framework.EnsureMainThread(nameof(DestroyFsm));
            FsmBase fsm = null;
            if (m_Fsms.TryGetValue(typeNamePair, out fsm))
            {
                fsm.Shutdown();
                return m_Fsms.Remove(typeNamePair);
            }

            return false;
        }
    }
}
