//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 剧情变量存储：以字符串为键的类型化键值表，承载分支对话所需的旗标与状态。
    /// 支持 bool/int/float/string 四种类型，写入后通过 <see cref="OnChanged"/> 通知监听方。
    /// 同一键可被不同类型覆写（后写入的类型生效）；读取时按目标类型取值，类型不匹配则返回回退值。
    /// 纯逻辑、与引擎无关，单线程使用，非线程安全。
    /// </summary>
    public sealed class DialogueVariables
    {
        // 变量值的内部种类标签，与存储的装箱值配套，避免读取时的拆箱类型猜测。
        private enum Kind
        {
            Bool,
            Int,
            Float,
            String,
        }

        // 一个变量槽：种类 + 实际值（数值统一以基础类型存放）。
        private struct Slot
        {
            public Kind Kind;
            public bool BoolValue;
            public int IntValue;
            public float FloatValue;
            public string StringValue;
        }

        private readonly Dictionary<string, Slot> m_Slots =
            new Dictionary<string, Slot>(StringComparer.Ordinal);

        /// <summary>
        /// 任意变量写入并实际改变后触发：(存储自身, 变更的键)。
        /// 仅在键不存在或值/类型发生变化时触发，写入相同值不会重复触发。
        /// </summary>
        public event Action<DialogueVariables, string> OnChanged;

        /// <summary>
        /// 写入布尔值。
        /// </summary>
        /// <param name="key">变量键，不可为空。</param>
        /// <param name="value">布尔值。</param>
        public void SetBool(string key, bool value)
        {
            RequireKey(key);
            Slot slot = new Slot { Kind = Kind.Bool, BoolValue = value };
            Store(key, slot);
        }

        /// <summary>
        /// 写入整数值。
        /// </summary>
        /// <param name="key">变量键，不可为空。</param>
        /// <param name="value">整数值。</param>
        public void SetInt(string key, int value)
        {
            RequireKey(key);
            Slot slot = new Slot { Kind = Kind.Int, IntValue = value };
            Store(key, slot);
        }

        /// <summary>
        /// 写入浮点值。
        /// </summary>
        /// <param name="key">变量键，不可为空。</param>
        /// <param name="value">浮点值。</param>
        public void SetFloat(string key, float value)
        {
            RequireKey(key);
            Slot slot = new Slot { Kind = Kind.Float, FloatValue = value };
            Store(key, slot);
        }

        /// <summary>
        /// 写入字符串值。
        /// </summary>
        /// <param name="key">变量键，不可为空。</param>
        /// <param name="value">字符串值，可为 null。</param>
        public void SetString(string key, string value)
        {
            RequireKey(key);
            Slot slot = new Slot { Kind = Kind.String, StringValue = value };
            Store(key, slot);
        }

        /// <summary>
        /// 读取布尔值。键不存在或类型不是 Bool 时返回回退值。
        /// </summary>
        /// <param name="key">变量键。</param>
        /// <param name="fallback">回退值。</param>
        /// <returns>布尔值或回退值。</returns>
        public bool GetBool(string key, bool fallback = false)
        {
            if (key != null && m_Slots.TryGetValue(key, out Slot slot) && slot.Kind == Kind.Bool)
            {
                return slot.BoolValue;
            }

            return fallback;
        }

        /// <summary>
        /// 读取整数值。键不存在或类型不是 Int 时返回回退值。
        /// </summary>
        /// <param name="key">变量键。</param>
        /// <param name="fallback">回退值。</param>
        /// <returns>整数值或回退值。</returns>
        public int GetInt(string key, int fallback = 0)
        {
            if (key != null && m_Slots.TryGetValue(key, out Slot slot) && slot.Kind == Kind.Int)
            {
                return slot.IntValue;
            }

            return fallback;
        }

        /// <summary>
        /// 读取浮点值。键不存在或类型不是 Float 时返回回退值。
        /// </summary>
        /// <param name="key">变量键。</param>
        /// <param name="fallback">回退值。</param>
        /// <returns>浮点值或回退值。</returns>
        public float GetFloat(string key, float fallback = 0f)
        {
            if (key != null && m_Slots.TryGetValue(key, out Slot slot) && slot.Kind == Kind.Float)
            {
                return slot.FloatValue;
            }

            return fallback;
        }

        /// <summary>
        /// 读取字符串值。键不存在或类型不是 String 时返回回退值。
        /// </summary>
        /// <param name="key">变量键。</param>
        /// <param name="fallback">回退值，默认 null。</param>
        /// <returns>字符串值或回退值。</returns>
        public string GetString(string key, string fallback = null)
        {
            if (key != null && m_Slots.TryGetValue(key, out Slot slot) && slot.Kind == Kind.String)
            {
                return slot.StringValue;
            }

            return fallback;
        }

        /// <summary>
        /// 判断是否存在该键（任意类型）。
        /// </summary>
        /// <param name="key">变量键。</param>
        /// <returns>存在返回 true。</returns>
        public bool Has(string key)
        {
            return key != null && m_Slots.ContainsKey(key);
        }

        /// <summary>
        /// 内部读取整数视图，供条件求值将 Int/Float 统一为 double 比较使用。
        /// 仅当槽为数值类型时给出 double 表示。
        /// </summary>
        internal bool TryGetNumeric(string key, out double value)
        {
            if (key != null && m_Slots.TryGetValue(key, out Slot slot))
            {
                if (slot.Kind == Kind.Int)
                {
                    value = slot.IntValue;
                    return true;
                }

                if (slot.Kind == Kind.Float)
                {
                    value = slot.FloatValue;
                    return true;
                }
            }

            value = 0d;
            return false;
        }

        // 写入一个槽，仅在内容实际变化时落库并触发 OnChanged。
        private void Store(string key, Slot slot)
        {
            if (m_Slots.TryGetValue(key, out Slot existing) && SlotEquals(existing, slot))
            {
                return;
            }

            m_Slots[key] = slot;
            OnChanged?.Invoke(this, key);
        }

        // 判断两个槽是否等价（种类相同且值相同）。
        private static bool SlotEquals(Slot a, Slot b)
        {
            if (a.Kind != b.Kind)
            {
                return false;
            }

            switch (a.Kind)
            {
                case Kind.Bool:
                    return a.BoolValue == b.BoolValue;
                case Kind.Int:
                    return a.IntValue == b.IntValue;
                case Kind.Float:
                    // 浮点精确相等比较；写入相同字面量即视为无变化。
                    return a.FloatValue.Equals(b.FloatValue);
                case Kind.String:
                    return string.Equals(a.StringValue, b.StringValue, StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static void RequireKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("变量键不能为空。", nameof(key));
            }
        }
    }
}
