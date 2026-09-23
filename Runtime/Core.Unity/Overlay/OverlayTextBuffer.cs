//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 覆盖层文字缓冲：固定行数 × 列数的字符格，每行一个颜色。写入只拷贝字符（零分配），
    /// 内容没变不递增 <see cref="Version"/>，显示端据此决定是否重建网格。
    /// </summary>
    public sealed class OverlayTextBuffer
    {
        private readonly char[][] m_Lines;
        private readonly int[] m_Lengths;
        private readonly Color32[] m_Colors;
        private readonly int m_Columns;
        private int m_Version;

        public OverlayTextBuffer(int rows, int columns)
        {
            if (rows < 1 || columns < 1) throw new FrameworkException("OverlayTextBuffer：行数与列数须为正。");
            m_Columns = columns;
            m_Lines = new char[rows][];
            for (int i = 0; i < rows; i++) m_Lines[i] = new char[columns];
            m_Lengths = new int[rows];
            m_Colors = new Color32[rows];
            for (int i = 0; i < rows; i++) m_Colors[i] = new Color32(255, 255, 255, 255);
        }

        public int Rows { get { return m_Lines.Length; } }

        public int Columns { get { return m_Columns; } }

        /// <summary>内容版本：任何可见变化 +1。</summary>
        public int Version { get { return m_Version; } }

        /// <summary>写一行（超出列数截断）。内容与颜色都未变时不改版本。</summary>
        public void SetLine(int row, ReadOnlySpan<char> text, Color32 color)
        {
            CheckRow(row);
            int length = text.Length < m_Columns ? text.Length : m_Columns;
            char[] line = m_Lines[row];
            bool changed = length != m_Lengths[row] || !SameColor(color, m_Colors[row]);
            for (int i = 0; i < length; i++)
            {
                if (line[i] != text[i])
                {
                    line[i] = text[i];
                    changed = true;
                }
            }

            m_Lengths[row] = length;
            m_Colors[row] = color;
            if (changed) m_Version++;
        }

        public void ClearLine(int row)
        {
            CheckRow(row);
            if (m_Lengths[row] == 0) return;
            m_Lengths[row] = 0;
            m_Version++;
        }

        public void Clear()
        {
            for (int i = 0; i < m_Lines.Length; i++) ClearLine(i);
        }

        public int GetLength(int row)
        {
            CheckRow(row);
            return m_Lengths[row];
        }

        public ReadOnlySpan<char> GetLine(int row)
        {
            CheckRow(row);
            return new ReadOnlySpan<char>(m_Lines[row], 0, m_Lengths[row]);
        }

        public Color32 GetColor(int row)
        {
            CheckRow(row);
            return m_Colors[row];
        }

        private void CheckRow(int row)
        {
            if (row < 0 || row >= m_Lines.Length) throw new FrameworkException("OverlayTextBuffer：行号越界 " + row + "。");
        }

        private static bool SameColor(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }
    }
}
