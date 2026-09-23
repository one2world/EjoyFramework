//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 帧时间柱状图：每个样本一根柱，高度 = 帧耗时 / <see cref="MaxMs"/>；不超预算绿、超预算不到两倍黄、两倍以上红；
    /// 另画一条预算线。样本由调用方写入 <see cref="Samples"/>（毫秒，最旧→最新），然后 <see cref="MarkDirty"/>。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FrameGraphGraphic : MaskableGraphic
    {
        private static readonly Color32 s_Good = new Color32(80, 220, 110, 220);
        private static readonly Color32 s_Warn = new Color32(240, 200, 60, 230);
        private static readonly Color32 s_Bad = new Color32(240, 70, 60, 240);
        private static readonly Color32 s_Budget = new Color32(255, 255, 255, 140);

        private float[] m_Samples = new float[240];
        private int m_Count;

        /// <summary>纵轴上限（毫秒）。默认 50。</summary>
        public float MaxMs = 50f;

        /// <summary>帧预算（毫秒）。默认 16.67。</summary>
        public float BudgetMs = 1000f / 60f;

        public float[] Samples { get { return m_Samples; } }

        public int SampleCount
        {
            get { return m_Count; }
            set { m_Count = value < 0 ? 0 : value > m_Samples.Length ? m_Samples.Length : value; }
        }

        /// <summary>更换样本容量（分配新数组，初始化时调用）。</summary>
        public void SetCapacity(int capacity)
        {
            m_Samples = new float[capacity < 2 ? 2 : capacity];
            m_Count = 0;
        }

        public void MarkDirty()
        {
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (m_Count == 0 || rect.width <= 0f || rect.height <= 0f) return;

            float barWidth = rect.width / m_Samples.Length;
            float x = rect.xMax - m_Count * barWidth;   // 最新的贴右边
            float max = MaxMs > 0f ? MaxMs : 1f;
            for (int i = 0; i < m_Count; i++)
            {
                float ms = m_Samples[i];
                float h = ms / max;
                if (h > 1f) h = 1f;
                if (h < 0f) h = 0f;
                Color32 c = ms <= BudgetMs ? s_Good : ms <= BudgetMs * 2f ? s_Warn : s_Bad;
                AddQuad(vh, x, rect.yMin, x + barWidth * 0.8f, rect.yMin + h * rect.height, c);
                x += barWidth;
            }

            float by = rect.yMin + (BudgetMs / max > 1f ? 1f : BudgetMs / max) * rect.height;
            AddQuad(vh, rect.xMin, by - 0.5f, rect.xMax, by + 0.5f, s_Budget);
        }

        private static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
        {
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
