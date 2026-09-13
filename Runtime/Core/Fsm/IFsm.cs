//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Fsm
{
    /// <summary>
    /// 有限状态机接口。
    /// </summary>
    /// <typeparam name="T">有限状态机持有者类型。</typeparam>
    public interface IFsm<T> where T : class
    {
        /// <summary>
        /// 获取有限状态机名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 获取有限状态机完整名称。
        /// </summary>
        string FullName { get; }

        /// <summary>
        /// 获取有限状态机持有者。
        /// </summary>
        T Owner { get; }

        /// <summary>
        /// 获取有限状态机中状态的数量。
        /// </summary>
        int FsmStateCount { get; }

        /// <summary>
        /// 获取有限状态机是否正在运行。
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 获取有限状态机是否被销毁。
        /// </summary>
        bool IsDestroyed { get; }

        /// <summary>
        /// 获取当前有限状态机状态。
        /// </summary>
        FsmState<T> CurrentState { get; }

        /// <summary>
        /// 获取当前有限状态机状态持续时间。
        /// </summary>
        float CurrentStateTime { get; }

        /// <summary>
        /// 开始有限状态机。
        /// </summary>
        /// <typeparam name="TState">要开始的有限状态机状态类型。</typeparam>
        void Start<TState>() where TState : FsmState<T>;

        /// <summary>
        /// 开始有限状态机。
        /// </summary>
        /// <param name="stateType">要开始的有限状态机状态类型。</param>
        void Start(Type stateType);

        /// <summary>
        /// 是否存在有限状态机状态。
        /// </summary>
        bool HasState<TState>() where TState : FsmState<T>;

        /// <summary>
        /// 是否存在有限状态机状态。
        /// </summary>
        bool HasState(Type stateType);

        /// <summary>
        /// 获取有限状态机状态。
        /// </summary>
        TState GetState<TState>() where TState : FsmState<T>;

        /// <summary>
        /// 获取有限状态机状态。
        /// </summary>
        FsmState<T> GetState(Type stateType);

        /// <summary>
        /// 获取有限状态机的所有状态。
        /// </summary>
        FsmState<T>[] GetAllStates();

        /// <summary>
        /// 获取有限状态机数据。
        /// </summary>
        TData GetData<TData>(string name) where TData : Variable;

        /// <summary>
        /// 设置有限状态机数据。
        /// </summary>
        void SetData<TData>(string name, TData data) where TData : Variable;

        /// <summary>
        /// 是否存在有限状态机数据。
        /// </summary>
        bool HasData(string name);

        /// <summary>
        /// 移除有限状态机数据。
        /// </summary>
        bool RemoveData(string name);
    }
}
