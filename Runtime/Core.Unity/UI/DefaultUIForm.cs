//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 默认 UIForm 具体实现。
    /// <see cref="UIFormBehaviour"/> 是抽象类，<c>AddComponent&lt;UIFormBehaviour&gt;()</c> 会在运行时抛 ArgumentException；
    /// 当 prefab 上没有挂载任何 IUIForm 组件时，<see cref="DefaultUIFormHelper"/> 兜底加挂本类型。
    /// 仅以最小方式实现基类的抽象成员（无额外逻辑）。
    /// </summary>
    public sealed class DefaultUIForm : UIFormBehaviour
    {
        protected override void OnFirstInit(object userData) { }
        protected override void OnReuseInit(object userData) { }
    }
}
