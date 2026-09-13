//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.GamePlay.Worldmap
{
    /// <summary>
    /// 小地图坐标投影组件：把世界坐标投影到一个小地图 <see cref="RectTransform"/> 上。
    /// 取世界坐标的 (x, z) 平面，经核心 <see cref="MapMath.WorldToNormalized"/> 归一化后，
    /// 按目标 RectTransform 的尺寸缩放为 anchoredPosition 空间坐标。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinimapProjector : MonoBehaviour
    {
        [Tooltip("世界范围最小角 (worldX, worldZ)。")]
        [SerializeField] private Vector2 m_WorldMin = new Vector2(-100f, -100f);

        [Tooltip("世界范围最大角 (worldX, worldZ)。")]
        [SerializeField] private Vector2 m_WorldMax = new Vector2(100f, 100f);

        [Tooltip("目标小地图 RectTransform，投影结果落在其 rect 尺寸内。")]
        [SerializeField] private RectTransform m_MinimapRect;

        /// <summary>
        /// 将世界坐标投影为小地图上的 anchoredPosition 坐标。
        /// 以 rect 中心为原点：归一化 [0,1] 经偏移 -0.5 后乘以 rect 宽高。
        /// </summary>
        /// <param name="worldPos">世界坐标（取 x、z 分量）。</param>
        /// <returns>小地图局部坐标；未配置 RectTransform 时返回 Vector2.zero。</returns>
        public Vector2 WorldToMinimap(Vector3 worldPos)
        {
            if (m_MinimapRect == null)
            {
                return Vector2.zero;
            }

            MapCoord world = new MapCoord(worldPos.x, worldPos.z);
            MapCoord min = new MapCoord(m_WorldMin.x, m_WorldMin.y);
            MapCoord max = new MapCoord(m_WorldMax.x, m_WorldMax.y);
            MapCoord n = MapMath.WorldToNormalized(world, min, max);

            Rect rect = m_MinimapRect.rect;
            float x = (n.X - 0.5f) * rect.width;
            float y = (n.Y - 0.5f) * rect.height;
            return new Vector2(x, y);
        }

        /// <summary>
        /// <see cref="Vector3"/> → <see cref="MapCoord"/>（取 x、z 平面）。
        /// </summary>
        /// <param name="worldPos">世界坐标。</param>
        /// <returns>地图坐标。</returns>
        public static MapCoord ToMapCoord(Vector3 worldPos)
        {
            return new MapCoord(worldPos.x, worldPos.z);
        }

        /// <summary>
        /// <see cref="MapCoord"/> → <see cref="Vector3"/>（映射到 x、z 平面，y 取 0）。
        /// </summary>
        /// <param name="coord">地图坐标。</param>
        /// <returns>世界坐标，y 分量为 0。</returns>
        public static Vector3 ToWorldPosition(MapCoord coord)
        {
            return new Vector3(coord.X, 0f, coord.Y);
        }
    }
}
