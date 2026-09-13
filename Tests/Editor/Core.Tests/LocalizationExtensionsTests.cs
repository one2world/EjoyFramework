//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

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
    }
}
