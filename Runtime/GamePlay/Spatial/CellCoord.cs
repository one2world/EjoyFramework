//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Spatial
{
    /// <summary>
    /// 空间哈希网格中的整数单元坐标（cell 索引），作为 <see cref="AoiGrid"/> 桶字典的键。
    ///
    /// 设计要点：
    /// - 单元索引由世界坐标按 floor(coord / cellSize) 计算得到，<b>对负坐标使用向下取整（floor）而非截断（truncate）</b>，
    ///   以保证 [-cellSize, 0) 区间映射到 -1 而非 0，单元划分在原点两侧严格对称、连续无重叠。
    /// - 作为 <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> 的键，实现 <see cref="IEquatable{T}"/>
    ///   与稳定的 <see cref="GetHashCode"/>，避免装箱并降低哈希冲突。
    ///
    /// 纯逻辑、与引擎无关（仅依赖 System.*）。
    /// </summary>
    internal readonly struct CellCoord : IEquatable<CellCoord>
    {
        /// <summary>
        /// 单元在 X 轴上的整数索引。
        /// </summary>
        public readonly int X;

        /// <summary>
        /// 单元在 Y 轴上的整数索引。
        /// </summary>
        public readonly int Y;

        /// <summary>
        /// 由整数索引直接构造单元坐标。
        /// </summary>
        public CellCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// 由世界坐标 (x, y) 与单元尺寸计算所属单元坐标，对负坐标使用向下取整（floor）。
        /// </summary>
        /// <param name="x">世界 X 坐标。</param>
        /// <param name="y">世界 Y 坐标。</param>
        /// <param name="cellSize">单元尺寸（世界单位），调用方需保证为正。</param>
        public static CellCoord FromWorld(float x, float y, float cellSize)
        {
            return new CellCoord(FloorDiv(x, cellSize), FloorDiv(y, cellSize));
        }

        /// <summary>
        /// 计算 floor(value / cellSize)，对负值正确向下取整。
        /// </summary>
        private static int FloorDiv(float value, float cellSize)
        {
            // Math.Floor 对负数返回更小的整数（如 -0.1 => -1），符合空间网格的对称划分要求；
            // 直接 (int) 强转会向 0 截断（-0.1 => 0），导致原点两侧单元错位，故此处必须使用 Floor。
            return (int)Math.Floor(value / cellSize);
        }

        /// <summary>
        /// 判断两个单元坐标是否相等。
        /// </summary>
        public bool Equals(CellCoord other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <summary>
        /// 判断与任意对象是否相等。
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is CellCoord other && Equals(other);
        }

        /// <summary>
        /// 计算哈希值（混合 X、Y），用于字典键分布。
        /// </summary>
        public override int GetHashCode()
        {
            // 经典无符号整数混合：减少 (X, Y) 组合落入同一桶的概率。
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X;
                hash = hash * 31 + Y;
                return hash;
            }
        }

        /// <summary>
        /// 返回便于调试的字符串表示。
        /// </summary>
        public override string ToString()
        {
            return "(" + X + ", " + Y + ")";
        }
    }
}
