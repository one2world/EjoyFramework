//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Fsm
{
    /// <summary>
    /// 有限状态机管理器接口。
    /// </summary>
    public interface IFsmManager
    {
        /// <summary>
        /// 获取有限状态机数量。
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        bool HasFsm<T>() where T : class;

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        bool HasFsm<T>(string name) where T : class;

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        IFsm<T> GetFsm<T>() where T : class;

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        IFsm<T> GetFsm<T>(string name) where T : class;

        /// <summary>
        /// 获取所有有限状态机。
        /// </summary>
        FsmBase[] GetAllFsms();

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        IFsm<T> CreateFsm<T>(T owner, params FsmState<T>[] states) where T : class;

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        IFsm<T> CreateFsm<T>(string name, T owner, params FsmState<T>[] states) where T : class;

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        IFsm<T> CreateFsm<T>(T owner, List<FsmState<T>> states) where T : class;

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        IFsm<T> CreateFsm<T>(string name, T owner, List<FsmState<T>> states) where T : class;

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        bool DestroyFsm<T>() where T : class;

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        bool DestroyFsm<T>(string name) where T : class;

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        bool DestroyFsm<T>(IFsm<T> fsm) where T : class;

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        bool DestroyFsm(FsmBase fsm);
    }
}
