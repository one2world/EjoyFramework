//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using EjoyFramework.Core;
using EjoyFramework.Core.Unity;
using EjoyFramework.Core.Unity.Editor;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// WS5-M1：Unity 值类型格式化器（与各类型自身 ToString 逐字对拍、零分配）与编辑期 StringHash 碰撞体检。
    /// </summary>
    public class UnityTextFormattersTests
    {
        [SetUp]
        public void SetUp()
        {
            UnityTextFormatters.Register();
        }

        [Test]
        public void Formatters_MatchUnityToString()
        {
            AssertSame(new Vector2(1.5f, -2.25f), null);
            AssertSame(new Vector3(1f, 2.345f, -3.5f), null);
            AssertSame(new Vector3(1f, 2.345f, -3.5f), "F1");
            AssertSame(new Vector4(1f, 2f, 3f, 4.5f), null);
            AssertSame(new Vector2Int(3, -4), null);
            AssertSame(new Vector3Int(1, 2, 3), "D2");
            AssertSame(Quaternion.Euler(10f, 20f, 30f), null);
            AssertSame(new Color(0.1f, 0.2f, 0.3f, 1f), null);
            AssertSame(new Color32(10, 20, 30, 255), null);
            AssertSame(new Rect(1f, 2f, 30.5f, 40.25f), null);
        }

        [Test]
        public void Formatters_AreRegisteredAndAllocationFree()
        {
            Assert.IsTrue(TextFormatter.IsAllocationFree<Vector3>());
            Assert.IsTrue(TextFormatter.IsAllocationFree<Color>());
            Vector3 position = new Vector3(1f, 2f, 3f);
            Quaternion rotation = Quaternion.identity;
            ZeroAlloc.Assert(() =>
            {
                using (var t = TempText.Rent(16))
                {
                    t.AppendFormat("pos {0:F1} rot {1}", position, rotation);
                }
            });
        }

        [Test]
        public void Validator_ExtractsLiteralKeysWithEscapes()
        {
            string source =
                "static readonly StringHash k_A = StringHash.Of(\"MaxHp\");\n" +
                "var b = StringHash . Of ( \"Tab\\tQuote\\\"Uni\\u4E2D\" );\n" +
                "var c = StringHash.Of(@\"C:\\path\"\"q\"\"\");\n" +
                "var d = StringHash.Of(\"MaxHp\");\n" +
                "var e = StringHash.Of(name);\n" +
                "var f = StringHash.Of(\"bad\\q\");\n";
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            StringHashValidator.ExtractLiterals(source, "File.cs", names);
            CollectionAssert.AreEquivalent(new[] { "MaxHp", "Tab\tQuote\"Uni\u4E2D", "C:\\path\"q\"" }, names.Keys);
            Assert.AreEqual("File.cs:1", names["MaxHp"], "the first occurrence is reported");
            Assert.AreEqual("File.cs:3", names["C:\\path\"q\""]);
        }

        [Test]
        public void Validator_FindsCollisionsInScannedSource()
        {
            string dir = Path.Combine(TestTempPaths.Root, "hashscan_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "A.cs"), "var a = StringHash.Of(\"costarring\");");
                File.WriteAllText(Path.Combine(dir, "B.cs"), "var b = StringHash.Of(\"liquid\"); var c = StringHash.Of(\"unique\");");
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                Assert.AreEqual(2, StringHashValidator.ScanDirectory(dir, names));
                Assert.AreEqual(0, StringHashValidator.ScanDirectory(Path.Combine(dir, "missing"), names));
                var collisions = new List<StringHashCollision>();
                Assert.AreEqual(1, StringHashRegistry.FindCollisions(names.Keys, collisions));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void FrameworkRuntimeSource_HasNoLiteralKeyCollisions()
        {
            string runtime = Path.Combine(
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(StringHash).Assembly).resolvedPath, "Runtime");
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            Assert.Greater(StringHashValidator.ScanDirectory(runtime, names), 0);
            var collisions = new List<StringHashCollision>();
            Assert.AreEqual(0, StringHashRegistry.FindCollisions(names.Keys, collisions),
                collisions.Count > 0 ? collisions[0].ToString() : null);
        }

        private static void AssertSame<T>(T value, string format) where T : IFormattable
        {
            string expected = value.ToString(format, CultureInfo.InvariantCulture);
            using (var t = TempText.Rent(4))
            {
                t.Append(value, format);
                Assert.AreEqual(expected, t.ToString(), typeof(T).Name + " " + format);
            }
        }
    }
}
