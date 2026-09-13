//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Worldmap
{
    /// <summary>
    /// 世界地图数据模型接口：管理地图点集合、发现状态与快速旅行规则。
    /// 作为框架模块经 <see cref="Framework.GetModule{T}"/> 暴露；纯数据查询，不持有渲染状态，可直接用于存档/同步。
    /// </summary>
    public interface IMapModel
    {
        /// <summary>
        /// 某个地图点被首次发现时触发。参数为 (模型, 被发现点的 Id)。
        /// 重复发现不会再次触发。
        /// </summary>
        event Action<IMapModel, string> OnDiscovered;

        /// <summary>
        /// 所有已注册的地图点（无序）。
        /// </summary>
        IEnumerable<MapPoint> Points { get; }

        /// <summary>
        /// 所有已发现的地图点（无序）。
        /// </summary>
        IEnumerable<MapPoint> DiscoveredPoints { get; }

        /// <summary>
        /// 添加一个地图点。
        /// </summary>
        /// <param name="point">待添加的点，不可为空。</param>
        /// <exception cref="ArgumentNullException">point 为空时抛出。</exception>
        /// <exception cref="ArgumentException">Id 已存在时抛出。</exception>
        void AddPoint(MapPoint point);

        /// <summary>
        /// 按 Id 获取地图点。
        /// </summary>
        /// <param name="id">点 Id。</param>
        /// <returns>对应的点；不存在时返回 null。</returns>
        MapPoint GetPoint(string id);

        /// <summary>
        /// 标记某个点为已发现；若为首次发现则触发 <see cref="OnDiscovered"/>（幂等）。
        /// 不存在的 Id 将被忽略（不标记、不触发）。
        /// </summary>
        /// <param name="id">点 Id。</param>
        void Discover(string id);

        /// <summary>
        /// 查询某个点是否已被发现。
        /// </summary>
        /// <param name="id">点 Id。</param>
        /// <returns>已发现返回 true。</returns>
        bool IsDiscovered(string id);

        /// <summary>
        /// 判断能否从 from 快速旅行到 to。
        /// 条件：两点均存在、均为快速旅行节点、均已发现，且 from != to。
        /// </summary>
        /// <param name="fromId">出发点 Id。</param>
        /// <param name="toId">目标点 Id。</param>
        /// <returns>满足全部条件返回 true。</returns>
        bool CanFastTravel(string fromId, string toId);

        /// <summary>
        /// 获取从 from 出发可达的快速旅行目的地：
        /// 即所有「已发现的快速旅行节点」中除 from 之外、且 from 自身满足出发条件者。
        /// 若 from 不存在/不可作为出发点，返回空列表。
        /// </summary>
        /// <param name="fromId">出发点 Id。</param>
        /// <returns>可旅行至的目的地列表（只读，永不为 null）。</returns>
        IReadOnlyList<MapPoint> GetFastTravelDestinations(string fromId);

        /// <summary>
        /// 查找距离 from 最近的点（基于平方距离，可选过滤）。
        /// </summary>
        /// <param name="from">参考坐标。</param>
        /// <param name="filter">可选谓词；返回 false 的点被排除。为空表示不过滤。</param>
        /// <returns>最近的点；无可用点时返回 null。</returns>
        MapPoint FindNearest(MapCoord from, Func<MapPoint, bool> filter = null);
    }
}
