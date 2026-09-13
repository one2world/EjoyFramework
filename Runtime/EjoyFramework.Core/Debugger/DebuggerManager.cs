//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Debugger
{
    internal sealed class DebuggerManager : FrameworkModule, IDebuggerManager
    {
        private readonly Dictionary<string, IDebuggerWindow> m_Windows = new Dictionary<string, IDebuggerWindow>(StringComparer.Ordinal);
        // GM 命令表（命令名大小写不敏感）。
        private readonly Dictionary<string, CommandEntry> m_Commands = new Dictionary<string, CommandEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly char[] s_CmdSep = { ' ', '\t', '\r', '\n' };
        private bool m_Active;

        public DebuggerManager()
        {
            RegisterBuiltInCommands();
        }

        // Priority 0：调试模块，独立于业务依赖图。
        public override int Priority { get { return 0; } }
        public override void Update(float a, float b)
        {
            if (!m_Active) return;
            foreach (var kv in m_Windows) try { kv.Value.OnUpdate(a, b); } catch (Exception e) { FrameworkLog.Error("Debugger window '{0}' OnUpdate: {1}", kv.Key, e); }
        }
        public override void Shutdown()
        {
            foreach (var kv in m_Windows) try { kv.Value.Shutdown(); } catch (Exception e) { FrameworkLog.Error("Debugger window '{0}' Shutdown: {1}", kv.Key, e); }
            m_Windows.Clear();
        }

        public bool ActiveWindow { get { return m_Active; } set { m_Active = value; } }

        public void RegisterDebuggerWindow(string path, IDebuggerWindow w)
        {
            Framework.EnsureMainThread(nameof(RegisterDebuggerWindow));
            if (string.IsNullOrEmpty(path)) throw new FrameworkException("Path is invalid.");
            if (w == null) throw new FrameworkException("Debugger window is invalid.");
            if (m_Windows.ContainsKey(path)) throw new FrameworkException(Utility.Text.Format("Debugger window '{0}' already exists.", path));
            m_Windows.Add(path, w);
        }

        public void UnregisterDebuggerWindow(string path)
        {
            Framework.EnsureMainThread(nameof(UnregisterDebuggerWindow));
            IDebuggerWindow w;
            if (m_Windows.TryGetValue(path, out w))
            {
                try { w.Shutdown(); } catch (Exception e) { FrameworkLog.Error("Debugger window '{0}' Shutdown: {1}", path, e); }
                m_Windows.Remove(path);
            }
        }

        public IDebuggerWindow GetDebuggerWindow(string path)
        {
            IDebuggerWindow w;
            return path != null && m_Windows.TryGetValue(path, out w) ? w : null;
        }

        // ===== GM / 作弊命令台 =====

        public void RegisterCommand(string name, Func<string[], string> handler, string help = "")
        {
            Framework.EnsureMainThread(nameof(RegisterCommand));
            if (string.IsNullOrEmpty(name)) throw new FrameworkException("Command name is invalid.");
            if (handler == null) throw new FrameworkException("Command handler is invalid.");
            m_Commands[name] = new CommandEntry { Name = name, Handler = handler, Help = help ?? string.Empty };
        }

        public bool UnregisterCommand(string name)
        {
            Framework.EnsureMainThread(nameof(UnregisterCommand));
            return name != null && m_Commands.Remove(name);
        }

        public string ExecuteCommand(string commandLine)
        {
            Framework.EnsureMainThread(nameof(ExecuteCommand));
            if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;

            string[] tokens = commandLine.Split(s_CmdSep, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return string.Empty;

            string name = tokens[0];
            string[] args = tokens.Length > 1 ? new string[tokens.Length - 1] : Array.Empty<string>();
            for (int i = 1; i < tokens.Length; i++) args[i - 1] = tokens[i];

            if (!m_Commands.TryGetValue(name, out CommandEntry entry))
                return Utility.Text.Format("Unknown command: '{0}'. Type 'help' for the list.", name);

            try
            {
                return entry.Handler(args) ?? string.Empty;
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("GM command '{0}' threw: {1}", name, ex);
                return Utility.Text.Format("Command '{0}' error: {1}", name, ex.Message);
            }
        }

        public IReadOnlyList<string> GetCommandNames()
        {
            var names = new List<string>(m_Commands.Count);
            foreach (var kv in m_Commands) names.Add(kv.Value.Name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public string GetCommandHelp(string name)
        {
            return name != null && m_Commands.TryGetValue(name, out CommandEntry e) ? e.Help : null;
        }

        private void RegisterBuiltInCommands()
        {
            m_Commands["help"] = new CommandEntry
            {
                Name = "help",
                Help = "help [command] — 列出所有命令，或显示某命令的帮助。",
                Handler = args =>
                {
                    if (args.Length > 0)
                    {
                        string h = GetCommandHelp(args[0]);
                        return h != null ? args[0] + ": " + h : Utility.Text.Format("No such command: '{0}'.", args[0]);
                    }
                    var names = GetCommandNames();
                    var sb = new System.Text.StringBuilder("Commands (" + names.Count + "): ");
                    for (int i = 0; i < names.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(names[i]); }
                    return sb.ToString();
                },
            };
            m_Commands["echo"] = new CommandEntry
            {
                Name = "echo",
                Help = "echo <text...> — 原样返回参数。",
                Handler = args => string.Join(" ", args),
            };
        }

        private sealed class CommandEntry
        {
            public string Name;
            public Func<string[], string> Handler;
            public string Help;
        }
    }
}
