//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 零 GC 可绑定文本。内部持有 char 缓冲与有效长度，供 UI 层直接下发到
    /// TMP_Text.SetCharArray(char[], int, int)，绕开 string 分配。
    /// 典型用法（ViewModel 侧）：
    /// <code>
    /// public BindableText Hp { get; }          // 构造：Hp = CreateText();
    /// // 每帧刷新，稳态零分配：
    /// Hp.SetValue(current);
    /// // 或组合文本：
    /// using (var t = TempText.Rent(32)) { t.Append(cur).Append('/').Append(max); Hp.Set(t); }
    /// </code>
    /// 设计理由：
    ///   - 实例本身长期存活且引用不变，因此 ViewModel 不能靠"换属性值"来通知；这里由本对象在内容
    ///     真正变化时抛出 <see cref="PropertyChanged"/>，再由 <see cref="BindableObject.CreateText"/>
    ///     转发成宿主 ViewModel 的属性通知，binder 侧无需任何特殊订阅逻辑。
    ///   - 写入前先比较内容，内容未变则不通知，避免每帧无谓的 UI 重建（TMP 重排版代价很高）。
    ///   - 缓冲取自 <see cref="CharBufferPool"/>；扩容时旧缓冲立即归还，稳态下不再分配。
    ///   - 数值一律走 TryFormat 直写缓冲，杜绝 int/float.ToString() 的临时 string。
    /// 线程：与 BindableObject 一致，仅在主线程写入。
    /// 注意：把本类型暴露成 ViewModel 属性后必须重跑 MVVM accessor codegen，否则玩家构建下该绑定静默失效，
    /// 详见 <see cref="BindableObject.CreateText"/>。
    /// </summary>
    public sealed class BindableText : IBindable
    {
        /// <summary>
        /// 通知使用的属性名。宿主 ViewModel 转发时会替换为自身的属性名。
        /// </summary>
        public const string ValuePropertyName = "Value";

        /// <summary>
        /// 容量兜底上限，防止异常格式化导致的无限扩容。
        /// 注意它同样约束 <see cref="Set(ReadOnlySpan{char})"/> 这类字符串路径：单条 UI 文本超过 100 万字符
        /// 一定是业务把整块数据错当文本塞了进来，宁可抛错也不要悄悄吃掉几 MB 内存。
        /// </summary>
        private const int MaxGrowCapacity = 1 << 20;

        /// <summary>
        /// 首次写入时的默认容量，与 CharBufferPool 的最小档位对齐。
        /// </summary>
        private const int DefaultCapacity = 64;

        /// <summary>
        /// 数值格式化所需的最小尾部空闲空间。float/double 的最长输出也在此范围内，通常一次到位。
        /// </summary>
        private const int MinFormatRoom = 32;

        private static readonly char[] s_Empty = Array.Empty<char>();

        private FastEvent<IBindable, string> m_PropertyChanged;
        private char[] m_Buffer;
        private int m_Length;

        /// <summary>
        /// 创建可绑定文本。
        /// </summary>
        /// <param name="capacity">初始容量，小于 1 时延迟到首次写入再分配。</param>
        public BindableText(int capacity = 0)
        {
            if (capacity > 0)
            {
                m_Buffer = CharBufferPool.Rent(capacity);
            }

            m_Length = 0;
        }

        /// <summary>内容变更通知，属性名固定为 <see cref="ValuePropertyName"/>。</summary>
        public event Action<IBindable, string> PropertyChanged
        {
            add { (m_PropertyChanged ??= new FastEvent<IBindable, string>()).Add(value); }
            remove { m_PropertyChanged?.Remove(value); }
        }

        /// <summary>
        /// 底层缓冲。仅供 UI 层直通下发（如 TMP 的 SetCharArray）使用，调用方不得写入或长期持有，
        /// 因为扩容会换成另一块缓冲。缓冲长度通常大于 <see cref="Length"/>，有效内容只有前 Length 个字符。
        /// </summary>
        public char[] Buffer
        {
            get { return m_Buffer ?? s_Empty; }
        }

        /// <summary>有效字符数。</summary>
        public int Length
        {
            get { return m_Length; }
        }

        /// <summary>内容是否为空。</summary>
        public bool IsEmpty
        {
            get { return m_Length == 0; }
        }

        /// <summary>获取内容的只读视图，下次写入后失效。</summary>
        /// <returns>指向内部缓冲的只读 Span。</returns>
        public ReadOnlySpan<char> AsSpan()
        {
            return new ReadOnlySpan<char>(Buffer, 0, m_Length);
        }

        /// <summary>
        /// 用字符序列覆盖内容，内容未变则不触发通知。
        /// </summary>
        /// <param name="value">新内容。</param>
        /// <returns>内容是否发生变化。</returns>
        public bool Set(ReadOnlySpan<char> value)
        {
            if (Equals(value))
            {
                return false;
            }

            EnsureCapacity(value.Length, false);
            if (value.Length > 0)
            {
                value.CopyTo(new Span<char>(m_Buffer, 0, value.Length));
            }

            m_Length = value.Length;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 用 <see cref="TempText"/> 的内容覆盖，全程不产生 string。
        /// 按值传参而非 ref：C# 不允许把 using 变量当作 ref 实参，而 TempText 的可变状态本就放在
        /// 池化的托管对象里，任何拷贝操作的都是同一份状态，因此按值传递既安全又能直接写
        /// <c>text.Set(t)</c>。
        /// </summary>
        /// <param name="value">已写入内容的临时构建器。</param>
        /// <returns>内容是否发生变化。</returns>
        public bool Set(TempText value)
        {
            return Set(value.AsSpan());
        }

        /// <summary>
        /// 用字符串覆盖内容，null 视为空串。仅在文本本身就是 string（如本地化结果）时使用。
        /// </summary>
        /// <param name="value">新内容。</param>
        /// <returns>内容是否发生变化。</returns>
        public bool Set(string value)
        {
            return Set(value.AsSpan());
        }

        /// <summary>设置为 32 位有符号整数。</summary>
        /// <param name="value">数值。</param>
        /// <param name="format">可选数值格式，如 "D4"。</param>
        /// <returns>内容是否发生变化。</returns>
        public bool SetValue(int value, string format = null)
        {
            return SetFormatted(value, format);
        }

        /// <summary>设置为 64 位有符号整数。</summary>
        /// <param name="value">数值。</param>
        /// <param name="format">可选数值格式。</param>
        /// <returns>内容是否发生变化。</returns>
        public bool SetValue(long value, string format = null)
        {
            return SetFormatted(value, format);
        }

        /// <summary>设置为单精度浮点数，默认不变文化以保证跨语言环境输出一致。</summary>
        /// <param name="value">数值。</param>
        /// <param name="format">可选数值格式，如 "F1"。</param>
        /// <returns>内容是否发生变化。</returns>
        public bool SetValue(float value, string format = null)
        {
            return SetFormatted(value, format);
        }

        /// <summary>设置为双精度浮点数。</summary>
        /// <param name="value">数值。</param>
        /// <param name="format">可选数值格式。</param>
        /// <returns>内容是否发生变化。</returns>
        public bool SetValue(double value, string format = null)
        {
            return SetFormatted(value, format);
        }

        /// <summary>
        /// 设置为任意可格式化的值（基础类型、枚举、已注册 <see cref="ITextFormatter{T}"/> 的类型零分配且不装箱）。
        /// </summary>
        /// <typeparam name="T">值类型。</typeparam>
        /// <param name="value">值。</param>
        /// <param name="format">可选格式串。</param>
        /// <returns>内容是否发生变化。</returns>
        public bool SetValue<T>(T value, string format = null)
        {
            return SetFormatted(value, format);
        }

        private bool SetFormatted<T>(T value, string format)
        {
            ITextFormatter<T> formatter = TextFormatter.Get<T>();
            int written;
            while (!formatter.TryFormat(value, TailSpan(), out written, format.AsSpan()))
            {
                Grow();
            }

            return Commit(written);
        }

        /// <summary>清空内容，已为空时不触发通知。</summary>
        /// <returns>内容是否发生变化。</returns>
        public bool Clear()
        {
            if (m_Length == 0)
            {
                return false;
            }

            m_Length = 0;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 强制触发一次通知，即使内容未变。用于 UI 重新绑定后的补刷。
        /// </summary>
        public void ForceNotify()
        {
            RaiseChanged();
        }

        /// <summary>比较当前内容与给定字符序列是否相同。</summary>
        /// <param name="value">待比较的字符序列。</param>
        /// <returns>相同返回 true。</returns>
        public bool Equals(ReadOnlySpan<char> value)
        {
            if (value.Length != m_Length)
            {
                return false;
            }

            return m_Length == 0 || value.SequenceEqual(new ReadOnlySpan<char>(m_Buffer, 0, m_Length));
        }

        /// <summary>
        /// 生成字符串。这是唯一会产生托管分配的出口，仅供无 char[] 接口的 UI 组件（如 uGUI Text）与调试使用。
        /// </summary>
        /// <returns>当前内容组成的字符串。</returns>
        public override string ToString()
        {
            return m_Length == 0 ? string.Empty : new string(m_Buffer, 0, m_Length);
        }

        /// <summary>
        /// 数值格式化的落点：缓冲中 [Length, 容量) 的空闲尾部。
        /// 之所以不直接写头部，是为了保住"内容未变则不通知"的语义 —— 旧内容必须保留到比较之后，
        /// TMP 的重排版代价很高，每帧无谓刷新是不可接受的。
        /// </summary>
        private Span<char> TailSpan()
        {
            EnsureCapacity(m_Length + MinFormatRoom, true);
            return new Span<char>(m_Buffer, m_Length, m_Buffer.Length - m_Length);
        }

        /// <summary>
        /// 比较尾部新写入的内容与头部旧内容，不同则前移到头部并通知。
        /// </summary>
        private bool Commit(int written)
        {
            if (written == m_Length
                && new ReadOnlySpan<char>(m_Buffer, m_Length, written).SequenceEqual(new ReadOnlySpan<char>(m_Buffer, 0, m_Length)))
            {
                return false;
            }

            // Array.Copy 允许源与目标重叠，语义等价于 memmove。
            Array.Copy(m_Buffer, m_Length, m_Buffer, 0, written);
            m_Length = written;
            RaiseChanged();
            return true;
        }

        /// <param name="required">所需的最小容量。</param>
        /// <param name="preserve">是否保留已有的前 <see cref="Length"/> 个字符。</param>
        private void EnsureCapacity(int required, bool preserve)
        {
            if (m_Buffer != null && required <= m_Buffer.Length)
            {
                return;
            }

            // 兜底：任何数值格式化的输出都远小于该上限，越过说明格式化行为异常，宁可抛错也不要无限扩容。
            if (required > MaxGrowCapacity)
            {
                throw new FrameworkException("BindableText grows beyond the maximum capacity.");
            }

            int newCapacity = m_Buffer == null ? DefaultCapacity : m_Buffer.Length * 2;
            if (newCapacity < required)
            {
                newCapacity = required;
            }

            char[] newBuffer = CharBufferPool.Rent(newCapacity);
            char[] oldBuffer = m_Buffer;
            if (preserve && oldBuffer != null && m_Length > 0)
            {
                Array.Copy(oldBuffer, 0, newBuffer, 0, m_Length);
            }

            m_Buffer = newBuffer;
            CharBufferPool.Return(oldBuffer);
        }

        private void Grow()
        {
            EnsureCapacity((m_Buffer == null ? DefaultCapacity : m_Buffer.Length) + MinFormatRoom, true);
        }

        private void RaiseChanged()
        {
            m_PropertyChanged?.Invoke(this, ValuePropertyName, "BindableText");
        }
    }
}
