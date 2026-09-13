//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 一次抽卡结果：命中的档位标识、具体物品标识，以及是否由保底触发（不可变值类型）。
    /// </summary>
    public readonly struct GachaResult
    {
        private readonly string m_TierId;
        private readonly string m_ItemId;
        private readonly bool m_WasPity;

        /// <summary>
        /// 构造一次抽卡结果。
        /// </summary>
        /// <param name="tierId">命中的档位标识。</param>
        /// <param name="itemId">命中的具体物品标识。</param>
        /// <param name="wasPity">本次保底档命中是否由硬保底强制触发。</param>
        public GachaResult(string tierId, string itemId, bool wasPity)
        {
            m_TierId = tierId;
            m_ItemId = itemId;
            m_WasPity = wasPity;
        }

        /// <summary>
        /// 命中的档位标识。
        /// </summary>
        public string TierId
        {
            get { return m_TierId; }
        }

        /// <summary>
        /// 命中的具体物品标识。
        /// </summary>
        public string ItemId
        {
            get { return m_ItemId; }
        }

        /// <summary>
        /// 是否由硬保底强制命中保底档。
        /// </summary>
        public bool WasPity
        {
            get { return m_WasPity; }
        }
    }
}
