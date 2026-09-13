//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Fsm;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 有限状态机组件。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Fsm")]
    public sealed class FsmComponent : GameFrameworkComponent
    {
        private IFsmManager m_FsmManager;

        protected override void Awake()
        {
            base.Awake();
            m_FsmManager = Framework.GetModule<IFsmManager>();
            if (m_FsmManager == null)
            {
                Log.Fatal("FSM manager is invalid.");
                return;
            }
        }

        /// <summary>
        /// 获取有限状态机数量。
        /// </summary>
        public int Count
        {
            get { return m_FsmManager.Count; }
        }

        /// <summary>
        /// 检查是否存在有限状态机。
        /// </summary>
        public bool HasFsm<T>() where T : class
        {
            return m_FsmManager.HasFsm<T>();
        }

        /// <summary>
        /// 获取有限状态机。
        /// </summary>
        public IFsm<T> GetFsm<T>() where T : class
        {
            return m_FsmManager.GetFsm<T>();
        }

        /// <summary>
        /// 获取所有有限状态机。
        /// </summary>
        public FsmBase[] GetAllFsms()
        {
            return m_FsmManager.GetAllFsms();
        }

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        public IFsm<T> CreateFsm<T>(T owner, params FsmState<T>[] states) where T : class
        {
            return m_FsmManager.CreateFsm(owner, states);
        }

        /// <summary>
        /// 创建有限状态机。
        /// </summary>
        public IFsm<T> CreateFsm<T>(string name, T owner, params FsmState<T>[] states) where T : class
        {
            return m_FsmManager.CreateFsm(name, owner, states);
        }

        /// <summary>
        /// 销毁有限状态机。
        /// </summary>
        public bool DestroyFsm<T>() where T : class
        {
            return m_FsmManager.DestroyFsm<T>();
        }
    }
}
