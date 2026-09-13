//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

// unsafe 路径除了要求显式开启 EJOY_UNSAFE_STRING，还必须运行在非移动 GC 的 Unity 运行时上。
// ENABLE_MONO / ENABLE_IL2CPP 由 Unity 自动定义，两者对应的 Boehm GC 都不移动对象；
// 在纯 .NET（CoreCLR，压缩式 GC）宿主下该条件不成立，自动退化为安全实现。原因见类型注释中的"GC 假设"。
#if EJOY_UNSAFE_STRING && (ENABLE_MONO || ENABLE_IL2CPP)
#define EJOY_UNSAFE_STRING_ACTIVE
#endif

using System;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 可复用的可变字符串，用于对接那些只接受 <see cref="string"/> 参数、无法改造成 Span 的第三方 API，
    /// 在稳态下完全不产生字符串分配。
    /// 典型用法：
    /// <code>
    /// using (var ms = MutableString.Rent(64))
    /// {
    ///     using (var t = TempText.Rent(64))
    ///     {
    ///         t.Append("HP ").Append(hp);
    ///         ms.Set(t.AsSpan());
    ///     }
    ///
    ///     ThirdPartyApi.Log(ms.Value);    // 只在本作用域内有效
    /// }
    /// </code>
    ///
    /// 实现原理（仅在 unsafe 路径被激活时启用，激活条件见下方"启用方式"）：
    ///   - 预先分配若干 <c>new string('\0', capacity)</c> 实例并按 64/256/1024 三档 [ThreadStatic] 缓存。
    ///   - 通过 <c>fixed (char* p = str)</c> 拿到首字符地址后直接写入内容；随后把 <c>((int*)p) - 1</c>
    ///     指向的私有 length 字段改写为实际长度。之所以用"首字符地址回退 4 字节"而不是"对象起始地址 + 固定偏移"，
    ///     是因为前者不依赖对象头大小，也不受 32/64 位差异影响。
    ///   - length 字段被改写后，<c>Length</c>、索引器、<c>Equals</c>、<c>GetHashCode</c>、<c>Substring</c>
    ///     等全部按新长度工作。Mono 与 IL2CPP 的 String 对象都没有哈希缓存字段（每次调用重新计算），
    ///     因此改写长度后哈希值与当前内容始终一致，可以安全地作为字典查询键使用；
    ///     该结论同样由启动自检实测校验，见 <see cref="Verify"/>。
    ///
    /// GC 假设（关键，决定了本类型只允许在 Unity 运行时启用）：
    ///   - 改写 length 之后，字符串对象报告的大小随之变小。在**压缩式/移动式 GC**（如 .NET CoreCLR）下，
    ///     GC 会按这个缩短后的长度计算对象尺寸来遍历堆，导致后续对象的起始地址错位，
    ///     只要在租借窗口内发生一次 GC 就会直接崩溃（Fatal CLR error），这一点已实测复现。
    ///   - Unity 使用的 Boehm GC（Mono 与 IL2CPP 后端）不移动对象，也不依赖 length 推导堆布局，因此不受影响。
    ///   - 因此 unsafe 路径的编译条件叠加了 ENABLE_MONO / ENABLE_IL2CPP：**仅获准在 Unity Boehm 下启用**，
    ///     任何纯 .NET 宿主（含在 Unity 之外直接跑本程序集的单元测试）都会自动退化为安全实现。
    ///   - 注意：<see cref="Verify"/> 只能校验内存布局假设，**无法**校验 GC 是否移动对象；
    ///     后者只能靠上述编译期闸门保证，自检通过并不代表当前 GC 是安全的。
    ///
    /// 启用方式（默认关闭，属于显式 opt-in）：
    ///   - Editor 与真机统一在 Player Settings > Other Settings > Scripting Define Symbols 中
    ///     为目标平台加入 <c>EJOY_UNSAFE_STRING</c>。
    ///   - 该符号必须对本程序集与其调用方/测试程序集**同时**生效，否则会出现"运行时走退化路径、
    ///     调用方却按 unsafe 语义编译"的不一致，所以不要用单个程序集的 csc.rsp 来定义它。
    ///   - 测试无需条件编译即可覆盖两种形态：用例通过运行期的 <see cref="IsUnsafeEnabled"/> 分流，
    ///     符号关闭时相关用例标记为 Ignore 而不是被编译掉。
    ///
    /// 借/还的 length 语义：
    ///   - 池中缓存的实例其 length 恒为"满容量"，这是它的静息状态。
    ///   - <see cref="Rent"/> 时把 length 置 0，之后每次 <see cref="Set(ReadOnlySpan{char})"/> 覆写为实际长度；
    ///     写入永远以池记录的 Capacity 为准，不依赖当前的 length 值，因此不需要"先恢复满容量再写"。
    ///   - <see cref="Dispose"/> 归还时必须把 length 复原为满容量，保证下一位租借者以及任何残留引用看到的
    ///     都是一个自洽的对象（内容是旧数据，但不会越界读）。内容本身无需清零。
    ///
    /// 安全网：
    ///   - 类型加载时执行一次布局自检（见 <see cref="Verify"/>）。自检失败则整个类型退化为普通分配路径，
    ///     并且只警告一次，功能不受影响，只是不再零分配。
    ///   - 未激活 unsafe 路径时同样走退化路径，API 面与行为（除分配量外）完全一致。
    ///
    /// 使用契约（违反会导致难以排查的数据错乱）：
    ///   - <see cref="Value"/> 返回的 string 只在 using 作用域内有效，禁止存入字段、静态表、闭包等长生命周期容器。
    ///   - 禁止把它作为需要留存的字典/集合键"插入"（插入时记录的哈希会在内容变化后失效）；仅"查询"是安全的。
    ///   - 不得跨线程传递：缓存是 [ThreadStatic] 的，租借与归还必须在同一线程。
    ///   - 需要留存内容时请使用 <see cref="ToString"/>，它返回一份独立的普通字符串。
    ///   - 编辑器与开发版下通过版本号检测 Dispose 之后继续使用。
    /// </summary>
    public struct MutableString : IDisposable
    {
        /// <summary>
        /// MutableString 的可变状态。与 <see cref="TempText"/> 同理，放在托管对象中以规避 using 只读变量导致的防御性拷贝。
        /// </summary>
        private sealed class State
        {
            /// <summary>
            /// 当前承载内容的字符串实例。
            /// </summary>
            public string Str;

            /// <summary>
            /// Str 的物理容量。池化实例为所属档位容量；退化实例恒等于当前内容长度。
            /// </summary>
            public int Capacity;

            /// <summary>
            /// 当前内容长度。
            /// </summary>
            public int Length;

            /// <summary>
            /// Str 是否为池化的可写实例。false 表示退化为普通分配。
            /// </summary>
            public bool Pooled;

            public int Version;

            public bool Disposed;
        }

        /// <summary>
        /// 可池化的容量档位，必须升序排列。
        /// </summary>
        private static readonly int[] s_TierSizes = new int[] { 64, 256, 1024 };

        /// <summary>
        /// 每档最多缓存的字符串实例数量。字符串实例是常驻内存的，因此比 char 缓冲池更保守。
        /// </summary>
        private const int MaxPerTier = 4;

        /// <summary>
        /// 每线程缓存的状态对象数量上限。
        /// </summary>
        private const int MaxCachedStates = 8;

        [ThreadStatic]
        private static string[] s_Strings;

        [ThreadStatic]
        private static int[] s_Counts;

        [ThreadStatic]
        private static State[] s_StateCache;

        [ThreadStatic]
        private static int s_StateCacheCount;

        /// <summary>
        /// 布局自检结果。为 false 时所有租借都走普通分配路径。
        /// </summary>
        private static readonly bool s_LayoutSupported = Verify();

        /// <summary>
        /// 自检失败的警告是否已经输出过，保证只警告一次。
        /// </summary>
        private static bool s_WarnedOnce;

        private readonly State m_State;
        private readonly int m_Version;

        private MutableString(State state)
        {
            m_State = state;
            m_Version = state.Version;
        }

        /// <summary>
        /// 当前进程是否真正启用了零分配的可变字符串。为 false 时本类型仍然可用，只是每次 Set 都会分配。
        /// </summary>
        public static bool IsUnsafeEnabled
        {
            get { return s_LayoutSupported; }
        }

        /// <summary>
        /// 可池化的最大容量，超过此值的请求一律走普通分配。
        /// </summary>
        public static int MaxPooledCapacity
        {
            get { return s_TierSizes[s_TierSizes.Length - 1]; }
        }

        /// <summary>
        /// 当前内容，只在 using 作用域内有效，切勿留存。内容为空时返回长度为 0 的字符串。
        /// </summary>
        public string Value
        {
            get
            {
                CheckUsable();
                return m_State.Str;
            }
        }

        /// <summary>
        /// 当前内容长度。
        /// </summary>
        public int Length
        {
            get
            {
                CheckUsable();
                return m_State.Length;
            }
        }

        /// <summary>
        /// 当前底层实例的容量。退化实例没有预留容量，其值等于内容长度。
        /// </summary>
        public int Capacity
        {
            get
            {
                CheckUsable();
                return m_State.Capacity;
            }
        }

        /// <summary>
        /// 当前实例是否由池化的可写字符串承载。false 表示本次租借退化为普通分配。
        /// </summary>
        public bool IsPooled
        {
            get
            {
                CheckUsable();
                return m_State.Pooled;
            }
        }

        /// <summary>
        /// 租借一个可变字符串，必须配合 using 或显式 <see cref="Dispose"/> 使用。
        /// </summary>
        /// <param name="capacity">预期的最大内容长度，小于 1 时按 1 处理；超过 <see cref="MaxPooledCapacity"/> 时退化为普通分配。</param>
        /// <returns>可用的实例，初始内容为空。</returns>
        public static MutableString Rent(int capacity = 64)
        {
            if (capacity < 1)
            {
                capacity = 1;
            }

            State state = RentState();
            state.Disposed = false;
            AcquireStorage(state, capacity);
            return new MutableString(state);
        }

        /// <summary>
        /// 设置内容为指定的字符序列。
        /// </summary>
        /// <param name="value">待写入的字符序列。</param>
        public void Set(ReadOnlySpan<char> value)
        {
            CheckUsable();
            State state = m_State;
            if (!state.Pooled || value.Length > state.Capacity)
            {
                // 退化实例或容量不足：尝试换一个足够大的池化实例，仍不满足则直接分配。
                ReleaseStorage(state);
                AcquireStorage(state, value.Length < 1 ? 1 : value.Length);
            }

            if (state.Pooled)
            {
                WriteContent(state, value);
                return;
            }

            state.Str = value.Length == 0 ? string.Empty : new string(value);
            state.Length = value.Length;
            state.Capacity = value.Length;
        }

        /// <summary>
        /// 设置内容为指定字符串，null 视为空串。
        /// </summary>
        /// <param name="value">待写入的字符串。</param>
        public void Set(string value)
        {
            Set(value == null ? ReadOnlySpan<char>.Empty : value.AsSpan());
        }

        /// <summary>
        /// 设置内容为指定 <see cref="TempText"/> 已写入的部分。
        /// </summary>
        /// <param name="text">内容来源。</param>
        public void Set(TempText text)
        {
            Set(text.AsSpan());
        }

        /// <summary>
        /// 清空内容，保留当前底层实例。
        /// </summary>
        public void Clear()
        {
            Set(ReadOnlySpan<char>.Empty);
        }

        /// <summary>
        /// 生成一份独立的普通字符串，用于需要长期留存内容的场景。该调用会产生托管分配。
        /// </summary>
        /// <returns>与当前内容相同的独立字符串。</returns>
        public override string ToString()
        {
            CheckUsable();
            State state = m_State;
            if (state.Length == 0)
            {
                return string.Empty;
            }

            return new string(state.Str.AsSpan(0, state.Length));
        }

        /// <summary>
        /// 归还底层实例与状态对象，重复调用安全（幂等）。
        /// </summary>
        public void Dispose()
        {
            State state = m_State;
            if (state == null || state.Disposed || state.Version != m_Version)
            {
                return;
            }

            ReleaseStorage(state);
            state.Str = null;
            state.Length = 0;
            state.Capacity = 0;
            state.Disposed = true;
            unchecked
            {
                state.Version++;
            }

            ReturnState(state);
        }

        /// <summary>
        /// 清空当前线程缓存的全部字符串实例与状态对象，一般仅在测试或内存告警时调用。
        /// </summary>
        public static void ClearCache()
        {
            if (s_Strings != null)
            {
                for (int i = 0; i < s_Strings.Length; i++)
                {
                    s_Strings[i] = null;
                }
            }

            if (s_Counts != null)
            {
                for (int i = 0; i < s_Counts.Length; i++)
                {
                    s_Counts[i] = 0;
                }
            }

            if (s_StateCache != null)
            {
                for (int i = 0; i < s_StateCache.Length; i++)
                {
                    s_StateCache[i] = null;
                }
            }

            s_StateCacheCount = 0;
        }

        /// <summary>
        /// 执行字符串内存布局自检：构造一个专用实例，写入内容并改写长度，逐项校验 Length、内容、索引、
        /// Equals 与 GetHashCode 是否与等价的普通字符串一致，最后校验长度可以复原。
        /// 该方法在类型加载时自动执行一次，也可以显式调用以便在启动流程中确认能力。
        /// 任何一项不符或抛出异常都会返回 false，此后本类型全部退化为普通分配。
        ///
        /// 覆盖范围（务必看清）：本自检**只覆盖内存布局假设**（length 字段位置与改写后各 API 的一致性），
        /// **不覆盖 GC 移动/压缩假设**。后者无法在运行期可靠探测，只能由编译期闸门
        /// （EJOY_UNSAFE_STRING 叠加 ENABLE_MONO / ENABLE_IL2CPP）保证；
        /// 换言之，自检返回 true 并不意味着当前 GC 允许这样改写长度。详见类型注释中的"GC 假设"。
        /// </summary>
        /// <returns>布局假设是否成立；unsafe 路径未激活时恒为 false。</returns>
        public static bool Verify()
        {
#if EJOY_UNSAFE_STRING_ACTIVE
            try
            {
                // 必须用 new string 构造，绝不能是字面量：字面量是驻留的，改写它会污染整个进程。
                string probe = new string('\0', 8);
                ReadOnlySpan<char> expected = "abc".AsSpan();
                UnsafeWrite(probe, expected, 8);
                if (probe.Length != expected.Length)
                {
                    return false;
                }

                if (probe[0] != 'a' || probe[1] != 'b' || probe[2] != 'c')
                {
                    return false;
                }

                if (!probe.Equals("abc", StringComparison.Ordinal))
                {
                    return false;
                }

                if (probe.GetHashCode() != "abc".GetHashCode())
                {
                    return false;
                }

                // 复原为满容量，验证 length 字段可以双向改写。
                UnsafeSetLength(probe, 8);
                return probe.Length == 8;
            }
            catch (Exception)
            {
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 为状态对象取得一个至少能容纳 capacity 个字符的承载实例。取不到池化实例时标记为退化。
        /// </summary>
        private static void AcquireStorage(State state, int capacity)
        {
            string pooled = s_LayoutSupported ? RentString(capacity) : null;
            if (pooled != null)
            {
                state.Str = pooled;
                state.Capacity = pooled.Length;
                state.Pooled = true;
                WriteContent(state, ReadOnlySpan<char>.Empty);
                return;
            }

            WarnDegradedOnce();
            state.Str = string.Empty;
            state.Capacity = 0;
            state.Length = 0;
            state.Pooled = false;
        }

        /// <summary>
        /// 把状态对象当前持有的池化实例复原为满容量并归还，退化实例直接丢弃交给 GC。
        /// </summary>
        private static void ReleaseStorage(State state)
        {
            if (!state.Pooled)
            {
                state.Str = null;
                return;
            }

            ReturnString(state.Str, state.Capacity);
            state.Str = null;
            state.Pooled = false;
        }

        private static void WriteContent(State state, ReadOnlySpan<char> value)
        {
#if EJOY_UNSAFE_STRING_ACTIVE
            UnsafeWrite(state.Str, value, state.Capacity);
            state.Length = value.Length;
#else
            throw new FrameworkException("Unsafe string writing is not compiled in.");
#endif
        }

        /// <summary>
        /// 在"整个类型不具备零分配能力"时输出一次性警告，用于让性能回退在日志里可见而不是静默发生。
        /// 语义边界：只针对全局性的能力缺失（unsafe 路径未激活，或布局自检失败）。
        /// 单次请求超过 <see cref="MaxPooledCapacity"/> 而临时走分配路径属于正常设计行为，不在此告警，
        /// 否则热路径上的偶发大字符串会把日志刷爆。因此当 <see cref="s_LayoutSupported"/> 为 true 时本方法直接返回。
        /// </summary>
        private static void WarnDegradedOnce()
        {
            if (s_WarnedOnce || s_LayoutSupported)
            {
                return;
            }

            s_WarnedOnce = true;
            FrameworkLog.Warning(
                "MutableString falls back to normal string allocation. Either EJOY_UNSAFE_STRING is not defined, "
                + "or the runtime is not Unity Mono/IL2CPP (a moving GC makes the length rewrite unsafe), "
                + "or the memory layout self-check failed. Note the self-check only covers layout, not GC movement.");
        }

        private static string RentString(int capacity)
        {
            int tier = GetTier(capacity);
            if (tier < 0)
            {
                return null;
            }

            EnsureStorage();
            int count = s_Counts[tier];
            if (count > 0)
            {
                int slot = tier * MaxPerTier + count - 1;
                string cached = s_Strings[slot];
                s_Strings[slot] = null;
                s_Counts[tier] = count - 1;
                if (cached != null)
                {
                    return cached;
                }
            }

            return new string('\0', s_TierSizes[tier]);
        }

        /// <summary>
        /// 归还池化实例。归还前把 length 复原为满容量，使其回到静息状态。
        /// </summary>
        private static void ReturnString(string value, int capacity)
        {
            if (value == null)
            {
                return;
            }

#if EJOY_UNSAFE_STRING_ACTIVE
            UnsafeSetLength(value, capacity);
#endif

            int tier = GetExactTier(capacity);
            if (tier < 0)
            {
                return;
            }

            EnsureStorage();
            int count = s_Counts[tier];
            if (count >= MaxPerTier)
            {
                return;
            }

            s_Strings[tier * MaxPerTier + count] = value;
            s_Counts[tier] = count + 1;
        }

        private static void EnsureStorage()
        {
            if (s_Strings == null)
            {
                s_Strings = new string[s_TierSizes.Length * MaxPerTier];
                s_Counts = new int[s_TierSizes.Length];
            }
        }

        private static int GetTier(int minCapacity)
        {
            for (int i = 0; i < s_TierSizes.Length; i++)
            {
                if (minCapacity <= s_TierSizes[i])
                {
                    return i;
                }
            }

            return -1;
        }

        private static int GetExactTier(int length)
        {
            for (int i = 0; i < s_TierSizes.Length; i++)
            {
                if (length == s_TierSizes[i])
                {
                    return i;
                }
            }

            return -1;
        }

        private static State RentState()
        {
            if (s_StateCache == null)
            {
                s_StateCache = new State[MaxCachedStates];
            }

            if (s_StateCacheCount > 0)
            {
                s_StateCacheCount--;
                State cached = s_StateCache[s_StateCacheCount];
                s_StateCache[s_StateCacheCount] = null;
                if (cached != null)
                {
                    return cached;
                }
            }

            return new State();
        }

        private static void ReturnState(State state)
        {
            if (s_StateCache == null)
            {
                s_StateCache = new State[MaxCachedStates];
            }

            if (s_StateCacheCount >= MaxCachedStates)
            {
                return;
            }

            s_StateCache[s_StateCacheCount] = state;
            s_StateCacheCount++;
        }

        private void CheckUsable()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_State == null)
            {
                throw new FrameworkException("MutableString is not initialized, use MutableString.Rent to create one.");
            }

            if (m_State.Disposed || m_State.Version != m_Version)
            {
                throw new FrameworkException("MutableString has already been disposed.");
            }
#endif
        }

#if EJOY_UNSAFE_STRING_ACTIVE
        /// <summary>
        /// 把内容写入字符串实例并改写其 length 字段。
        /// </summary>
        /// <param name="target">目标实例，必须是 new string 构造出来的非驻留实例。</param>
        /// <param name="value">待写入内容，长度不得超过 capacity。</param>
        /// <param name="capacity">目标实例的物理容量。</param>
        private static unsafe void UnsafeWrite(string target, ReadOnlySpan<char> value, int capacity)
        {
            if (value.Length > capacity)
            {
                throw new FrameworkException("Value exceeds the capacity of the mutable string.");
            }

            fixed (char* head = target)
            {
                if (value.Length > 0)
                {
                    value.CopyTo(new Span<char>(head, capacity));
                }

                if (value.Length < capacity)
                {
                    // 保持 length 位置上的结尾 '\0'，与运行时对字符串"以 null 结尾"的约定一致，
                    // 便于安全地传给需要 C 风格字符串的原生互操作层。
                    head[value.Length] = '\0';
                }

                // 首字符地址回退一个 int 即为 length 字段，该布局在 Mono / IL2CPP / CoreCLR 上一致。
                *((int*)head - 1) = value.Length;
            }
        }

        /// <summary>
        /// 仅改写字符串实例的 length 字段，不触碰内容。
        /// </summary>
        /// <param name="target">目标实例。</param>
        /// <param name="length">新的长度，调用方负责保证不超过物理容量。</param>
        private static unsafe void UnsafeSetLength(string target, int length)
        {
            fixed (char* head = target)
            {
                *((int*)head - 1) = length;
            }
        }
#endif
    }
}
