//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 游戏框架组件抽象类。
    /// </summary>
    public abstract class GameFrameworkComponent : MonoBehaviour
    {
        /// <summary>
        /// 游戏框架组件初始化。
        /// </summary>
        protected virtual void Awake()
        {
            if (!ComponentRegistry.RegisterComponent(this))
            {
                // 重复注册：避免后续依赖此 Component 的代码访问到错误实例
                Log.Error("Failed to register {0} (duplicate or invalid).", GetType().FullName);
            }
        }

        /// <summary>
        /// 游戏框架组件销毁：从注册表注销自身。
        /// DontDestroyOnLoad 关闭时场景卸载会销毁组件，必须注销，否则 s_ByType 残留已销毁实例，
        /// 后续重注册失败、GameEntry.X 永久返回失效旧实例。
        /// 现声明为 protected virtual 模板方法：默认从 ComponentRegistry 注销本组件。
        /// 需要自带销毁逻辑的子类必须 override 本方法，并调用 base.OnDestroy()，
        /// 以确保注销仍会发生（否则该子类的 OnDestroy 会遮蔽本方法，导致永不注销）。
        /// </summary>
        protected virtual void OnDestroy()
        {
            ComponentRegistry.UnregisterComponent(this);
        }
    }
}
