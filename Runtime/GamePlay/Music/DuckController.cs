//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Music
{
    /// <summary>
    /// 临时压低控制器（ducking，纯逻辑，引擎无关）。
    /// 用于在对白 / 重要音效期间临时降低 BGM 音量。支持多个压低请求叠加。
    /// <para>
    /// 语义：维护一组活动的压低请求，每个请求带一个目标倍率（target multiplier，[0,1]，1 表示不压低）。
    /// 生效目标 = 所有活动请求中“最强”（最低）的目标倍率；若无活动请求则为 1。
    /// </para>
    /// <para>
    /// <see cref="CurrentMultiplier"/> 在每次 <see cref="Tick"/> 中朝目标平滑移动：
    /// 朝更低（压低）移动时使用对应请求的 attack 时间，朝更高（恢复）移动时使用 release 时间。
    /// attack/release 表示“从满量程 1.0 走完所需的秒数”，按此速率线性逼近目标。
    /// </para>
    /// 单线程使用，非线程安全。
    /// <para>
    /// 作为框架模块：通过 <c>Framework.GetModule&lt;IDuckController&gt;()</c> 获取，需要无参构造函数。
    /// 每帧驱动经由 <see cref="Update"/>（内部转调 <see cref="Tick"/>）。
    /// </para>
    /// </summary>
    public sealed class DuckController : FrameworkModule, IDuckController
    {
        // 单个压低请求。
        private struct DuckRequest
        {
            public int Handle;
            public float Target;  // [0,1]
            public float Attack;  // 达到目标所需时间（秒）

            public DuckRequest(int handle, float target, float attack)
            {
                Handle = handle;
                Target = target;
                Attack = attack;
            }
        }

        private readonly List<DuckRequest> m_Requests = new List<DuckRequest>();

        private int m_NextHandle = 1;
        private float m_CurrentMultiplier = 1f;

        // 当前活动（生效）目标对应的 attack 时间（朝更低移动时使用）。
        private float m_ActiveAttack = 0f;

        // 最近一次 PopDuck 指定的 release 时间（朝更高移动时使用）。
        private float m_PendingRelease = 0f;

        /// <summary>
        /// 当前生效的压低倍率，范围 [0,1]，1 表示不压低。
        /// </summary>
        public float CurrentMultiplier
        {
            get { return m_CurrentMultiplier; }
        }

        /// <summary>
        /// 当前活动的压低请求数量。
        /// </summary>
        public int ActiveDuckCount
        {
            get { return m_Requests.Count; }
        }

        /// <summary>
        /// 模块优先级。无特殊顺序需求，返回 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 框架模块每帧轮询：以逻辑时间增量推进压低平滑。等价于调用 <see cref="Tick"/>(elapseSeconds)。
        /// </summary>
        /// <param name="elapseSeconds">逻辑时间增量（秒）。</param>
        /// <param name="realElapseSeconds">真实时间增量（秒），本模块不使用。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            Tick(elapseSeconds);
        }

        /// <summary>
        /// 关闭并清理运行期状态：清空所有压低请求，复位生效倍率为 1（不压低）及内部计时/句柄。
        /// </summary>
        public override void Shutdown()
        {
            m_Requests.Clear();
            m_NextHandle = 1;
            m_CurrentMultiplier = 1f;
            m_ActiveAttack = 0f;
            m_PendingRelease = 0f;
        }

        /// <summary>
        /// 压入一个压低请求：目标倍率 targetMultiplier，在 attackSeconds 内达到。
        /// </summary>
        /// <param name="targetMultiplier">目标倍率，自动钳制到 [0,1]。</param>
        /// <param name="attackSeconds">达到目标所需时间（秒），负值视为 0（瞬时）。</param>
        /// <returns>句柄 id，用于后续 <see cref="PopDuck"/>。</returns>
        public int PushDuck(float targetMultiplier, float attackSeconds)
        {
            float target = GameMath.Clamp01(targetMultiplier);
            float attack = attackSeconds < 0f ? 0f : attackSeconds;

            int handle = m_NextHandle++;
            m_Requests.Add(new DuckRequest(handle, target, attack));

            // 新请求可能使目标变强（变低），记录用于朝下移动的 attack。
            m_ActiveAttack = ResolveActiveAttack();
            return handle;
        }

        /// <summary>
        /// 弹出指定句柄的压低请求，并朝剩余请求中“最强”的目标（或 1）恢复，历时 releaseSeconds。
        /// </summary>
        /// <param name="handle">由 <see cref="PushDuck"/> 返回的句柄。无效句柄为空操作。</param>
        /// <param name="releaseSeconds">恢复时间（秒），负值视为 0（瞬时）。</param>
        public void PopDuck(int handle, float releaseSeconds)
        {
            float release = releaseSeconds < 0f ? 0f : releaseSeconds;

            bool removed = false;
            for (int i = 0; i < m_Requests.Count; i++)
            {
                if (m_Requests[i].Handle == handle)
                {
                    m_Requests.RemoveAt(i);
                    removed = true;
                    break;
                }
            }

            if (!removed)
            {
                return;
            }

            // 移除后目标通常上升（变弱），记录朝上移动的 release；同时刷新朝下用的 attack（若仍有更强请求）。
            m_PendingRelease = release;
            m_ActiveAttack = ResolveActiveAttack();
        }

        /// <summary>
        /// 推进平滑。deltaTime &lt;= 0 时不做任何处理。
        /// </summary>
        /// <param name="deltaTime">本帧时间增量（秒）。</param>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            float target = ComputeTarget();

            if (target < m_CurrentMultiplier)
            {
                // 朝更低（压低）移动，使用 attack。
                MoveToward(target, m_ActiveAttack, deltaTime);
            }
            else if (target > m_CurrentMultiplier)
            {
                // 朝更高（恢复）移动，使用 release。
                MoveToward(target, m_PendingRelease, deltaTime);
            }
            // 已到位则不动。

            m_CurrentMultiplier = GameMath.Clamp01(m_CurrentMultiplier);
        }

        // 以“满量程 1.0 历时 transition 秒”的速率朝 target 线性逼近，不越过 target。
        private void MoveToward(float target, float transition, float deltaTime)
        {
            if (transition <= 0f)
            {
                m_CurrentMultiplier = target;
                return;
            }

            float step = deltaTime / transition; // 本帧可移动的绝对量。
            float delta = target - m_CurrentMultiplier;
            float absDelta = delta < 0f ? -delta : delta;

            if (step >= absDelta)
            {
                m_CurrentMultiplier = target;
                return;
            }

            m_CurrentMultiplier += delta < 0f ? -step : step;
        }

        // 生效目标 = 所有活动请求中最低的 target；无请求则为 1。
        private float ComputeTarget()
        {
            float min = 1f;
            for (int i = 0; i < m_Requests.Count; i++)
            {
                if (m_Requests[i].Target < min)
                {
                    min = m_Requests[i].Target;
                }
            }

            return min;
        }

        // 找到当前生效（最强/最低）目标对应请求的 attack（同目标多请求取最大 attack，避免过快）。
        private float ResolveActiveAttack()
        {
            if (m_Requests.Count == 0)
            {
                return 0f;
            }

            float target = ComputeTarget();
            float attack = 0f;
            bool found = false;
            for (int i = 0; i < m_Requests.Count; i++)
            {
                if (IsApproximately(m_Requests[i].Target, target))
                {
                    if (!found || m_Requests[i].Attack > attack)
                    {
                        attack = m_Requests[i].Attack;
                        found = true;
                    }
                }
            }

            return attack;
        }

        private static bool IsApproximately(float a, float b)
        {
            float d = a - b;
            return (d < 0f ? -d : d) < 1e-6f;
        }
    }
}
