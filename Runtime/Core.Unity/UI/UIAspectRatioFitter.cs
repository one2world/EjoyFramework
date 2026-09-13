//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;
using EjoyFramework.Core;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 宽高比适配：把内容限制在目标 AspectRatio 内，超出部分自动加 letterbox（黑边）或 pillarbox。
    /// 适用于：
    ///   - 超宽屏（21:9）需要把 16:9 内容居中显示
    ///   - 折叠屏 / 平板的非标比例需要保护核心 UI 区域
    ///
    /// 用法：把本组件挂在 UIForm root RectTransform 上，调 RecomputeNow() 或开 AutoFollow=true 自动跟随。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("EjoyFramework/Core/UI/Aspect Ratio Fitter")]
    public sealed class UIAspectRatioFitter : MonoBehaviour
    {
        public enum FitMode
        {
            /// <summary>EnvelopeAspect：内容填满容器，超出部分由父容器裁剪。</summary>
            Envelope,
            /// <summary>FitAspect：内容居中并按目标比例缩放，超出区域留白（letterbox/pillarbox）。</summary>
            Fit,
        }

        [SerializeField] private float m_TargetAspect = 16f / 9f;
        [SerializeField] private FitMode m_Mode = FitMode.Fit;
        [SerializeField]
        [Tooltip("启用后每帧检测屏幕尺寸 / 父容器尺寸变化并自动重算。")]
        private bool m_AutoFollow = true;

        private RectTransform m_Rt;
        private Vector2 m_LastParentSize;

        private void Awake() { m_Rt = (RectTransform)transform; }

        private void OnEnable() { RecomputeNow(); }

        private void Update()
        {
            if (!m_AutoFollow) return;
            var size = GetParentSize();
            if (size != m_LastParentSize) RecomputeNow();
        }

        public float TargetAspect
        {
            get { return m_TargetAspect; }
            set { m_TargetAspect = value > 0f ? value : 1f; RecomputeNow(); }
        }

        public FitMode Mode
        {
            get { return m_Mode; }
            set { m_Mode = value; RecomputeNow(); }
        }

        /// <summary>立即按当前父容器尺寸 / TargetAspect 重算 anchor + offset。</summary>
        public void RecomputeNow()
        {
            if (m_Rt == null) m_Rt = (RectTransform)transform;
            var size = GetParentSize();
            m_LastParentSize = size;
            if (size.x <= 0f || size.y <= 0f) return;

            float parentAspect = size.x / size.y;
            float scale;

            if (m_Mode == FitMode.Envelope)
            {
                // 内容填满：父容器比目标宽 → 高度需放大；父容器比目标窄 → 宽度需放大
                scale = parentAspect > m_TargetAspect
                    ? (parentAspect / m_TargetAspect)
                    : (m_TargetAspect / parentAspect);
                m_Rt.anchorMin = new Vector2(0.5f, 0.5f);
                m_Rt.anchorMax = new Vector2(0.5f, 0.5f);
                m_Rt.sizeDelta = new Vector2(size.x * scale, size.y * scale);
                m_Rt.anchoredPosition = Vector2.zero;
            }
            else // Fit
            {
                // 内容按目标比缩放，留 letterbox
                Vector2 targetSize;
                if (parentAspect > m_TargetAspect)
                {
                    // 父容器更宽 → pillarbox（左右留白）
                    targetSize = new Vector2(size.y * m_TargetAspect, size.y);
                }
                else
                {
                    // 父容器更高 → letterbox（上下留白）
                    targetSize = new Vector2(size.x, size.x / m_TargetAspect);
                }
                m_Rt.anchorMin = new Vector2(0.5f, 0.5f);
                m_Rt.anchorMax = new Vector2(0.5f, 0.5f);
                m_Rt.sizeDelta = targetSize;
                m_Rt.anchoredPosition = Vector2.zero;
            }
        }

        private Vector2 GetParentSize()
        {
            if (m_Rt == null) m_Rt = (RectTransform)transform;
            var parent = m_Rt.parent as RectTransform;
            if (parent != null) return parent.rect.size;
            throw new FrameworkException("UIAspectRatioFitter requires a parent RectTransform.");
        }
    }
}
