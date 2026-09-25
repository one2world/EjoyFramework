//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 标记某 partial class 由 DeepCopyGenerator 自动生成 <c>DeepCopy()</c> / <c>CopyTo(target)</c>
    /// （实现 <see cref="EjoyFramework.Core.Serialization.IDeepCopyable{T}"/>）。
    ///
    /// 生成代码逐字段直接赋值；嵌套 [GenerateDeepCopy] 类型递归深拷贝，
    /// <c>List&lt;T&gt;</c>/数组逐元素复制。用于存档快照、状态回滚、组件克隆等
    /// ——替代反射拷贝（如 JsonUtility 序列化再反序列化的"穷人深拷贝"）。
    ///
    /// 成员处理规则同 <see cref="GenerateSerializerAttribute"/>：受 <see cref="SerializeIgnoreAttribute"/>
    /// 与 <see cref="SerializeOrderAttribute"/> 影响（Order 仅影响生成顺序，不影响语义）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class GenerateDeepCopyAttribute : Attribute { }
}
