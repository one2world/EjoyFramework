//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Performance
{
    /// <summary>
    /// Unity 对象探针：把 <see cref="ObjectTracker"/>（引擎无关）对 UnityEngine.Object 的特殊语义判定下放到 Unity 层。
    ///
    /// 为什么需要它：UnityEngine.Object 重载了 <c>==</c> —— 原生对象被 <c>Destroy</c> 后，C# 托管引用仍非 null
    /// （未被 GC），但 <c>(uo == null)</c> 返回 true。即「引用判空」（托管是否存活）与「对象判空」（原生是否存活）
    /// 含义不同：
    ///   • 托管存活 + 原生存活 → 正常；
    ///   • 托管存活 + 原生已毁 → <b>悬垂引用泄漏</b>（已 Destroy 却仍被持有，阻止 GC，常见 bug）；
    ///   • 托管已被 GC          → 健康。
    /// 核心层不引用 UnityEngine，故由 Unity 层实现本接口并经
    /// <see cref="ObjectTracker.SetUnityObjectInspector"/> 注入。
    /// </summary>
    public interface IUnityObjectInspector
    {
        /// <summary>该类型是否为 <c>UnityEngine.Object</c> 或其子类。</summary>
        bool IsUnityObjectType(Type type);

        /// <summary>
        /// 给定一个 <c>UnityEngine.Object</c> 引用，其「原生对象」是否仍存活（未被 Destroy）。
        /// 实现应使用 Unity 重载的 <c>==</c> 语义：<c>obj is UnityEngine.Object uo &amp;&amp; uo != null</c>。
        /// 调用方仅对 Unity 对象调用本方法。
        /// </summary>
        bool IsNativeAlive(object obj);
    }
}
