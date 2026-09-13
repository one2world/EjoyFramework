//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.Debugger;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// GM 命令台（DebuggerManager 命令注册/执行）测试。
    /// </summary>
    public class GmConsoleTests
    {
        private DebuggerManager NewManager()
        {
            Framework.MarkMainThread();
            return new DebuggerManager();
        }

        [Test]
        public void Execute_RegisteredCommand_ReturnsHandlerOutput()
        {
            var dm = NewManager();
            dm.RegisterCommand("add", args => (int.Parse(args[0]) + int.Parse(args[1])).ToString());
            Assert.AreEqual("7", dm.ExecuteCommand("add 3 4"));
        }

        [Test]
        public void Execute_MultiArg_ParsedAndNameExcluded()
        {
            var dm = NewManager();
            string[] captured = null;
            dm.RegisterCommand("cap", args => { captured = args; return ""; });
            dm.ExecuteCommand("cap  a   b\tc");   // 多空白折叠
            Assert.AreEqual(new[] { "a", "b", "c" }, captured);
        }

        [Test]
        public void Execute_NoArgs_PassesEmptyArray()
        {
            var dm = NewManager();
            int len = -1;
            dm.RegisterCommand("noargs", args => { len = args.Length; return ""; });
            dm.ExecuteCommand("noargs");
            Assert.AreEqual(0, len);
        }

        [Test]
        public void Execute_UnknownCommand_ReturnsError_NoThrow()
        {
            var dm = NewManager();
            string r = dm.ExecuteCommand("nope x");
            StringAssert.Contains("Unknown command", r);
        }

        [Test]
        public void Execute_HandlerThrows_ReturnsError_DoesNotPropagate()
        {
            var dm = NewManager();
            dm.RegisterCommand("boom", args => throw new System.InvalidOperationException("kaboom"));
            string r = dm.ExecuteCommand("boom");
            StringAssert.Contains("kaboom", r);
        }

        [Test]
        public void Execute_EmptyOrWhitespace_ReturnsEmpty()
        {
            var dm = NewManager();
            Assert.AreEqual("", dm.ExecuteCommand(""));
            Assert.AreEqual("", dm.ExecuteCommand("   "));
            Assert.AreEqual("", dm.ExecuteCommand(null));
        }

        [Test]
        public void Command_CaseInsensitive()
        {
            var dm = NewManager();
            dm.RegisterCommand("Foo", args => "ok");
            Assert.AreEqual("ok", dm.ExecuteCommand("foo"));
            Assert.AreEqual("ok", dm.ExecuteCommand("FOO"));
        }

        [Test]
        public void BuiltIns_HelpAndEcho()
        {
            var dm = NewManager();
            StringAssert.Contains("hello world", dm.ExecuteCommand("echo hello world"));
            StringAssert.Contains("help", dm.ExecuteCommand("help"));   // help lists itself
            StringAssert.Contains("echo", dm.ExecuteCommand("help echo"));
        }

        [Test]
        public void Unregister_RemovesCommand()
        {
            var dm = NewManager();
            dm.RegisterCommand("temp", args => "x");
            Assert.IsTrue(dm.UnregisterCommand("temp"));
            Assert.IsFalse(dm.UnregisterCommand("temp"));
            StringAssert.Contains("Unknown command", dm.ExecuteCommand("temp"));
        }

        [Test]
        public void Register_DuplicateName_Overwrites()
        {
            var dm = NewManager();
            dm.RegisterCommand("c", args => "first");
            dm.RegisterCommand("c", args => "second");
            Assert.AreEqual("second", dm.ExecuteCommand("c"));
        }

        [Test]
        public void GetCommandNames_IncludesBuiltInsAndCustom_Sorted()
        {
            var dm = NewManager();
            dm.RegisterCommand("zzz", args => "");
            var names = dm.GetCommandNames();
            CollectionAssert.Contains((System.Collections.IEnumerable)names, "echo");
            CollectionAssert.Contains((System.Collections.IEnumerable)names, "help");
            CollectionAssert.Contains((System.Collections.IEnumerable)names, "zzz");
        }
    }
}
