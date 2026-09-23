//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 把 <see cref="OverlayTextBuffer"/> 画成 UGUI 网格：每个字符一个四边形，字形取自动态字体图集。
    /// 只预取可打印 ASCII（32..126）——性能覆盖层的内容是数字与英文标签，这样 <c>GetCharacterInfo</c> 全程零分配；
    /// 图集重建（<see cref="Font.textureRebuilt"/>）时重新预取并重建网格。非 ASCII 字符按半个字宽留空。
    /// 仅在缓冲版本变化时重建网格（<see cref="Refresh"/>）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class OverlayTextGraphic : MaskableGraphic
    {
        private const string AsciiSet =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        [SerializeField] private Font m_Font;
        [SerializeField] private int m_FontSize = 14;
        [SerializeField] private float m_LineSpacing = 1.25f;
        [SerializeField] private float m_Padding = 6f;

        private OverlayTextBuffer m_Buffer;
        private int m_BuiltVersion = -1;
        private bool m_Subscribed;

        public Font Font
        {
            get { return m_Font; }
            set
            {
                if (m_Font == value) return;
                m_Font = value;
                RequestGlyphs();
                SetAllDirty();
            }
        }

        public int FontSize
        {
            get { return m_FontSize; }
            set
            {
                int size = value < 4 ? 4 : value;
                if (m_FontSize == size) return;
                m_FontSize = size;
                RequestGlyphs();
                SetVerticesDirty();
            }
        }

        public OverlayTextBuffer Buffer
        {
            get { return m_Buffer; }
            set
            {
                m_Buffer = value;
                m_BuiltVersion = -1;
                SetVerticesDirty();
            }
        }

        /// <summary>最近一次建网格画出的字形数（测试 / 诊断用）。</summary>
        public int LastGlyphCount { get; private set; }

        /// <summary>内容需要的像素高度（行数 × 行高 + 上下留白）。</summary>
        public float PreferredHeight
        {
            get { return m_Buffer == null ? 0f : m_Buffer.Rows * m_FontSize * m_LineSpacing + m_Padding * 2f; }
        }

        public override Texture mainTexture
        {
            get { return m_Font != null && m_Font.material != null ? m_Font.material.mainTexture : base.mainTexture; }
        }

        /// <summary>缓冲版本变化时标记重建（每帧调用也只是一次整数比较）。</summary>
        public void Refresh()
        {
            if (m_Buffer != null && m_Buffer.Version != m_BuiltVersion) SetVerticesDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!m_Subscribed)
            {
                Font.textureRebuilt += OnFontTextureRebuilt;
                m_Subscribed = true;
            }

            RequestGlyphs();
        }

        protected override void OnDisable()
        {
            if (m_Subscribed)
            {
                Font.textureRebuilt -= OnFontTextureRebuilt;
                m_Subscribed = false;
            }

            base.OnDisable();
        }

        private void OnFontTextureRebuilt(Font font)
        {
            if (font != m_Font) return;
            RequestGlyphs();
            SetAllDirty();   // 图集换了：UV 与贴图都要更新
        }

        private void RequestGlyphs()
        {
            if (m_Font != null && m_Font.dynamic) m_Font.RequestCharactersInTexture(AsciiSet, m_FontSize, FontStyle.Normal);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            LastGlyphCount = 0;
            if (m_Buffer == null || m_Font == null) return;
            m_BuiltVersion = m_Buffer.Version;

            Rect rect = GetPixelAdjustedRect();
            float lineHeight = m_FontSize * m_LineSpacing;
            float baseline = rect.yMax - m_Padding - m_FontSize;
            float left = rect.xMin + m_Padding;
            for (int row = 0; row < m_Buffer.Rows; row++)
            {
                int length = m_Buffer.GetLength(row);
                Color32 rowColor = m_Buffer.GetColor(row);
                Color32 c = new Color32(rowColor.r, rowColor.g, rowColor.b, (byte)(rowColor.a * color.a));
                System.ReadOnlySpan<char> line = m_Buffer.GetLine(row);
                float x = left;
                for (int i = 0; i < length; i++)
                {
                    CharacterInfo ci;
                    if (!m_Font.GetCharacterInfo(line[i], out ci, m_FontSize, FontStyle.Normal))
                    {
                        x += m_FontSize * 0.5f;
                        continue;
                    }

                    if (ci.maxX > ci.minX && ci.maxY > ci.minY)
                    {
                        int start = vh.currentVertCount;
                        float x0 = x + ci.minX, x1 = x + ci.maxX;
                        float y0 = baseline + ci.minY, y1 = baseline + ci.maxY;
                        vh.AddVert(new Vector3(x0, y0), c, ci.uvBottomLeft);
                        vh.AddVert(new Vector3(x0, y1), c, ci.uvTopLeft);
                        vh.AddVert(new Vector3(x1, y1), c, ci.uvTopRight);
                        vh.AddVert(new Vector3(x1, y0), c, ci.uvBottomRight);
                        vh.AddTriangle(start, start + 1, start + 2);
                        vh.AddTriangle(start + 2, start + 3, start);
                        LastGlyphCount++;
                    }

                    x += ci.advance;
                }

                baseline -= lineHeight;
            }
        }
    }
}
