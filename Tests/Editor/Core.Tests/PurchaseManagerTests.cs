//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Purchase;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// PurchaseManager 单测。
    /// 覆盖：未初始化购买 → NotInitialized；初始化置 IsInitialized；成功购买触发 PurchaseCompleted(Success)；
    /// 校验器返回 false → ReceiptInvalid；后端取消(Cancelled)透传；GetProduct / GetProducts。
    /// </summary>
    public class PurchaseManagerTests
    {
        /// <summary>可配置结果的假后端。</summary>
        private sealed class FakeBackend : IPurchaseBackend
        {
            public bool InitResult = true;
            public bool Initialized;
            public PurchaseResultCode NextPurchaseCode = PurchaseResultCode.Success;
            public string NextReceipt = "fake-receipt";
            public string LastPurchasedId;
            public int PurchaseCalls;
            public bool RestoreResult = true;

            public bool IsInitialized { get { return Initialized; } }

            public void Initialize(IReadOnlyList<PurchaseProduct> products, Action<bool> onInitialized)
            {
                Initialized = InitResult;
                if (onInitialized != null) onInitialized(InitResult);
            }

            public void Purchase(string productId, Action<PurchaseResult> onResult)
            {
                PurchaseCalls++;
                LastPurchasedId = productId;
                if (onResult != null)
                {
                    onResult(new PurchaseResult
                    {
                        ProductId = productId,
                        Code = NextPurchaseCode,
                        Receipt = NextReceipt,
                        TransactionId = "tx-1",
                    });
                }
            }

            public void RestorePurchases(Action<bool> onDone)
            {
                if (onDone != null) onDone(RestoreResult);
            }
        }

        /// <summary>可配置结果的假校验器。</summary>
        private sealed class FakeValidator : IReceiptValidator
        {
            public bool ValidResult = true;
            public int ValidateCalls;
            public PurchaseResult LastValidated;

            public void Validate(PurchaseResult purchase, Action<bool> onValid)
            {
                ValidateCalls++;
                LastValidated = purchase;
                if (onValid != null) onValid(ValidResult);
            }
        }

        private static List<PurchaseProduct> SampleProducts()
        {
            return new List<PurchaseProduct>
            {
                new PurchaseProduct { ProductId = "gold_100", Type = PurchaseProductType.Consumable, Price = 6m, CurrencyCode = "CNY" },
                new PurchaseProduct { ProductId = "no_ads", Type = PurchaseProductType.NonConsumable, Price = 12m, CurrencyCode = "CNY" },
            };
        }

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void Purchase_NotInitialized_ReportsNotInitialized()
        {
            var pm = new PurchaseManager();
            PurchaseResult result = default;
            int completedCount = 0;
            pm.PurchaseCompleted += r => completedCount++;

            pm.Purchase("gold_100", r => result = r);

            Assert.IsFalse(pm.IsInitialized);
            Assert.AreEqual(PurchaseResultCode.NotInitialized, result.Code);
            Assert.AreEqual("gold_100", result.ProductId);
            Assert.AreEqual(1, completedCount, "未初始化购买也应触发 PurchaseCompleted");
        }

        [Test]
        public void Initialize_SetsIsInitialized()
        {
            var pm = new PurchaseManager();
            pm.SetBackend(new FakeBackend());

            bool? initOk = null;
            pm.Initialize(SampleProducts(), ok => initOk = ok);

            Assert.IsTrue(pm.IsInitialized);
            Assert.AreEqual(true, initOk);
        }

        [Test]
        public void Initialize_BackendFails_NotInitialized()
        {
            var pm = new PurchaseManager();
            pm.SetBackend(new FakeBackend { InitResult = false });

            bool? initOk = null;
            pm.Initialize(SampleProducts(), ok => initOk = ok);

            Assert.IsFalse(pm.IsInitialized);
            Assert.AreEqual(false, initOk);
        }

        [Test]
        public void Purchase_Success_FiresPurchaseCompletedWithSuccess()
        {
            var pm = new PurchaseManager();
            var backend = new FakeBackend { NextPurchaseCode = PurchaseResultCode.Success };
            pm.SetBackend(backend);
            pm.Initialize(SampleProducts());

            PurchaseResult fromEvent = default;
            int completedCount = 0;
            pm.PurchaseCompleted += r => { fromEvent = r; completedCount++; };

            PurchaseResult fromCallback = default;
            pm.Purchase("gold_100", r => fromCallback = r);

            Assert.AreEqual(1, backend.PurchaseCalls);
            Assert.AreEqual("gold_100", backend.LastPurchasedId);
            Assert.AreEqual(PurchaseResultCode.Success, fromCallback.Code);
            Assert.AreEqual(PurchaseResultCode.Success, fromEvent.Code);
            Assert.AreEqual(1, completedCount);
        }

        [Test]
        public void Purchase_ValidatorReturnsFalse_ReportsReceiptInvalid()
        {
            var pm = new PurchaseManager();
            pm.SetBackend(new FakeBackend { NextPurchaseCode = PurchaseResultCode.Success });
            var validator = new FakeValidator { ValidResult = false };
            pm.SetReceiptValidator(validator);
            pm.Initialize(SampleProducts());

            PurchaseResult result = default;
            pm.Purchase("gold_100", r => result = r);

            Assert.AreEqual(1, validator.ValidateCalls, "成功购买应触发收据校验");
            Assert.AreEqual(PurchaseResultCode.ReceiptInvalid, result.Code);
        }

        [Test]
        public void Purchase_ValidatorReturnsTrue_ReportsSuccess()
        {
            var pm = new PurchaseManager();
            pm.SetBackend(new FakeBackend { NextPurchaseCode = PurchaseResultCode.Success });
            var validator = new FakeValidator { ValidResult = true };
            pm.SetReceiptValidator(validator);
            pm.Initialize(SampleProducts());

            PurchaseResult result = default;
            pm.Purchase("gold_100", r => result = r);

            Assert.AreEqual(1, validator.ValidateCalls);
            Assert.AreEqual(PurchaseResultCode.Success, result.Code);
        }

        [Test]
        public void Purchase_BackendCancelled_PassesThroughWithoutValidation()
        {
            var pm = new PurchaseManager();
            pm.SetBackend(new FakeBackend { NextPurchaseCode = PurchaseResultCode.Cancelled });
            var validator = new FakeValidator { ValidResult = true };
            pm.SetReceiptValidator(validator);
            pm.Initialize(SampleProducts());

            PurchaseResult result = default;
            pm.Purchase("gold_100", r => result = r);

            Assert.AreEqual(PurchaseResultCode.Cancelled, result.Code);
            Assert.AreEqual(0, validator.ValidateCalls, "非成功结果不应触发校验");
        }

        [Test]
        public void GetProduct_And_GetProducts()
        {
            var pm = new PurchaseManager();
            pm.SetBackend(new FakeBackend());
            var products = SampleProducts();
            pm.Initialize(products);

            Assert.AreEqual(2, pm.GetProducts().Count);
            Assert.IsNotNull(pm.GetProduct("gold_100"));
            Assert.AreEqual(PurchaseProductType.Consumable, pm.GetProduct("gold_100").Type);
            Assert.IsNotNull(pm.GetProduct("no_ads"));
            Assert.IsNull(pm.GetProduct("does_not_exist"));
            Assert.IsNull(pm.GetProduct(null));
        }

        [Test]
        public void SetBackendNull_RevertsToNotInitialized()
        {
            var pm = new PurchaseManager();
            pm.SetBackend(new FakeBackend());
            pm.Initialize(SampleProducts());
            Assert.IsTrue(pm.IsInitialized);

            pm.SetBackend(null);
            Assert.IsFalse(pm.IsInitialized);

            PurchaseResult result = default;
            pm.Purchase("gold_100", r => result = r);
            Assert.AreEqual(PurchaseResultCode.NotInitialized, result.Code);
        }

        [Test]
        public void RestorePurchases_NotInitialized_ReportsFalse()
        {
            var pm = new PurchaseManager();
            bool? done = null;
            pm.RestorePurchases(ok => done = ok);
            Assert.AreEqual(false, done);
        }

        [Test]
        public void RestorePurchases_Initialized_ForwardsResult()
        {
            var pm = new PurchaseManager();
            pm.SetBackend(new FakeBackend { RestoreResult = true });
            pm.Initialize(SampleProducts());

            bool? done = null;
            pm.RestorePurchases(ok => done = ok);
            Assert.AreEqual(true, done);
        }
    }
}
