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
    /// 世界地图数据模型：管理地图点集合、发现状态与快速旅行规则。
    /// 纯 C# 逻辑，不依赖 UnityEngine；不持有任何渲染状态，可直接用于存档/同步。
    /// 作为框架模块经 <see cref="Framework.GetModule{T}"/> 暴露；无构造依赖，无每帧逻辑。
    /// </summary>
    public sealed class MapModel : FrameworkModule, IMapModel
    {
        private readonly Dictionary<string, MapPoint> m_Points = new Dictionary<string, MapPoint>();
        private readonly HashSet<string> m_Discovered = new HashSet<string>();

        /// <summary>
        /// 某个地图点被首次发现时触发。参数为 (模型, 被发现点的 Id)。
        /// 重复发现不会再次触发。
        /// </summary>
        public event Action<IMapModel, string> OnDiscovered;

        /// <summary>
        /// 获取游戏框架模块优先级。世界地图为纯数据查询，使用默认优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 添加一个地图点。
        /// </summary>
        /// <param name="point">待添加的点，不可为空。</param>
        /// <exception cref="ArgumentNullException">point 为空时抛出。</exception>
        /// <exception cref="ArgumentException">Id 已存在时抛出。</exception>
        public void AddPoint(MapPoint point)
        {
            if (point == null)
            {
                throw new ArgumentNullException(nameof(point));
            }
            if (m_Points.ContainsKey(point.Id))
            {
                throw new ArgumentException($"地图点 Id 已存在：{point.Id}", nameof(point));
            }
            m_Points.Add(point.Id, point);
        }

        /// <summary>
        /// 按 Id 获取地图点。
        /// </summary>
        /// <param name="id">点 Id。</param>
        /// <returns>对应的点；不存在时返回 null。</returns>
        public MapPoint GetPoint(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            MapPoint point;
            return m_Points.TryGetValue(id, out point) ? point : null;
        }

        /// <summary>
        /// 所有已注册的地图点（无序）。
        /// </summary>
        public IEnumerable<MapPoint> Points
        {
            get { return m_Points.Values; }
        }

        /// <summary>
        /// 标记某个点为已发现；若为首次发现则触发 <see cref="OnDiscovered"/>（幂等）。
        /// 不存在的 Id 将被忽略（不标记、不触发）。
        /// </summary>
        /// <param name="id">点 Id。</param>
        public void Discover(string id)
        {
            // 仅对已注册的点生效，避免发现一个不存在的点。
            if (string.IsNullOrEmpty(id) || !m_Points.ContainsKey(id))
            {
                return;
            }
            // HashSet.Add 在已存在时返回 false，天然保证只在首次触发事件。
            if (m_Discovered.Add(id))
            {
                Action<IMapModel, string> handler = OnDiscovered;
                if (handler != null)
                {
                    handler(this, id);
                }
            }
        }

        /// <summary>
        /// 查询某个点是否已被发现。
        /// </summary>
        /// <param name="id">点 Id。</param>
        /// <returns>已发现返回 true。</returns>
        public bool IsDiscovered(string id)
        {
            return !string.IsNullOrEmpty(id) && m_Discovered.Contains(id);
        }

        /// <summary>
        /// 所有已发现的地图点（无序）。
        /// </summary>
        public IEnumerable<MapPoint> DiscoveredPoints
        {
            get
            {
                // 快照已发现的 Id，避免在迭代字典/集合期间外部修改导致的枚举异常（Mono/IL2CPP 风险）。
                foreach (string id in m_Discovered)
                {
                    MapPoint point;
                    if (m_Points.TryGetValue(id, out point))
                    {
                        yield return point;
                    }
                }
            }
        }

        /// <summary>
        /// 判断能否从 from 快速旅行到 to。
        /// 条件：两点均存在、均为快速旅行节点、均已发现，且 from != to。
        /// </summary>
        /// <param name="fromId">出发点 Id。</param>
        /// <param name="toId">目标点 Id。</param>
        /// <returns>满足全部条件返回 true。</returns>
        public bool CanFastTravel(string fromId, string toId)
        {
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId) || fromId == toId)
            {
                return false;
            }

            MapPoint from = GetPoint(fromId);
            MapPoint to = GetPoint(toId);
            if (from == null || to == null)
            {
                return false;
            }

            return from.IsFastTravel && to.IsFastTravel
                && IsDiscovered(fromId) && IsDiscovered(toId);
        }

        /// <summary>
        /// 获取从 from 出发可达的快速旅行目的地：
        /// 即所有「已发现的快速旅行节点」中除 from 之外、且 from 自身满足出发条件者。
        /// 若 from 不存在/不可作为出发点，返回空列表。
        /// </summary>
        /// <param name="fromId">出发点 Id。</param>
        /// <returns>可旅行至的目的地列表（只读，永不为 null）。</returns>
        public IReadOnlyList<MapPoint> GetFastTravelDestinations(string fromId)
        {
            var result = new List<MapPoint>();

            MapPoint from = GetPoint(fromId);
            if (from == null || !from.IsFastTravel || !IsDiscovered(fromId))
            {
                return result;
            }

            // 快照已发现 Id，避免迭代期间外部修改集合。
            foreach (string id in m_Discovered)
            {
                if (id == fromId)
                {
                    continue;
                }
                MapPoint candidate;
                if (m_Points.TryGetValue(id, out candidate) && candidate.IsFastTravel)
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        /// <summary>
        /// 查找距离 from 最近的点（基于平方距离，可选过滤）。
        /// </summary>
        /// <param name="from">参考坐标。</param>
        /// <param name="filter">可选谓词；返回 false 的点被排除。为空表示不过滤。</param>
        /// <returns>最近的点；无可用点时返回 null。</returns>
        public MapPoint FindNearest(MapCoord from, Func<MapPoint, bool> filter = null)
        {
            MapPoint nearest = null;
            float bestSqr = float.PositiveInfinity;

            foreach (MapPoint point in m_Points.Values)
            {
                if (filter != null && !filter(point))
                {
                    continue;
                }
                float sqr = MapMath.SqrDistance(from, point.Position);
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    nearest = point;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 游戏框架模块轮询。世界地图无每帧逻辑，空实现。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理游戏框架模块，移除全部地图点、发现状态与发现事件订阅。
        /// </summary>
        public override void Shutdown()
        {
            m_Points.Clear();
            m_Discovered.Clear();
            OnDiscovered = null;
        }
    }
}
