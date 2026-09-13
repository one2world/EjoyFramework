//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 对变量存储施加的一次副作用（赋值或累加）。当节点进入或选项被选中时应用。
    /// 通过类型化静态工厂创建。Add 仅对数值类型有意义。
    /// 累加时若目标键当前不存在或类型不符，以 0 为基底再累加（即等价于直接写入 delta）。
    /// 不可变值对象，可安全共享。
    /// </summary>
    public sealed class DialogueEffect
    {
        private enum Action
        {
            SetBool,
            SetInt,
            AddInt,
            SetFloat,
            AddFloat,
            SetString,
        }

        private readonly Action m_Action;
        private readonly string m_Variable;
        private readonly bool m_BoolValue;
        private readonly int m_IntValue;
        private readonly float m_FloatValue;
        private readonly string m_StringValue;

        private DialogueEffect(
            Action action,
            string variable,
            bool boolValue,
            int intValue,
            float floatValue,
            string stringValue)
        {
            if (string.IsNullOrEmpty(variable))
            {
                throw new ArgumentException("效果变量名不能为空。", nameof(variable));
            }

            m_Action = action;
            m_Variable = variable;
            m_BoolValue = boolValue;
            m_IntValue = intValue;
            m_FloatValue = floatValue;
            m_StringValue = stringValue;
        }

        /// <summary>
        /// 效果作用的变量名。
        /// </summary>
        public string Variable
        {
            get { return m_Variable; }
        }

        /// <summary>
        /// 创建“设置布尔值”效果。
        /// </summary>
        public static DialogueEffect SetBool(string variable, bool value)
        {
            return new DialogueEffect(Action.SetBool, variable, value, 0, 0f, null);
        }

        /// <summary>
        /// 创建“设置整数值”效果。
        /// </summary>
        public static DialogueEffect SetInt(string variable, int value)
        {
            return new DialogueEffect(Action.SetInt, variable, false, value, 0f, null);
        }

        /// <summary>
        /// 创建“整数累加”效果。基底取当前 Int 值（缺失/类型不符按 0）。
        /// </summary>
        public static DialogueEffect AddInt(string variable, int delta)
        {
            return new DialogueEffect(Action.AddInt, variable, false, delta, 0f, null);
        }

        /// <summary>
        /// 创建“设置浮点值”效果。
        /// </summary>
        public static DialogueEffect SetFloat(string variable, float value)
        {
            return new DialogueEffect(Action.SetFloat, variable, false, 0, value, null);
        }

        /// <summary>
        /// 创建“浮点累加”效果。基底取当前 Float 值（缺失/类型不符按 0）。
        /// </summary>
        public static DialogueEffect AddFloat(string variable, float delta)
        {
            return new DialogueEffect(Action.AddFloat, variable, false, 0, delta, null);
        }

        /// <summary>
        /// 创建“设置字符串值”效果。
        /// </summary>
        public static DialogueEffect SetString(string variable, string value)
        {
            return new DialogueEffect(Action.SetString, variable, false, 0, 0f, value);
        }

        /// <summary>
        /// 将效果应用到变量存储。
        /// </summary>
        /// <param name="vars">变量存储，不可为空。</param>
        /// <exception cref="ArgumentNullException">vars 为 null。</exception>
        public void Apply(DialogueVariables vars)
        {
            if (vars == null)
            {
                throw new ArgumentNullException(nameof(vars));
            }

            switch (m_Action)
            {
                case Action.SetBool:
                    vars.SetBool(m_Variable, m_BoolValue);
                    break;
                case Action.SetInt:
                    vars.SetInt(m_Variable, m_IntValue);
                    break;
                case Action.AddInt:
                    vars.SetInt(m_Variable, vars.GetInt(m_Variable, 0) + m_IntValue);
                    break;
                case Action.SetFloat:
                    vars.SetFloat(m_Variable, m_FloatValue);
                    break;
                case Action.AddFloat:
                    vars.SetFloat(m_Variable, vars.GetFloat(m_Variable, 0f) + m_FloatValue);
                    break;
                case Action.SetString:
                    vars.SetString(m_Variable, m_StringValue);
                    break;
            }
        }
    }
}
