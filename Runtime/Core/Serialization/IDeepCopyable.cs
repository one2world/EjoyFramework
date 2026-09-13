//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Serialization
{
    /// <summary>
    /// 深拷贝契约。由 DeepCopyGenerator 为标记了 <see cref="GenerateDeepCopyAttribute"/>
    /// 的类型自动生成实现（partial 方法）。
    ///
    /// 生成代码逐字段赋值；引用型字段若为嵌套可深拷贝类型则递归 <see cref="DeepCopy"/>，
    /// 集合逐元素复制——避免共享引用导致的隐藏副作用。
    /// </summary>
    /// <typeparam name="T">实现类型自身。</typeparam>
    public interface IDeepCopyable<T>
    {
        /// <summary>创建并返回一个深拷贝副本。</summary>
        T DeepCopy();

        /// <summary>把自身的字段深拷贝到已存在的 <paramref name="target"/>（复用对象，零分配场景）。</summary>
        void CopyTo(T target);
    }
}
