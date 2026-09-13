//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 缓动管理器。持有活跃 Tween 列表，逐帧推进并移除已完成 / 已杀死的项。
    ///
    /// 变更安全：Tick 期间若某个 Tween 的 OnComplete 回调调用 <see cref="Add"/> 或 Kill，
    /// 不会破坏正在进行的遍历——本帧用一个复用快照缓冲遍历；遍历过程中新加入的 Tween
    /// 先暂存在待加入缓冲，遍历结束后并入活跃列表（即下一帧才开始推进），从而避免
    /// "遍历时修改集合" 在 Mono / IL2CPP 下抛异常。
    /// 单线程使用，非线程安全。
    ///
    /// 作为 <see cref="FrameworkModule"/> 提供，可通过 <c>Framework.GetModule&lt;ITweenManager&gt;()</c> 访问。
    /// 框架以 <see cref="Update"/> 逐帧驱动；其内部直接调用 <see cref="Tick"/>。
    /// </summary>
    public sealed class TweenManager : FrameworkModule, ITweenManager
    {
        private readonly List<Tween> m_ActiveTweens = new List<Tween>(64);
        private readonly List<Tween> m_TickBuffer = new List<Tween>(64);
        private readonly List<Tween> m_PendingAdds = new List<Tween>(16);

        private bool m_Ticking;

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 当前活跃（含待加入）的 Tween 数量。
        /// </summary>
        public int ActiveCount
        {
            get { return m_ActiveTweens.Count + m_PendingAdds.Count; }
        }

        /// <summary>
        /// 游戏框架模块轮询：以本帧时间增量推进所有活跃 Tween。
        /// </summary>
        /// <param name="elapseSeconds">逻辑时间增量（秒）。</param>
        /// <param name="realElapseSeconds">真实时间增量（秒，未使用）。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            Tick(elapseSeconds);
        }

        /// <summary>
        /// 关闭并清理模块：杀死并移除所有活跃 Tween，清空所有缓冲。
        /// </summary>
        public override void Shutdown()
        {
            KillAll(false);
            m_ActiveTweens.Clear();
            m_TickBuffer.Clear();
            m_PendingAdds.Clear();
        }

        /// <summary>
        /// 加入一个 Tween 进行驱动。重复加入同一实例会被忽略。
        /// 若在 Tick 过程中调用，会暂存到待加入缓冲，本帧结束后并入。
        /// </summary>
        /// <param name="tween">要加入的 Tween，为空则忽略。</param>
        public void Add(Tween tween)
        {
            if (tween == null || tween.IsKilled || tween.IsComplete)
            {
                return;
            }

            if (m_Ticking)
            {
                if (!m_PendingAdds.Contains(tween) && !m_ActiveTweens.Contains(tween))
                {
                    m_PendingAdds.Add(tween);
                }
                return;
            }

            if (!m_ActiveTweens.Contains(tween))
            {
                m_ActiveTweens.Add(tween);
            }
        }

        /// <summary>
        /// 推进所有活跃 Tween 一帧，并移除已完成 / 已杀死的项。
        /// </summary>
        /// <param name="deltaTime">本帧时间增量（秒）。</param>
        public void Tick(float deltaTime)
        {
            int count = m_ActiveTweens.Count;
            if (count == 0)
            {
                FlushPendingAdds();
                return;
            }

            // 复制到复用缓冲后再遍历，回调期间对活跃列表的增删互不影响。
            m_TickBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                m_TickBuffer.Add(m_ActiveTweens[i]);
            }

            m_Ticking = true;
            try
            {
                for (int i = 0; i < m_TickBuffer.Count; i++)
                {
                    Tween tween = m_TickBuffer[i];
                    if (tween.IsKilled || tween.IsComplete)
                    {
                        continue;
                    }

                    tween.Tick(deltaTime);
                }
            }
            finally
            {
                m_Ticking = false;
            }

            // 反向遍历原活跃列表，安全移除已结束的项。
            for (int i = m_ActiveTweens.Count - 1; i >= 0; i--)
            {
                Tween tween = m_ActiveTweens[i];
                if (tween.IsKilled || tween.IsComplete)
                {
                    m_ActiveTweens.RemoveAt(i);
                }
            }

            m_TickBuffer.Clear();
            FlushPendingAdds();
        }

        /// <summary>
        /// 杀死并移除所有活跃 Tween。
        /// </summary>
        /// <param name="complete">为 true 时每个 Tween 都快照到终点并触发 OnComplete。</param>
        public void KillAll(bool complete = false)
        {
            // 先把待加入并入，确保它们也被处理。
            FlushPendingAdds();

            // 快照后遍历，Kill 回调可能再次操作管理器。
            m_TickBuffer.Clear();
            for (int i = 0; i < m_ActiveTweens.Count; i++)
            {
                m_TickBuffer.Add(m_ActiveTweens[i]);
            }

            m_Ticking = true;
            try
            {
                for (int i = 0; i < m_TickBuffer.Count; i++)
                {
                    Tween tween = m_TickBuffer[i];
                    if (!tween.IsKilled)
                    {
                        tween.Kill(complete);
                    }
                }
            }
            finally
            {
                m_Ticking = false;
            }

            m_ActiveTweens.Clear();
            m_TickBuffer.Clear();

            // KillAll 期间若回调又加入新 Tween，并入以待下帧；但通常应被清空，这里保留语义一致性。
            FlushPendingAdds();
        }

        private void FlushPendingAdds()
        {
            if (m_PendingAdds.Count == 0)
            {
                return;
            }

            for (int i = 0; i < m_PendingAdds.Count; i++)
            {
                Tween tween = m_PendingAdds[i];
                if (tween == null || tween.IsKilled || tween.IsComplete)
                {
                    continue;
                }
                if (!m_ActiveTweens.Contains(tween))
                {
                    m_ActiveTweens.Add(tween);
                }
            }

            m_PendingAdds.Clear();
        }
    }
}
