//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Items
{
    /// <summary>
    /// 货币钱包：以任意字符串作为货币标识（如 "gold"、"gem"、"energy"），余额为非负 long。
    /// 余额永不为负；<see cref="TrySpend"/> 为原子操作（失败时不改动任何余额）。
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class Wallet
    {
        private readonly Dictionary<string, long> m_Balances =
            new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>
        /// 余额变更时触发：(钱包, 货币标识, 旧余额, 新余额)。仅在余额实际变化时触发。
        /// </summary>
        public event Action<Wallet, string, long, long> OnBalanceChanged;

        /// <summary>
        /// 全部货币余额的只读视图。
        /// </summary>
        public IReadOnlyDictionary<string, long> Balances
        {
            get { return m_Balances; }
        }

        /// <summary>
        /// 获取指定货币的余额。未持有时返回 0。
        /// </summary>
        /// <param name="currencyId">货币标识。</param>
        /// <returns>余额。</returns>
        public long GetBalance(string currencyId)
        {
            if (string.IsNullOrEmpty(currencyId))
            {
                return 0;
            }

            long balance;
            return m_Balances.TryGetValue(currencyId, out balance) ? balance : 0;
        }

        /// <summary>
        /// 增加指定货币的余额。
        /// </summary>
        /// <param name="currencyId">货币标识，不可为空。</param>
        /// <param name="amount">增加的数量，必须为非负。</param>
        /// <exception cref="ArgumentException">当货币标识为空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">当 amount 为负时抛出。</exception>
        public void Add(string currencyId, long amount)
        {
            RequireCurrencyId(currencyId);
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "增加的数量不能为负。");
            }

            if (amount == 0)
            {
                return;
            }

            long oldBalance = GetBalance(currencyId);
            if (amount > long.MaxValue - oldBalance)
            {
                // 显式拒绝而非静默回绕：oldBalance + amount 溢出会得到负余额，破坏「余额永不为负」不变量。
                throw new OverflowException(
                    string.Format("货币「{0}」余额溢出：{1} + {2} 超出 long 上限。", currencyId, oldBalance, amount));
            }

            long newBalance = oldBalance + amount;
            m_Balances[currencyId] = newBalance;
            RaiseChanged(currencyId, oldBalance, newBalance);
        }

        /// <summary>
        /// 尝试扣除指定货币。原子操作：余额不足时不做任何修改并返回 false。
        /// </summary>
        /// <param name="currencyId">货币标识，不可为空。</param>
        /// <param name="amount">扣除的数量，必须为非负。</param>
        /// <returns>扣除成功返回 true；余额不足返回 false。</returns>
        /// <exception cref="ArgumentException">当货币标识为空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">当 amount 为负时抛出。</exception>
        public bool TrySpend(string currencyId, long amount)
        {
            RequireCurrencyId(currencyId);
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "扣除的数量不能为负。");
            }

            long oldBalance = GetBalance(currencyId);
            if (oldBalance < amount)
            {
                return false;
            }

            if (amount == 0)
            {
                return true;
            }

            long newBalance = oldBalance - amount;
            m_Balances[currencyId] = newBalance;
            RaiseChanged(currencyId, oldBalance, newBalance);
            return true;
        }

        /// <summary>
        /// 判断是否足以支付指定数量的某货币。
        /// </summary>
        /// <param name="currencyId">货币标识。</param>
        /// <param name="amount">需要的数量。</param>
        /// <returns>余额足够返回 true。负数 amount 视为可支付。</returns>
        public bool CanAfford(string currencyId, long amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            return GetBalance(currencyId) >= amount;
        }

        /// <summary>
        /// 直接设置指定货币的余额。
        /// </summary>
        /// <param name="currencyId">货币标识，不可为空。</param>
        /// <param name="amount">目标余额，必须为非负。</param>
        /// <exception cref="ArgumentException">当货币标识为空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">当 amount 为负时抛出。</exception>
        public void Set(string currencyId, long amount)
        {
            RequireCurrencyId(currencyId);
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "余额不能为负。");
            }

            long oldBalance = GetBalance(currencyId);
            if (oldBalance == amount)
            {
                return;
            }

            m_Balances[currencyId] = amount;
            RaiseChanged(currencyId, oldBalance, amount);
        }

        private static void RequireCurrencyId(string currencyId)
        {
            if (string.IsNullOrEmpty(currencyId))
            {
                throw new ArgumentException("货币标识不能为空。", nameof(currencyId));
            }
        }

        private void RaiseChanged(string currencyId, long oldBalance, long newBalance)
        {
            Action<Wallet, string, long, long> handler = OnBalanceChanged;
            if (handler != null)
            {
                handler(this, currencyId, oldBalance, newBalance);
            }
        }
    }
}
