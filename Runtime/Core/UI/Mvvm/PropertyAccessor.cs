//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Concurrent;
#if UNITY_EDITOR
using System.Reflection;
#endif

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 属性访问器：把 (类型, 成员) 映射为强类型 get/set delegate，供 MVVM 绑定按属性名取/写值。
    ///
    /// 注册表优先（零反射）：生成代码 <c>MvvmAccessorRegistrations</c>（由 MvvmAccessorGenerator 产出）在启动时
    /// 调用 <see cref="Register"/> 登记每个可绑定成员的 get/set delegate。玩家构建<b>完全不依赖反射</b>——
    /// IL2CPP 不会因裁剪属性访问器而使绑定失效，性能也是直接 delegate 调用。
    ///
    /// Editor 兜底：仅在 <c>UNITY_EDITOR</c> 下，未登记的 (类型, 成员) 退回一次性反射查找并缓存，便于编辑期迭代
    /// 与单元测试（无需先跑 codegen）。玩家构建无此兜底——未登记即返回 null，并按 (类型, 成员) 告警一次，
    /// 提示重跑 codegen；绑定本身不生效，但不会没有任何线索。
    /// </summary>
    public static class PropertyAccessor
    {
        private sealed class MemberKey : IEquatable<MemberKey>
        {
            public readonly Type Type;
            public readonly string Member;
            public MemberKey(Type t, string m) { Type = t; Member = m; }
            public bool Equals(MemberKey other) { return other != null && Type == other.Type && Member == other.Member; }
            public override bool Equals(object obj) { return obj is MemberKey k && Equals(k); }
            public override int GetHashCode() { return (Type != null ? Type.GetHashCode() : 0) * 397 ^ (Member != null ? Member.GetHashCode() : 0); }
        }

        private static readonly ConcurrentDictionary<MemberKey, Func<object, object>> s_Getters
            = new ConcurrentDictionary<MemberKey, Func<object, object>>();
        private static readonly ConcurrentDictionary<MemberKey, Action<object, object>> s_Setters
            = new ConcurrentDictionary<MemberKey, Action<object, object>>();

#if !UNITY_EDITOR
        /// <summary>
        /// 玩家构建下已告警过的 (类型, 成员)，用于把"未登记"警告压成每个成员一条。
        /// 未登记属于 codegen 配置错误而非运行时常态，但 getter 会在每次通知刷新时取用，不去重会刷屏。
        /// </summary>
        private static readonly ConcurrentDictionary<MemberKey, bool> s_MissingWarned
            = new ConcurrentDictionary<MemberKey, bool>();

        /// <summary>
        /// 玩家构建下未登记成员的告警。没有它，绑定会静默失效——UI 不更新且日志里没有任何线索，
        /// 是最难排查的一类问题。
        /// </summary>
        private static void WarnMissing(MemberKey key, string accessorKind)
        {
            if (!s_MissingWarned.TryAdd(key, true)) return;
            FrameworkLog.Warning(
                "[PropertyAccessor] No registered {0} for '{1}.{2}'. The binding will silently do nothing in player builds. Re-run the MVVM accessor codegen (MvvmAccessorGenerator) after adding bindable members.",
                accessorKind, key.Type != null ? key.Type.FullName : "<null>", key.Member);
        }
#endif

        /// <summary>
        /// 登记某 (类型, 成员) 的强类型 get/set delegate（由生成代码调用，幂等覆盖）。
        /// 只读属性传 <paramref name="setter"/>=null；只写属性传 <paramref name="getter"/>=null。
        /// </summary>
        public static void Register(Type ownerType, string memberName,
            Func<object, object> getter, Action<object, object> setter)
        {
            if (ownerType == null || string.IsNullOrEmpty(memberName)) return;
            MemberKey key = new MemberKey(ownerType, memberName);
            if (getter != null) s_Getters[key] = getter;
            if (setter != null) s_Setters[key] = setter;
        }

        /// <summary>
        /// 取属性/字段的 getter delegate。已登记则直接返回；未登记时玩家构建返回 null，
        /// Editor 退回一次性反射并缓存。
        /// </summary>
        public static Func<object, object> GetGetter(Type ownerType, string memberName)
        {
            if (ownerType == null || string.IsNullOrEmpty(memberName)) return null;
            MemberKey key = new MemberKey(ownerType, memberName);
            if (s_Getters.TryGetValue(key, out Func<object, object> getter)) return getter;
#if UNITY_EDITOR
            getter = BuildGetterByReflection(key);
            s_Getters[key] = getter; // 缓存（含 null），避免重复反射查找
            return getter;
#else
            WarnMissing(key, "getter");
            return null;
#endif
        }

        /// <summary>
        /// 取属性/字段的 setter delegate。已登记则直接返回；未登记时玩家构建返回 null，
        /// Editor 退回一次性反射并缓存。
        /// </summary>
        public static Action<object, object> GetSetter(Type ownerType, string memberName)
        {
            if (ownerType == null || string.IsNullOrEmpty(memberName)) return null;
            MemberKey key = new MemberKey(ownerType, memberName);
            if (s_Setters.TryGetValue(key, out Action<object, object> setter)) return setter;
#if UNITY_EDITOR
            setter = BuildSetterByReflection(key);
            s_Setters[key] = setter;
            return setter;
#else
            WarnMissing(key, "setter");
            return null;
#endif
        }

#if UNITY_EDITOR
        // Editor-only 反射兜底：玩家构建中这些方法不存在（连同 System.Reflection 引用），故设备包零反射。
        private static Func<object, object> BuildGetterByReflection(MemberKey key)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo p = key.Type.GetProperty(key.Member, flags);
            if (p != null)
            {
                if (!p.CanRead) return null;
                return o => p.GetValue(o);
            }
            FieldInfo f = key.Type.GetField(key.Member, flags);
            if (f != null) return o => f.GetValue(o);
            return null;
        }

        private static Action<object, object> BuildSetterByReflection(MemberKey key)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo p = key.Type.GetProperty(key.Member, flags);
            if (p != null)
            {
                if (!p.CanWrite) return null;
                return (o, v) => p.SetValue(o, v);
            }
            FieldInfo f = key.Type.GetField(key.Member, flags);
            if (f != null)
            {
                if (f.IsInitOnly || f.IsLiteral) return null;
                return (o, v) => f.SetValue(o, v);
            }
            return null;
        }
#endif
    }
}
