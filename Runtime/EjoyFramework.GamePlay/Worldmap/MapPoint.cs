//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Worldmap
{
    /// <summary>
    /// 世界地图上的一个兴趣点（不可变）。
    /// 表示城镇、地下城、POI 等可在地图上标记、发现并（可选）作为快速旅行节点的位置。
    /// 纯 C# 实现，不依赖 UnityEngine。
    /// </summary>
    public sealed class MapPoint
    {
        private readonly string m_Id;
        private readonly MapCoord m_Position;
        private readonly string m_Type;
        private readonly bool m_IsFastTravel;
        private readonly object m_Payload;

        /// <summary>
        /// 构造地图点。
        /// </summary>
        /// <param name="id">唯一标识，不可为空或空白。</param>
        /// <param name="position">世界单位坐标 (X,Y)；俯视地图中 Y 通常为世界 Z。</param>
        /// <param name="type">类型标签，例如 "town"、"dungeon"、"poi"。可为空。</param>
        /// <param name="isFastTravel">是否为快速旅行节点。</param>
        /// <param name="payload">业务层附带数据，框架不解释其内容。</param>
        /// <exception cref="ArgumentException">id 为空或空白时抛出。</exception>
        public MapPoint(string id, MapCoord position, string type = null, bool isFastTravel = false, object payload = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("MapPoint id 不能为空。", nameof(id));
            }

            m_Id = id;
            m_Position = position;
            m_Type = type;
            m_IsFastTravel = isFastTravel;
            m_Payload = payload;
        }

        /// <summary>
        /// 唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 世界单位坐标 (X,Y)。
        /// </summary>
        public MapCoord Position
        {
            get { return m_Position; }
        }

        /// <summary>
        /// 类型标签，例如 "town"、"dungeon"、"poi"。可为空。
        /// </summary>
        public string Type
        {
            get { return m_Type; }
        }

        /// <summary>
        /// 是否为快速旅行节点。
        /// </summary>
        public bool IsFastTravel
        {
            get { return m_IsFastTravel; }
        }

        /// <summary>
        /// 业务层附带数据，框架不解释其内容。
        /// </summary>
        public object Payload
        {
            get { return m_Payload; }
        }
    }
}
