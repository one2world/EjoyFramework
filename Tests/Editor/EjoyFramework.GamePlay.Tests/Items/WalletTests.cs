//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Items;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Items
{
    /// <summary>
    /// 针对 <see cref="Wallet"/> 余额增减/原子扣费/事件语义的单元测试。
    /// </summary>
    [TestFixture]
    public class WalletTests
    {
        [Test]
        public void Add_Then_GetBalance()
        {
            Wallet wallet = new Wallet();

            wallet.Add("gold", 100);
            wallet.Add("gold", 50);

            Assert.AreEqual(150, wallet.GetBalance("gold"));
            // 未持有的货币余额为 0。
            Assert.AreEqual(0, wallet.GetBalance("gem"));
        }

        [Test]
        public void Add_Overflow_Throws_PreservesBalance()
        {
            Wallet wallet = new Wallet();
            wallet.Add("gold", long.MaxValue);

            // 再加任何正数都会溢出：应显式抛出而非回绕为负，且余额保持不变。
            Assert.Throws<OverflowException>(() => wallet.Add("gold", 1));
            Assert.AreEqual(long.MaxValue, wallet.GetBalance("gold"));
        }

        [Test]
        public void TrySpend_Success_ReducesBalance()
        {
            Wallet wallet = new Wallet();
            wallet.Add("gold", 100);

            bool ok = wallet.TrySpend("gold", 30);

            Assert.IsTrue(ok);
            Assert.AreEqual(70, wallet.GetBalance("gold"));
        }

        [Test]
        public void TrySpend_Insufficient_ReturnsFalse_LeavesBalance()
        {
            Wallet wallet = new Wallet();
            wallet.Add("gold", 20);

            bool ok = wallet.TrySpend("gold", 50);

            Assert.IsFalse(ok);
            Assert.AreEqual(20, wallet.GetBalance("gold"));
        }

        [Test]
        public void CanAfford_ReflectsBalance()
        {
            Wallet wallet = new Wallet();
            wallet.Add("gem", 10);

            Assert.IsTrue(wallet.CanAfford("gem", 10));
            Assert.IsFalse(wallet.CanAfford("gem", 11));
            // 非正需求始终可支付。
            Assert.IsTrue(wallet.CanAfford("gem", 0));
            Assert.IsTrue(wallet.CanAfford("missing", 0));
        }

        [Test]
        public void Set_OverwritesBalance()
        {
            Wallet wallet = new Wallet();
            wallet.Add("energy", 5);

            wallet.Set("energy", 42);

            Assert.AreEqual(42, wallet.GetBalance("energy"));
        }

        [Test]
        public void Add_Negative_Throws()
        {
            Wallet wallet = new Wallet();
            Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Add("gold", -1));
        }

        [Test]
        public void TrySpend_Negative_Throws()
        {
            Wallet wallet = new Wallet();
            Assert.Throws<ArgumentOutOfRangeException>(() => wallet.TrySpend("gold", -1));
        }

        [Test]
        public void Set_Negative_Throws()
        {
            Wallet wallet = new Wallet();
            Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Set("gold", -1));
        }

        [Test]
        public void EmptyCurrencyId_Throws()
        {
            Wallet wallet = new Wallet();
            Assert.Throws<ArgumentException>(() => wallet.Add(null, 1));
            Assert.Throws<ArgumentException>(() => wallet.TrySpend(string.Empty, 1));
            Assert.Throws<ArgumentException>(() => wallet.Set(null, 1));
        }

        [Test]
        public void OnBalanceChanged_ReportsOldAndNew()
        {
            Wallet wallet = new Wallet();
            string capturedCurrency = null;
            long capturedOld = -1;
            long capturedNew = -1;
            int calls = 0;

            wallet.OnBalanceChanged += (w, currency, oldBal, newBal) =>
            {
                Assert.AreSame(wallet, w);
                capturedCurrency = currency;
                capturedOld = oldBal;
                capturedNew = newBal;
                calls++;
            };

            wallet.Add("gold", 100);
            Assert.AreEqual(1, calls);
            Assert.AreEqual("gold", capturedCurrency);
            Assert.AreEqual(0, capturedOld);
            Assert.AreEqual(100, capturedNew);

            wallet.TrySpend("gold", 40);
            Assert.AreEqual(2, calls);
            Assert.AreEqual(100, capturedOld);
            Assert.AreEqual(60, capturedNew);

            wallet.Set("gold", 5);
            Assert.AreEqual(3, calls);
            Assert.AreEqual(60, capturedOld);
            Assert.AreEqual(5, capturedNew);
        }

        [Test]
        public void OnBalanceChanged_NotFired_OnNoOp()
        {
            Wallet wallet = new Wallet();
            wallet.Add("gold", 10);

            int calls = 0;
            wallet.OnBalanceChanged += (w, c, o, n) => calls++;

            // 加 0：不触发。
            wallet.Add("gold", 0);
            // 扣 0：成功但不改变余额，不触发。
            Assert.IsTrue(wallet.TrySpend("gold", 0));
            // 设置为相同值：不触发。
            wallet.Set("gold", 10);
            // 扣费失败：不触发。
            Assert.IsFalse(wallet.TrySpend("gold", 999));

            Assert.AreEqual(0, calls);
            Assert.AreEqual(10, wallet.GetBalance("gold"));
        }

        [Test]
        public void Balances_ReadOnlyView_ReflectsState()
        {
            Wallet wallet = new Wallet();
            wallet.Add("gold", 100);
            wallet.Add("gem", 7);

            Assert.AreEqual(2, wallet.Balances.Count);
            Assert.AreEqual(100, wallet.Balances["gold"]);
            Assert.AreEqual(7, wallet.Balances["gem"]);
        }
    }
}
