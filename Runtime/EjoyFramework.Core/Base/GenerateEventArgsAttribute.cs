//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 标记某 FrameworkEventArgs / GameEventArgs partial class 由 EventArgsGenerator 自动生成
    /// Clear() 和 Create(...) 静态工厂方法。
    /// 业务侧只需定义字段，运行 Menu EjoyFramework/Core/CodeGen/Generate EventArgs Factories。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class GenerateEventArgsAttribute : Attribute { }
}
