//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Debugger
{
    /// <summary>
    /// 调试器管理器接口。
    /// </summary>
    public interface IDebuggerManager
    {
        /// <summary>
        /// 获取或设置调试器窗口是否激活。
        /// </summary>
        bool ActiveWindow { get; set; }

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        void RegisterDebuggerWindow(string path, IDebuggerWindow debuggerWindow);

        /// <summary>
        /// 解除注册调试器窗口。
        /// </summary>
        void UnregisterDebuggerWindow(string path);

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        IDebuggerWindow GetDebuggerWindow(string path);

        // ===== GM / 作弊命令台 =====

        /// <summary>
        /// 注册一个 GM 命令。handler 接收解析后的参数数组（不含命令名），返回输出字符串。
        /// 重名覆盖。命令名大小写不敏感。
        /// </summary>
        void RegisterCommand(string name, System.Func<string[], string> handler, string help = "");

        /// <summary>解除注册命令；不存在返回 false。</summary>
        bool UnregisterCommand(string name);

        /// <summary>
        /// 执行一行命令（首 token 为命令名，其余为参数，按空白分隔）。
        /// 返回 handler 输出；未知命令返回错误串；handler 抛异常被捕获并返回错误串（不向上抛）。
        /// 空/纯空白行返回空串。
        /// </summary>
        string ExecuteCommand(string commandLine);

        /// <summary>列出所有已注册命令名（排序）。</summary>
        System.Collections.Generic.IReadOnlyList<string> GetCommandNames();

        /// <summary>获取命令的帮助文本；不存在返回 null。</summary>
        string GetCommandHelp(string name);
    }

    /// <summary>
    /// 调试器窗口接口。
    /// </summary>
    public interface IDebuggerWindow
    {
        /// <summary>
        /// 初始化调试器窗口。
        /// </summary>
        void Initialize(params object[] args);

        /// <summary>
        /// 关闭并清理调试器窗口。
        /// </summary>
        void Shutdown();

        /// <summary>
        /// 进入调试器窗口。
        /// </summary>
        void OnEnter();

        /// <summary>
        /// 离开调试器窗口。
        /// </summary>
        void OnLeave();

        /// <summary>
        /// 调试器窗口轮询。
        /// </summary>
        void OnUpdate(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 调试器窗口绘制。
        /// </summary>
        void OnDraw();
    }
}
