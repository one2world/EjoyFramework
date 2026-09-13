//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Config;
using EjoyFramework.Core.DataTable;
using EjoyFramework.Core.Localization;
using EjoyFramework.Tests.TestSupport;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// Phase 5.2 — 数据三件套加载链回归。
    /// 直接验证 IConfigManager/ILocalizationManager/IDataTableManager 的 LoadXxx → Helper.ReadData → 入库 → 事件 链路。
    /// </summary>
    public class DataTrioLoadingTests
    {
        // ===== Config =====

        private sealed class StringConfigHelper : IConfigHelper
        {
            public int Releases;
            public bool ReadData(IConfigManager m, string a, object asset, object ud) => ParseData(m, (string)asset, ud);
            public bool ParseData(IConfigManager m, string text, object ud)
            {
                if (text == null) return false;
                foreach (var line in text.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var c = line.Trim().Split('\t');
                    if (c.Length < 5) continue;
                    m.AddConfig(c[0], c[1], c[2], c[3], c[4]);
                }
                return true;
            }
            public void ReleaseDataAsset(object a) { Releases++; }
        }

        [Test]
        public void Config_LoadAsset_PopulatesAndFiresSuccess()
        {
            var res = new MockResourceManager { AssetFactory = _ => "Title\thello\ttrue\t42\t3.14" };
            var cm = new ConfigManager(res);
            var helper = new StringConfigHelper();
            cm.SetConfigHelper(helper);

            string ok = null;
            cm.LoadConfigSuccess += (s, e) => ok = e.ConfigAssetName;

            cm.LoadConfig("Config/Title.txt", 0, null);

            Assert.AreEqual("Config/Title.txt", ok);
            Assert.AreEqual(1, cm.Count);
            Assert.AreEqual("hello", cm.GetString("Title"));
            Assert.AreEqual(true, cm.GetBool("Title"));
            Assert.AreEqual(42, cm.GetInt("Title"));
            Assert.AreEqual(3.14f, cm.GetFloat("Title"), 0.001f);
            Assert.AreEqual(1, helper.Releases, "ReleaseDataAsset should be called once after ReadData");
        }

        [Test]
        public void Config_LoadAsset_LoadFailure_FiresFailure()
        {
            var res = new MockResourceManager { SyncSucceed = false };
            var cm = new ConfigManager(res);
            cm.SetConfigHelper(new StringConfigHelper());

            string err = null;
            cm.LoadConfigFailure += (s, e) => err = e.ErrorMessage;

            cm.LoadConfig("Config/Missing.txt", 0, null);
            Assert.IsNotNull(err);
            StringAssert.Contains("fake load failed", err);
        }

        [Test]
        public void Config_SyncCompletion_FiresSuccessExactlyOnce()
        {
            // MockResourceManager 同步模式：LoadAssetWithHandle 在返回前即 FireSuccess，
            // Completed 在 += 时同步回调 OnLoadCompleted。验证 B5 顺序修复后仍恰好成功一次。
            var res = new MockResourceManager { AssetFactory = _ => "Title\thello\ttrue\t42\t3.14" };
            var cm = new ConfigManager(res);
            cm.SetConfigHelper(new StringConfigHelper());

            int successCount = 0;
            cm.LoadConfigSuccess += (s, e) => successCount++;

            cm.LoadConfig("Config/Title.txt", 0, null);

            Assert.AreEqual(1, successCount, "同步完成路径应恰好触发一次成功事件");
            Assert.AreEqual(1, cm.Count);
        }

        [Test]
        public void Config_TryGet_DistinguishesAbsentFromZeroOrEmpty()
        {
            var cm = new ConfigManager(new MockResourceManager());
            cm.SetConfigHelper(new StringConfigHelper());
            // 存在但值为 0 / false / 空串。
            Assert.IsTrue(cm.AddConfig("Zero", "", "false", "0", "0"));

            Assert.IsTrue(cm.TryGetBool("Zero", out bool b), "存在键应返回 true");
            Assert.IsFalse(b);
            Assert.IsTrue(cm.TryGetInt("Zero", out int i));
            Assert.AreEqual(0, i);
            Assert.IsTrue(cm.TryGetFloat("Zero", out float f));
            Assert.AreEqual(0f, f, 1e-6f);
            Assert.IsTrue(cm.TryGetString("Zero", out string str));
            Assert.AreEqual("", str);

            // 缺失键：返回 false 且 out 为默认值。
            Assert.IsFalse(cm.TryGetBool("Absent", out bool b2));
            Assert.IsFalse(b2);
            Assert.IsFalse(cm.TryGetInt("Absent", out int i2));
            Assert.AreEqual(0, i2);
            Assert.IsFalse(cm.TryGetFloat("Absent", out float f2));
            Assert.AreEqual(0f, f2, 1e-6f);
            Assert.IsFalse(cm.TryGetString("Absent", out string s2));
            Assert.IsNull(s2);
        }

        [Test]
        public void Config_Getters_NullKey_DoNotThrow()
        {
            var cm = new ConfigManager(new MockResourceManager());
            cm.SetConfigHelper(new StringConfigHelper());

            Assert.IsFalse(cm.GetBool(null));
            Assert.AreEqual(0, cm.GetInt(null));
            Assert.AreEqual(0f, cm.GetFloat(null), 1e-6f);
            Assert.IsNull(cm.GetString(null));
            Assert.IsFalse(cm.TryGetBool(null, out _));
            Assert.IsFalse(cm.TryGetInt(null, out _));
            Assert.IsFalse(cm.TryGetFloat(null, out _));
            Assert.IsFalse(cm.TryGetString(null, out _));
        }

        // ===== Localization =====

        private sealed class StringLocHelper : ILocalizationHelper
        {
            public bool ReadData(ILocalizationManager m, string a, object asset, object ud) => ParseData(m, (string)asset, ud);
            public bool ParseData(ILocalizationManager m, string text, object ud)
            {
                if (text == null) return false;
                foreach (var line in text.Split('\n'))
                {
                    var s = line.Trim(); if (s.Length == 0) continue;
                    var tab = s.IndexOf('\t'); if (tab <= 0) continue;
                    m.AddRawString(s.Substring(0, tab), s.Substring(tab + 1));
                }
                return true;
            }
            public void ReleaseDataAsset(object a) { }
        }

        [Test]
        public void Localization_LoadDictionary_PopulatesAndFiresSuccess()
        {
            var res = new MockResourceManager { AssetFactory = _ => "greet\thello\nbye\tgoodbye" };
            var lm = new LocalizationManager(res);
            lm.SetLocalizationHelper(new StringLocHelper());

            string ok = null;
            lm.LoadDictionarySuccess += (s, e) => ok = e.DictionaryAssetName;

            lm.LoadDictionary("Loc/zh.txt", 0, null);

            Assert.AreEqual("Loc/zh.txt", ok);
            Assert.AreEqual(2, lm.DictionaryCount);
            Assert.AreEqual("hello", lm.GetRawString("greet"));
            Assert.AreEqual("goodbye", lm.GetRawString("bye"));
        }

        [Test]
        public void Localization_LoadDictionary_HelperReadDataReturnsFalse_FiresFailure()
        {
            // AssetFactory 返回 null → helper.ReadData 拿到 null → 返回 false → Manager 触发失败事件
            var res = new MockResourceManager { AssetFactory = _ => null };
            var lm = new LocalizationManager(res);
            lm.SetLocalizationHelper(new StringLocHelper());

            string err = null;
            lm.LoadDictionaryFailure += (s, e) => err = e.ErrorMessage;
            lm.LoadDictionary("Loc/bad.txt", 0, null);
            Assert.IsNotNull(err);
        }

        // ===== DataTable =====

        private sealed class FakeRow : IDataRow
        {
            public int Id { get; private set; }
            public string Value { get; private set; }
            public bool ParseDataRow(string s, object ud)
            {
                var c = s.Split('\t');
                if (c.Length < 2) return false;
                if (!int.TryParse(c[0], out int id)) return false;
                Id = id; Value = c[1];
                return true;
            }
        }

        private sealed class StringTableHelper : IDataTableHelper
        {
            public bool ReadData<T>(IDataTable<T> table, string a, object asset, object ud) where T : class, IDataRow, new()
                => ParseData<T>(table, (string)asset, ud);
            public bool ParseData<T>(IDataTable<T> table, string text, object ud) where T : class, IDataRow, new()
            {
                if (text == null) return false;
                foreach (var line in text.Split('\n'))
                {
                    var s = line.TrimEnd();
                    if (s.Length == 0 || s[0] == '#') continue;
                    table.AddDataRow(line, ud);
                }
                return true;
            }
            public void ReleaseDataAsset(object a) { }
        }

        [Test]
        public void DataTable_LoadDataTable_PopulatesAndFiresSuccess()
        {
            var res = new MockResourceManager { AssetFactory = _ => "1\tone\n2\ttwo\n3\tthree" };
            var dm = new DataTableManager(res);
            dm.SetDataTableHelper(new StringTableHelper());

            string ok = null;
            dm.LoadDataTableSuccess += (s, e) => ok = e.DataTableAssetName;

            dm.LoadDataTable<FakeRow>("DT/Items.txt", 0, null);

            Assert.AreEqual("DT/Items.txt", ok);
            var table = dm.GetDataTable<FakeRow>();
            Assert.IsNotNull(table);
            Assert.AreEqual(3, table.Count);
            Assert.AreEqual("two", table.GetDataRow(2).Value);
        }

        [Test]
        public void DataTable_LoadDataTable_LoadFailure_FiresFailure()
        {
            var res = new MockResourceManager { SyncSucceed = false };
            var dm = new DataTableManager(res);
            dm.SetDataTableHelper(new StringTableHelper());

            string err = null;
            dm.LoadDataTableFailure += (s, e) => err = e.ErrorMessage;
            dm.LoadDataTable<FakeRow>("DT/Bad.txt", 0, null);
            Assert.IsNotNull(err);
        }

        [Test]
        public void DataTable_ParseDataTable_InMemory_Works()
        {
            var dm = new DataTableManager(new MockResourceManager());
            dm.SetDataTableHelper(new StringTableHelper());

            Assert.IsTrue(dm.ParseDataTable<FakeRow>("10\tten\n20\ttwenty", null));
            Assert.AreEqual(2, dm.GetDataTable<FakeRow>().Count);
        }
    }
}
