//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 标准 UI Group 枚举：framework 预定义 8 层，sortingOrder 步长 100 留给业务插入自定义 group。
    ///
    /// 业务可在 OpenUIForm 时传 group 名（字符串），值匹配本枚举的 ToString() 即落到对应 Canvas。
    /// 也支持业务自定义 group 名（不在此枚举里）；UIComponent 会按需创建 Canvas。
    /// </summary>
    public enum UIGroupKind
    {
        /// <summary>背景层：天空 / 远景 / 主菜单背景 (order=0)。</summary>
        Background = 0,
        /// <summary>场景叠加层：minimap / 浮空伤害 / 头顶名字 (order=100)。</summary>
        Scene = 100,
        /// <summary>HUD 层：血条 / 资源 / 状态栏 / 准星 (order=200)。</summary>
        HUD = 200,
        /// <summary>常规窗口层：背包 / 商店 / 任务面板 (order=300)。</summary>
        Window = 300,
        /// <summary>模态对话框层：确认 / 输入 / 二次确认 (order=400)。</summary>
        Modal = 400,
        /// <summary>提示层：Toast / 气泡 / 飘字 (order=500)。</summary>
        Tip = 500,
        /// <summary>系统层：网络断开 / 错误提示 / 强更弹窗 (order=600)。</summary>
        System = 600,
        /// <summary>置顶层：Loading 遮罩 / 全屏 transition (order=700)。</summary>
        Top = 700,
    }

    /// <summary>
    /// UIGroupKind ↔ name string 互转工具，统一约定避免硬编码。
    /// </summary>
    public static class UIGroupKindExtensions
    {
        public static string ToGroupName(this UIGroupKind kind) => kind.ToString();
        public static int ToSortingOrder(this UIGroupKind kind) => (int)kind;

        /// <summary>所有标准 group，按 sortingOrder 升序。</summary>
        public static readonly UIGroupKind[] AllStandardGroups =
        {
            UIGroupKind.Background,
            UIGroupKind.Scene,
            UIGroupKind.HUD,
            UIGroupKind.Window,
            UIGroupKind.Modal,
            UIGroupKind.Tip,
            UIGroupKind.System,
            UIGroupKind.Top,
        };
    }
}
