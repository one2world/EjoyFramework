//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Globalization;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 零 GC 临时字符串构建器，用于每帧拼接 UI 文本、日志、调试信息等短生命周期字符串。
    /// 典型用法：
    /// <code>
    /// using (var t = TempText.Rent(64))
    /// {
    ///     t.Append("HP ").Append(hp).Append('/').Append(maxHp);
    ///     label.SetText(t.AsSpan());
    /// }
    /// </code>
    /// 设计理由：
    ///   - 所有数值追加一律走 TryFormat 直写 Span，杜绝 int/float.ToString() 产生的临时 string 分配。
    ///   - 声明为 ref struct，保证实例只能存在于栈上，无法被闭包/字段捕获，从根本上避免"缓冲已归还但引用仍存活"。
    ///   - 关键设计：可变状态放在池化的 TempTextState 对象里，而不是 ref struct 的字段中。
    ///     原因是 C# 的 using 语句会把变量声明为只读，对只读结构体变量调用非只读实例方法会产生
    ///     防御性拷贝，若 Length 存在结构体字段中，Append 的写入将丢失。把状态放在引用对象里后，
    ///     无论拷贝多少份 TempText，操作的都是同一份状态；结构体本身进一步声明为 readonly，
    ///     彻底消除防御性拷贝开销，同时 Append 可以按值返回 TempText 实现链式调用
    ///     （结构体方法无法 return ref this）。
    ///   - 容量不足时从 <see cref="CharBufferPool"/> 换更大缓冲并拷贝，旧缓冲立即归还，稳态下不再分配。
    ///   - 使用后（Dispose 之后）继续使用会抛 FrameworkException：版本号校验是常编译的，Release 下同样生效，
    ///     因为 State 会被池复用，放过陈旧拷贝将导致它静默写入别人的缓冲；Disposed 标志则只在
    ///     编辑器与开发版下作为额外校验。
    ///   - 单个实例的缓冲上限约 1M 个字符（<see cref="MaxGrowCapacity"/>），越界抛 FrameworkException。
    /// 注意：<see cref="AsSpan"/> 返回的 Span 在 Dispose 之后即失效，不得跨 using 作用域使用。
    /// </summary>
    public readonly ref struct TempText
    {
        /// <summary>
        /// TempText 的可变状态。放在托管对象中以规避 readonly 结构体变量的防御性拷贝问题，并由线程私有的状态池复用，稳态零分配。
        /// </summary>
        private sealed class State
        {
            public char[] Buffer;
            public int Length;

            /// <summary>
            /// 每次 Dispose 递增，用于识别指向"已归还并可能被复用"状态的陈旧 TempText 拷贝。
            /// </summary>
            public int Version;

            /// <summary>
            /// 与 <see cref="Version"/> 校验在功能上冗余（Dispose 必定同时置位并递增版本号），
            /// 保留作纵深防御：版本号回绕或后续改动破坏递增时仍能拦住一部分误用。仅编辑器/开发版校验。
            /// </summary>
            public bool Disposed;
        }

        /// <summary>
        /// 每线程缓存的状态对象数量上限。
        /// </summary>
        private const int MaxCachedStates = 8;

        /// <summary>
        /// 单个 TempText 底层缓冲的全局硬上限（约 1M 个字符）。所有扩容路径（追加内容与数值格式化重试）
        /// 都经 <see cref="CalculateNewCapacity"/> 受此约束，越界抛 FrameworkException：
        /// 既防止异常格式化导致的无限扩容，也避免误用把临时缓冲撑成内存黑洞。
        /// </summary>
        private const int MaxGrowCapacity = 1 << 20;

        [ThreadStatic]
        private static State[] s_StateCache;

        [ThreadStatic]
        private static int s_StateCacheCount;

        private readonly State m_State;
        private readonly int m_Version;

        private TempText(State state)
        {
            m_State = state;
            m_Version = state.Version;
        }

        /// <summary>
        /// 当前已写入的字符数。
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
        /// 当前底层缓冲的容量。
        /// </summary>
        public int Capacity
        {
            get
            {
                CheckUsable();
                char[] buffer = m_State.Buffer;
                return buffer != null ? buffer.Length : 0;
            }
        }

        /// <summary>
        /// 租借一个临时字符串构建器，必须配合 using 或显式 <see cref="Dispose"/> 使用。
        /// </summary>
        /// <param name="capacity">初始容量，小于 1 时按 1 处理。</param>
        /// <returns>可用的构建器实例。</returns>
        public static TempText Rent(int capacity = 64)
        {
            State state = RentState();
            state.Buffer = CharBufferPool.Rent(capacity);
            state.Length = 0;
            state.Disposed = false;
            return new TempText(state);
        }

        /// <summary>
        /// 追加字符串，null 视为空串。
        /// </summary>
        /// <param name="value">待追加的字符串。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(string value)
        {
            if (value == null)
            {
                CheckUsable();
                return this;
            }

            return Append(value.AsSpan());
        }

        /// <summary>
        /// 追加字符序列。
        /// </summary>
        /// <param name="value">待追加的字符序列。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(ReadOnlySpan<char> value)
        {
            CheckUsable();
            if (value.Length == 0)
            {
                return this;
            }

            int required = m_State.Length + value.Length;
            if (required > m_State.Buffer.Length)
            {
                // 扩容与拷贝在此就地完成，而不是先 EnsureCapacity 再拷贝：
                // 后者会在拷贝前把旧缓冲还给池，一旦 value 是本对象自身缓冲的切片
                // （如 t.Append(t.AsSpan())），源数据就已经处于"已归还"状态，
                // 正确性只能依赖"其间没有别的租借"这一巧合。
                char[] oldBuffer = m_State.Buffer;
                char[] newBuffer = CharBufferPool.Rent(CalculateNewCapacity(required));
                Array.Copy(oldBuffer, 0, newBuffer, 0, m_State.Length);
                value.CopyTo(new Span<char>(newBuffer, m_State.Length, value.Length));
                m_State.Buffer = newBuffer;
                m_State.Length = required;
                CharBufferPool.Return(oldBuffer);
                return this;
            }

            value.CopyTo(new Span<char>(m_State.Buffer, m_State.Length, value.Length));
            m_State.Length = required;
            return this;
        }

        /// <summary>
        /// 追加单个字符。
        /// </summary>
        /// <param name="value">待追加的字符。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(char value)
        {
            CheckUsable();
            EnsureCapacity(m_State.Length + 1);
            m_State.Buffer[m_State.Length] = value;
            m_State.Length++;
            return this;
        }

        /// <summary>
        /// 追加布尔值，输出 True 或 False，与 bool.ToString 一致但不分配。
        /// </summary>
        /// <param name="value">待追加的布尔值。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(bool value)
        {
            return Append(value ? "True".AsSpan() : "False".AsSpan());
        }

        /// <summary>
        /// 追加 32 位有符号整数。
        /// </summary>
        /// <param name="value">待追加的值。</param>
        /// <param name="format">可选的数值格式，如 "D4"。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(int value, string format = null)
        {
            CheckUsable();
            int written;
            while (!value.TryFormat(FreeSpan(), out written, format.AsSpan(), CultureInfo.InvariantCulture))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        /// <summary>
        /// 追加 64 位有符号整数。
        /// </summary>
        /// <param name="value">待追加的值。</param>
        /// <param name="format">可选的数值格式。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(long value, string format = null)
        {
            CheckUsable();
            int written;
            while (!value.TryFormat(FreeSpan(), out written, format.AsSpan(), CultureInfo.InvariantCulture))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        /// <summary>
        /// 追加 32 位无符号整数。
        /// </summary>
        /// <param name="value">待追加的值。</param>
        /// <param name="format">可选的数值格式。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(uint value, string format = null)
        {
            CheckUsable();
            int written;
            while (!value.TryFormat(FreeSpan(), out written, format.AsSpan(), CultureInfo.InvariantCulture))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        /// <summary>
        /// 追加 64 位无符号整数。
        /// </summary>
        /// <param name="value">待追加的值。</param>
        /// <param name="format">可选的数值格式。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(ulong value, string format = null)
        {
            CheckUsable();
            int written;
            while (!value.TryFormat(FreeSpan(), out written, format.AsSpan(), CultureInfo.InvariantCulture))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        /// <summary>
        /// 追加单精度浮点数，默认使用不变文化以保证跨语言环境输出一致。
        /// </summary>
        /// <param name="value">待追加的值。</param>
        /// <param name="format">可选的数值格式，如 "F1"。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(float value, string format = null)
        {
            CheckUsable();
            int written;
            while (!value.TryFormat(FreeSpan(), out written, format.AsSpan(), CultureInfo.InvariantCulture))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        /// <summary>
        /// 追加双精度浮点数。
        /// </summary>
        /// <param name="value">待追加的值。</param>
        /// <param name="format">可选的数值格式。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(double value, string format = null)
        {
            CheckUsable();
            int written;
            while (!value.TryFormat(FreeSpan(), out written, format.AsSpan(), CultureInfo.InvariantCulture))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        /// <summary>
        /// 清空已写入的内容，保留当前缓冲。
        /// </summary>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Clear()
        {
            CheckUsable();
            m_State.Length = 0;
            return this;
        }

        /// <summary>
        /// 获取已写入内容的只读视图，Dispose 之后该视图失效。
        /// </summary>
        /// <returns>指向内部缓冲的只读 Span。</returns>
        public ReadOnlySpan<char> AsSpan()
        {
            CheckUsable();
            return new ReadOnlySpan<char>(m_State.Buffer, 0, m_State.Length);
        }

        /// <summary>
        /// 把已写入内容拷贝到目标数组。
        /// </summary>
        /// <param name="dest">目标数组。</param>
        /// <param name="destIndex">目标起始下标。</param>
        public void CopyTo(char[] dest, int destIndex)
        {
            CheckUsable();
            if (dest == null)
            {
                throw new FrameworkException("Dest is invalid.");
            }

            if (destIndex < 0 || destIndex > dest.Length - m_State.Length)
            {
                throw new FrameworkException("Dest index is invalid.");
            }

            Array.Copy(m_State.Buffer, 0, dest, destIndex, m_State.Length);
        }

        /// <summary>
        /// 生成字符串，这是本类型唯一会产生托管分配的出口，热路径应优先使用 <see cref="AsSpan"/>。
        /// </summary>
        /// <returns>已写入内容组成的字符串。</returns>
        public override string ToString()
        {
            CheckUsable();
            return m_State.Length == 0 ? string.Empty : new string(m_State.Buffer, 0, m_State.Length);
        }

        /// <summary>
        /// 归还缓冲与状态对象，重复调用安全（幂等）。
        /// </summary>
        public void Dispose()
        {
            State state = m_State;
            if (state == null || state.Disposed || state.Version != m_Version)
            {
                return;
            }

            CharBufferPool.Return(state.Buffer);
            state.Buffer = null;
            state.Length = 0;
            state.Disposed = true;
            unchecked
            {
                state.Version++;
            }

            ReturnState(state);
        }

        private Span<char> FreeSpan()
        {
            char[] buffer = m_State.Buffer;
            return new Span<char>(buffer, m_State.Length, buffer.Length - m_State.Length);
        }

        private void EnsureCapacity(int required)
        {
            if (required <= m_State.Buffer.Length)
            {
                return;
            }

            Reallocate(CalculateNewCapacity(required));
        }

        /// <summary>
        /// 计算满足 required 的新容量：优先翻倍，并做上限兜底。
        /// </summary>
        private int CalculateNewCapacity(int required)
        {
            int newCapacity = m_State.Buffer.Length * 2;
            if (newCapacity < required)
            {
                newCapacity = required;
            }

            if (newCapacity > MaxGrowCapacity)
            {
                throw new FrameworkException("TempText grows beyond the maximum capacity.");
            }

            return newCapacity;
        }

        private void Grow()
        {
            // TryFormat 失败只说明剩余空间不足。CalculateNewCapacity 会取"翻倍"与"required"中的较大者，
            // 这里传 Length + 32 只是保证即便缓冲极小也能一次多出一档常见数值宽度。
            Reallocate(CalculateNewCapacity(m_State.Buffer.Length + 32));
        }

        private void Reallocate(int newCapacity)
        {
            char[] oldBuffer = m_State.Buffer;
            char[] newBuffer = CharBufferPool.Rent(newCapacity);
            Array.Copy(oldBuffer, 0, newBuffer, 0, m_State.Length);
            m_State.Buffer = newBuffer;
            CharBufferPool.Return(oldBuffer);
        }

        private void CheckUsable()
        {
            // 版本与 null 校验常编译（不受 UNITY_EDITOR 限制）：State 是池化复用的，
            // 若 Release 下放过陈旧副本，它会静默写入该 State 被重新租借后的新缓冲，造成极难排查的数据串扰。
            // 代价仅为一次 int 比较与一次引用比较。
            State state = m_State;
            if (state == null)
            {
                throw new FrameworkException("TempText is not initialized, use TempText.Rent to create one.");
            }

            if (state.Version != m_Version)
            {
                throw new FrameworkException("TempText has already been disposed.");
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (state.Disposed)
            {
                throw new FrameworkException("TempText has already been disposed.");
            }
#endif
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
    }
}
