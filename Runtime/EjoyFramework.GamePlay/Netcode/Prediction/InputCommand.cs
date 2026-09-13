//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Netcode.Prediction
{
    /// <summary>
    /// 带序号与时间步长的「输入指令」。
    /// 客户端在本地预测时产生它，既发送给服务器，又缓存在本地待重放（reconcile）使用。
    /// 不可变只读结构体，避免在重放过程中被意外修改。
    /// </summary>
    /// <typeparam name="TInput">每帧输入的载荷类型（如移动方向、按键、瞄准等）。</typeparam>
    public readonly struct InputCommand<TInput>
    {
        private readonly uint m_Sequence;
        private readonly float m_DeltaTime;
        private readonly TInput m_Input;

        /// <summary>
        /// 构造一条输入指令。
        /// </summary>
        /// <param name="sequence">单调递增的序号，由客户端预测器分配，用于和服务器 ack 对齐。</param>
        /// <param name="deltaTime">该输入对应的模拟时间步长（秒）。</param>
        /// <param name="input">输入载荷。</param>
        public InputCommand(uint sequence, float deltaTime, TInput input)
        {
            m_Sequence = sequence;
            m_DeltaTime = deltaTime;
            m_Input = input;
        }

        /// <summary>
        /// 该输入的序号。客户端按发出顺序单调递增分配，服务器通过 ack 该序号告知已处理到哪一条。
        /// </summary>
        public uint Sequence
        {
            get { return m_Sequence; }
        }

        /// <summary>
        /// 该输入对应的模拟时间步长（秒），重放时原样喂回 step 以保证确定性。
        /// </summary>
        public float DeltaTime
        {
            get { return m_DeltaTime; }
        }

        /// <summary>
        /// 输入载荷。
        /// </summary>
        public TInput Input
        {
            get { return m_Input; }
        }
    }
}
