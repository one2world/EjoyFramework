//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.Localization;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class LocalizationExtensionsTests
    {
        private LocalizationManager m_LM;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_LM = new LocalizationManager();
            m_LM.Language = Language.English;
        }

        // ===== PluralRules =====

        [Test]
        public void Plural_English_OneVsOther()
        {
            Assert.AreEqual(PluralForm.One, PluralRules.Get(Language.English, 1));
            Assert.AreEqual(PluralForm.Other, PluralRules.Get(Language.English, 0));
            Assert.AreEqual(PluralForm.Other, PluralRules.Get(Language.English, 5));
        }

        [Test]
        public void Plural_Chinese_AlwaysOther()
        {
            Assert.AreEqual(PluralForm.Other, PluralRules.Get(Language.ChineseSimplified, 1));
            Assert.AreEqual(PluralForm.Other, PluralRules.Get(Language.ChineseSimplified, 100));
        }

        [Test]
        public void Plural_Russian_One()
        {
            Assert.AreEqual(PluralForm.One, PluralRules.Get(Language.Russian, 1));
            Assert.AreEqual(PluralForm.One, PluralRules.Get(Language.Russian, 21));
        }

        [Test]
        public void Plural_Russian_Few()
        {
            Assert.AreEqual(PluralForm.Few, PluralRules.Get(Language.Russian, 2));
            Assert.AreEqual(PluralForm.Few, PluralRules.Get(Language.Russian, 23));
        }

        [Test]
        public void Plural_Russian_Many()
        {
            Assert.AreEqual(PluralForm.Many, PluralRules.Get(Language.Russian, 5));
            Assert.AreEqual(PluralForm.Many, PluralRules.Get(Language.Russian, 11));
            Assert.AreEqual(PluralForm.Many, PluralRules.Get(Language.Russian, 111));
            Assert.AreEqual(PluralForm.Many, PluralRules.Get(Language.Russian, 0));
            Assert.AreEqual(PluralForm.Many, PluralRules.Get(Language.Russian, 100));
        }

        [Test]
        public void Plural_MakeKey_AppendsSuffix()
        {
            Assert.AreEqual("apple.one", PluralRules.MakeKey("apple", PluralForm.One));
            Assert.AreEqual("apple.other", PluralRules.MakeKey("apple", PluralForm.Other));
        }

        // ===== Format =====

        [Test]
        public void Format_PositionalPlaceholder()
        {
            m_LM.AddRawString("greet", "Hello, {0}!");
            Assert.AreEqual("Hello, Alice!", m_LM.Format("greet", "Alice"));
        }

        [Test]
        public void Format_MissingKey_ReturnsKey()
        {
            Assert.AreEqual("missing", m_LM.Format("missing", "Alice"));
        }

        [Test]
        public void FormatNamed_ReplacesNamedPlaceholders()
        {
            m_LM.AddRawString("welcome", "Hi {name}, hp={hp}.");
            string result = m_LM.FormatNamed("welcome", ("name", "Alice"), ("hp", 100));
            Assert.AreEqual("Hi Alice, hp=100.", result);
        }

        // ===== GetPlural =====

        [Test]
        public void GetPlural_English_SelectsRightForm()
        {
            m_LM.AddRawString("apple.one", "{0} apple");
            m_LM.AddRawString("apple.other", "{0} apples");
            Assert.AreEqual("1 apple", m_LM.GetPlural("apple", 1));
            Assert.AreEqual("5 apples", m_LM.GetPlural("apple", 5));
            Assert.AreEqual("0 apples", m_LM.GetPlural("apple", 0));
        }

        [Test]
        public void GetPlural_Chinese_AlwaysOtherForm()
        {
            m_LM.Language = Language.ChineseSimplified;
            m_LM.AddRawString("apple.other", "{0} 个苹果");
            Assert.AreEqual("1 个苹果", m_LM.GetPlural("apple", 1));
            Assert.AreEqual("99 个苹果", m_LM.GetPlural("apple", 99));
        }

        [Test]
        public void GetPlural_FallsBackToBaseKey_WhenNoFormFound()
        {
            m_LM.AddRawString("orphan", "raw value");
            Assert.AreEqual("raw value", m_LM.GetPlural("orphan", 3));
        }

        [Test]
        public void HasLocalized_DetectsBothRawAndPluralOther()
        {
            m_LM.AddRawString("hello", "Hi");
            m_LM.AddRawString("apple.other", "{0} apples");
            Assert.IsTrue(m_LM.HasLocalized("hello"));
            Assert.IsTrue(m_LM.HasLocalized("apple"));
            Assert.IsFalse(m_LM.HasLocalized("missing"));
        }

        // ===== WS5-M1：切片查表与零分配追加 =====

        [Test]
        public void TryGetRawString_BySpan()
        {
            m_LM.AddRawString("apple.one", "{0} apple");
            string value;
            Assert.IsTrue(m_LM.TryGetRawString("xapple.onex".AsSpan(1, 9), out value));
            Assert.AreEqual("{0} apple", value);
            Assert.IsFalse(m_LM.TryGetRawString("apple.two".AsSpan(), out value));
            Assert.IsNull(value);
        }

        [Test]
        public void AppendFormat_WritesFormattedTemplate()
        {
            m_LM.AddRawString("hp", "HP {0}/{1}");
            m_LM.AddRawString("title", "Stage");
            using (var t = TempText.Rent())
            {
                Assert.IsTrue(m_LM.AppendFormat(t, "hp", 30, 100));
                t.Append(' ');
                Assert.IsTrue(m_LM.AppendString(t, "title"));
                Assert.AreEqual("HP 30/100 Stage", t.ToString());
            }
        }

        [Test]
        public void AppendFormat_MalformedTemplate_AppendsRawTemplate()
        {
            m_LM.AddRawString("broken", "HP {0");
            using (var t = TempText.Rent())
            {
                Assert.IsFalse(m_LM.AppendFormat(t, "broken", 30));
                Assert.AreEqual("HP {0", t.ToString(), "a translator's typo must not throw or leave half-formatted text");
            }
        }

        [Test]
        public void AppendFormat_MissingKey_AppendsFallback()
        {
            using (var t = TempText.Rent())
            {
                Assert.IsFalse(m_LM.AppendFormat(t, "nope", 1));
                Assert.AreEqual(m_LM.GetString("nope"), t.ToString());
            }
        }

        [Test]
        public void AppendPlural_SelectsFormAndFallsBack()
        {
            m_LM.AddRawString("apple.one", "{0} apple");
            m_LM.AddRawString("apple.other", "{0} apples");
            m_LM.AddRawString("coin", "{0} coin(s)");
            using (var t = TempText.Rent())
            {
                Assert.IsTrue(m_LM.AppendPlural(t, "apple", 1));
                t.Append('|');
                Assert.IsTrue(m_LM.AppendPlural(t, "apple", 5));
                t.Append('|');
                Assert.IsTrue(m_LM.AppendPlural(t, "coin", 3), "falls back to the base key");
                t.Append('|');
                Assert.IsFalse(m_LM.AppendPlural(t, "ghost", 2), "all candidates missing");
                Assert.AreEqual("1 apple|5 apples|3 coin(s)|ghost", t.ToString());
            }

            m_LM.Language = Language.Russian;
            using (var t = TempText.Rent())
            {
                Assert.IsTrue(m_LM.AppendPlural(t, "apple", 3), "Russian 'few' is missing and falls back to .other");
                Assert.AreEqual("3 apples", t.ToString());
            }
        }

        [Test]
        public void Plural_LongBaseKey_UsesPooledKeyBuffer()
        {
            string baseKey = new string('k', 300);
            m_LM.AddRawString(baseKey + ".other", "{0} long");
            Assert.AreEqual("4 long", m_LM.GetPlural(baseKey, 4));
            using (var t = TempText.Rent())
            {
                Assert.IsTrue(m_LM.AppendPlural(t, baseKey, 4));
                Assert.AreEqual("4 long", t.ToString());
            }
        }

        [Test]
        public void Append_IsAllocationFree()
        {
            m_LM.AddRawString("hp", "HP {0}/{1}");
            m_LM.AddRawString("apple.one", "{0} apple");
            m_LM.AddRawString("apple.other", "{0} apples");
            int count = 0;
            ZeroAlloc.Assert(() =>
            {
                using (var t = TempText.Rent(64))
                {
                    m_LM.AppendFormat(t, "hp", count, 100);
                    m_LM.AppendPlural(t, "apple", count & 3);
                    count++;
                }
            });
        }
    }
}
