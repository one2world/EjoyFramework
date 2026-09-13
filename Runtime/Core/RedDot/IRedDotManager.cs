//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.RedDot
{
    /// <summary>
    /// 红点（消息角标）管理器接口。
    ///
    /// 模型：以 '/' 分隔的路径树。每个节点要么是叶子（持有自身计数 ownLeafCount），
    /// 要么是内部节点（有效计数 = 所有后代叶子计数之和）。
    ///   - 任意节点可同时既被 SetLeafCount（成为叶子来源），也拥有子节点；
    ///     此时其有效计数 = ownLeafCount + 所有子节点有效计数之和。
    ///   - 引用任意路径时自动创建其祖先节点。
    ///
    /// 示例：
    ///   SetLeafCount("Mail/System", 2);
    ///   SetLeafCount("Mail/Friend", 1);
    ///   GetCount("Mail")   => 3   // 内部节点 = 后代叶子之和
    ///   IsActive("Mail")   => true
    /// </summary>
    public interface IRedDotManager
    {
        /// <summary>
        /// 设置某个叶子路径的自身计数，并向上传播刷新所有祖先的聚合计数。
        /// 计数变化的节点会触发其订阅者回调。
        /// </summary>
        /// <param name="path">'/' 分隔的路径（首尾 '/' 会被规整）。</param>
        /// <param name="count">该叶子的自身计数（&lt;0 会被钳为 0）。</param>
        void SetLeafCount(string path, int count);

        /// <summary>
        /// 获取节点的有效计数。
        /// 叶子返回其自身计数；内部节点返回所有后代叶子计数之和（含自身 ownLeafCount）。
        /// 不存在的路径返回 0。
        /// </summary>
        int GetCount(string path);

        /// <summary>
        /// 节点是否处于激活态（有效计数 &gt; 0）。
        /// </summary>
        bool IsActive(string path);

        /// <summary>
        /// 订阅某节点有效计数变化。回调参数为变化后的新有效计数。
        /// 订阅时不会立即回调，由订阅方自行读取初始状态（GetCount）。
        /// </summary>
        void Subscribe(string path, Action<int> onChanged);

        /// <summary>
        /// 取消订阅。未订阅时调用安全无副作用。
        /// </summary>
        void Unsubscribe(string path, Action<int> onChanged);

        /// <summary>
        /// 清空整棵树（计数与订阅者）。
        /// </summary>
        void Clear();
    }
}
