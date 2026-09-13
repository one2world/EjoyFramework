//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Fsm
{
    /// <summary>
    /// 有限状态机。
    /// </summary>
    /// <typeparam name="T">有限状态机持有者类型。</typeparam>
    internal sealed class Fsm<T> : FsmBase, IFsm<T>, IReference where T : class
    {
        private const int MaxDeferredStateChangesPerDrain = 64;

        private T m_Owner;
        private readonly Dictionary<Type, FsmState<T>> m_States;
        private readonly Queue<FsmState<T>> m_PendingStateChanges;
        private Dictionary<string, Variable> m_Datas;
        private FsmState<T> m_CurrentState;
        private float m_CurrentStateTime;
        private bool m_IsDestroyed;
        // OnLeave/OnEnter 中的链式 ChangeState 会入队，在当前切换完成后顺序执行。
        private bool m_IsChangingState;

        /// <summary>
        /// 初始化有限状态机的新实例。
        /// </summary>
        public Fsm()
        {
            m_Owner = null;
            m_States = new Dictionary<Type, FsmState<T>>();
            m_PendingStateChanges = new Queue<FsmState<T>>();
            m_Datas = null;
            m_CurrentState = null;
            m_CurrentStateTime = 0f;
            m_IsDestroyed = true;
            m_IsChangingState = false;
        }

        /// <summary>
        /// 获取有限状态机持有者。
        /// </summary>
        public T Owner
        {
            get
            {
                return m_Owner;
            }
        }

        /// <summary>
        /// 获取有限状态机持有者类型。
        /// </summary>
        public override Type OwnerType
        {
            get
            {
                return typeof(T);
            }
        }

        /// <summary>
        /// 获取有限状态机中状态的数量。
        /// </summary>
        public override int FsmStateCount
        {
            get
            {
                return m_States.Count;
            }
        }

        /// <summary>
        /// 获取有限状态机是否正在运行。
        /// </summary>
        public override bool IsRunning
        {
            get
            {
                return m_CurrentState != null;
            }
        }

        /// <summary>
        /// 获取有限状态机是否被销毁。
        /// </summary>
        public override bool IsDestroyed
        {
            get
            {
                return m_IsDestroyed;
            }
        }

        /// <summary>
        /// 获取当前有限状态机状态。
        /// </summary>
        public FsmState<T> CurrentState
        {
            get
            {
                return m_CurrentState;
            }
        }

        /// <summary>
        /// 获取当前有限状态机状态名称。
        /// </summary>
        public override string CurrentStateName
        {
            get
            {
                return m_CurrentState != null ? m_CurrentState.GetType().FullName : null;
            }
        }

        /// <summary>
        /// 获取当前有限状态机状态持续时间。
        /// </summary>
        public override float CurrentStateTime
        {
            get
            {
                return m_CurrentStateTime;
            }
        }

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        public static Fsm<T> Create(string name, T owner, params FsmState<T>[] states)
        {
            if (owner == null)
            {
                throw new FrameworkException("FSM owner is invalid.");
            }

            if (states == null || states.Length < 1)
            {
                throw new FrameworkException("FSM states is invalid.");
            }

            Fsm<T> fsm = ReferencePool.Acquire<Fsm<T>>();
            fsm.Name = name;
            fsm.m_Owner = owner;
            fsm.m_IsDestroyed = false;
            foreach (FsmState<T> state in states)
            {
                if (state == null)
                {
                    throw new FrameworkException("FSM states is invalid.");
                }

                Type stateType = state.GetType();
                if (fsm.m_States.ContainsKey(stateType))
                {
                    throw new FrameworkException(Utility.Text.Format("FSM '{0}' state '{1}' is already exist.", Utility.Text.GetFullName<T>(name), stateType.FullName));
                }

                fsm.m_States.Add(stateType, state);
                state.OnInit(fsm);
            }

            return fsm;
        }

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        public static Fsm<T> Create(string name, T owner, List<FsmState<T>> states)
        {
            if (owner == null)
            {
                throw new FrameworkException("FSM owner is invalid.");
            }

            if (states == null || states.Count < 1)
            {
                throw new FrameworkException("FSM states is invalid.");
            }

            Fsm<T> fsm = ReferencePool.Acquire<Fsm<T>>();
            fsm.Name = name;
            fsm.m_Owner = owner;
            fsm.m_IsDestroyed = false;
            foreach (FsmState<T> state in states)
            {
                if (state == null)
                {
                    throw new FrameworkException("FSM states is invalid.");
                }

                Type stateType = state.GetType();
                if (fsm.m_States.ContainsKey(stateType))
                {
                    throw new FrameworkException(Utility.Text.Format("FSM '{0}' state '{1}' is already exist.", Utility.Text.GetFullName<T>(name), stateType.FullName));
                }

                fsm.m_States.Add(stateType, state);
                state.OnInit(fsm);
            }

            return fsm;
        }

        /// <summary>
        /// 清理引用。会触发当前状态的 OnLeave(isShutdown=true) 与所有状态的 OnDestroy，避免业务资源泄漏。
        /// </summary>
        public void Clear()
        {
            // 1) 给当前状态一次清理机会
            if (m_CurrentState != null)
            {
                FsmState<T> state = m_CurrentState;
                m_CurrentState = null;
                m_CurrentStateTime = 0f;
                try { state.OnLeave(this, true); }
                catch (Exception ex) { FrameworkLog.Error("FSM '{0}' state '{1}' OnLeave(shutdown) threw: {2}", FullName, state.GetType().FullName, ex); }
            }

            // 2) OnDestroy 通知所有已注册状态
            foreach (KeyValuePair<Type, FsmState<T>> kvp in m_States)
            {
                try { kvp.Value.OnDestroy(this); }
                catch (Exception ex) { FrameworkLog.Error("FSM '{0}' state '{1}' OnDestroy threw: {2}", FullName, kvp.Key.FullName, ex); }
            }

            m_Owner = null;
            m_States.Clear();
            m_PendingStateChanges.Clear();

            if (m_Datas != null)
            {
                foreach (KeyValuePair<string, Variable> data in m_Datas)
                {
                    if (data.Value == null) continue;
                    try { ReferencePool.Release(data.Value); }
                    catch (Exception ex) { FrameworkLog.Error("FSM '{0}' release data '{1}' threw: {2}", FullName, data.Key, ex); }
                }

                m_Datas.Clear();
            }

            m_IsChangingState = false;
            m_IsDestroyed = true;
        }

        /// <summary>
        /// 开始有限状态机。
        /// </summary>
        public void Start<TState>() where TState : FsmState<T>
        {
            Start(typeof(TState));
        }

        /// <summary>
        /// 开始有限状态机。
        /// </summary>
        public void Start(Type stateType)
        {
            if (IsRunning)
            {
                throw new FrameworkException("FSM is running, can not start again.");
            }

            if (stateType == null)
            {
                throw new FrameworkException("State type is invalid.");
            }

            FsmState<T> state = GetState(stateType);
            if (state == null)
            {
                throw new FrameworkException(Utility.Text.Format("FSM '{0}' can not start state '{1}' which is not exist.", FullName, stateType.FullName));
            }

            m_CurrentStateTime = 0f;
            m_CurrentState = state;
            m_IsChangingState = true;
            try
            {
                m_CurrentState.OnEnter(this);
            }
            catch
            {
                m_PendingStateChanges.Clear();
                throw;
            }
            finally
            {
                m_IsChangingState = false;
            }

            DrainPendingStateChanges();
        }

        /// <summary>
        /// 是否存在有限状态机状态。
        /// </summary>
        public bool HasState<TState>() where TState : FsmState<T>
        {
            return m_States.ContainsKey(typeof(TState));
        }

        /// <summary>
        /// 是否存在有限状态机状态。
        /// </summary>
        public bool HasState(Type stateType)
        {
            if (stateType == null)
            {
                throw new FrameworkException("State type is invalid.");
            }

            return m_States.ContainsKey(stateType);
        }

        /// <summary>
        /// 获取有限状态机状态。
        /// </summary>
        public TState GetState<TState>() where TState : FsmState<T>
        {
            FsmState<T> state = null;
            if (m_States.TryGetValue(typeof(TState), out state))
            {
                return (TState)state;
            }

            return null;
        }

        /// <summary>
        /// 获取有限状态机状态。
        /// </summary>
        public FsmState<T> GetState(Type stateType)
        {
            if (stateType == null)
            {
                throw new FrameworkException("State type is invalid.");
            }

            FsmState<T> state = null;
            if (m_States.TryGetValue(stateType, out state))
            {
                return state;
            }

            return null;
        }

        /// <summary>
        /// 获取有限状态机的所有状态。
        /// </summary>
        public FsmState<T>[] GetAllStates()
        {
            int index = 0;
            FsmState<T>[] results = new FsmState<T>[m_States.Count];
            foreach (KeyValuePair<Type, FsmState<T>> state in m_States)
            {
                results[index++] = state.Value;
            }

            return results;
        }

        /// <summary>
        /// 获取有限状态机数据。
        /// </summary>
        public TData GetData<TData>(string name) where TData : Variable
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new FrameworkException("Data name is invalid.");
            }

            if (m_Datas == null)
            {
                return null;
            }

            Variable data = null;
            if (m_Datas.TryGetValue(name, out data))
            {
                return (TData)data;
            }

            return null;
        }

        /// <summary>
        /// 设置有限状态机数据。
        /// 所有权约定：调用方移交 data 的所有权；data 应由 ReferencePool.Acquire 获得。
        /// 覆盖已有同名数据时，框架会对旧值自动 ReferencePool.Release（恰好一次），调用方不得再持有旧值。
        /// </summary>
        public void SetData<TData>(string name, TData data) where TData : Variable
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new FrameworkException("Data name is invalid.");
            }

            if (m_Datas == null)
            {
                m_Datas = new Dictionary<string, Variable>(StringComparer.Ordinal);
            }

            Variable oldData = null;
            if (m_Datas.TryGetValue(name, out oldData))
            {
                // 防御：用同一实例自我覆盖时不得 Release（否则会把仍要存回的实例归还引用池，造成 use-after-release）。
                if (oldData != null && !ReferenceEquals(oldData, data))
                {
                    ReferencePool.Release(oldData);
                }
            }

            m_Datas[name] = data;
        }

        /// <summary>
        /// 是否存在有限状态机数据。
        /// </summary>
        public bool HasData(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new FrameworkException("Data name is invalid.");
            }

            if (m_Datas == null)
            {
                return false;
            }

            return m_Datas.ContainsKey(name);
        }

        /// <summary>
        /// 移除有限状态机数据。
        /// </summary>
        public bool RemoveData(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new FrameworkException("Data name is invalid.");
            }

            if (m_Datas == null)
            {
                return false;
            }

            Variable oldData = null;
            if (m_Datas.TryGetValue(name, out oldData))
            {
                if (oldData != null)
                {
                    ReferencePool.Release(oldData);
                }
            }

            return m_Datas.Remove(name);
        }

        /// <summary>
        /// 有限状态机轮询。
        /// </summary>
        internal override void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_CurrentState == null)
            {
                return;
            }

            m_CurrentStateTime += elapseSeconds;
            m_CurrentState.OnUpdate(this, elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 关闭并清理有限状态机。
        /// </summary>
        internal override void Shutdown()
        {
            ReferencePool.Release(this);
        }

        /// <summary>
        /// 切换当前有限状态机状态。
        /// </summary>
        internal void ChangeState<TState>() where TState : FsmState<T>
        {
            ChangeState(typeof(TState));
        }

        /// <summary>
        /// 切换当前有限状态机状态。
        /// </summary>
        internal void ChangeState(Type stateType)
        {
            if (m_CurrentState == null)
            {
                throw new FrameworkException("Current state is invalid.");
            }

            FsmState<T> state = GetState(stateType);
            if (state == null)
            {
                throw new FrameworkException(Utility.Text.Format("FSM '{0}' can not change state to '{1}' which is not exist.", FullName, stateType.FullName));
            }

            if (m_IsChangingState)
            {
                m_PendingStateChanges.Enqueue(state);
                return;
            }

            ChangeStateImmediately(state);
            DrainPendingStateChanges();
        }

        private void ChangeStateImmediately(FsmState<T> state)
        {
            m_IsChangingState = true;
            try
            {
                FsmState<T> previous = m_CurrentState;
                try
                {
                    previous.OnLeave(this, false);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("FSM '{0}' OnLeave threw: {1}", FullName, ex);
                }

                m_CurrentStateTime = 0f;
                m_CurrentState = state;

                try
                {
                    state.OnEnter(this);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("FSM '{0}' OnEnter threw: {1}", FullName, ex);
                }
            }
            finally
            {
                m_IsChangingState = false;
            }
        }

        private void DrainPendingStateChanges()
        {
            int transitionCount = 0;
            while (m_PendingStateChanges.Count > 0)
            {
                if (++transitionCount > MaxDeferredStateChangesPerDrain)
                {
                    m_PendingStateChanges.Clear();
                    throw new FrameworkException(Utility.Text.Format(
                        "FSM '{0}' exceeded {1} deferred state changes in one transition chain. Check for an OnEnter/OnLeave state-change loop.",
                        FullName, MaxDeferredStateChangesPerDrain));
                }

                if (m_IsDestroyed || m_CurrentState == null)
                {
                    m_PendingStateChanges.Clear();
                    return;
                }

                ChangeStateImmediately(m_PendingStateChanges.Dequeue());
            }
        }
    }
}
