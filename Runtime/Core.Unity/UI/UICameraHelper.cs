//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.Rendering;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// UI 相机 helper：标准 orthographic 配置 + 仅渲染 UI layer + 高深度（覆盖主相机）。
    ///
    /// 三种典型 Canvas RenderMode 支持：
    ///   - ScreenSpaceOverlay：不需要 Camera（最高性能；但无法插入 3D 物体到 UI）
    ///   - ScreenSpaceCamera：用本 helper 提供的 Camera（支持 UI 中嵌入 3D 模型 / shader effects）
    ///   - WorldSpace：业务自管 Camera（罕见）
    ///
    /// 默认模式：ScreenSpaceCamera + UICamera depth=10（盖在主相机上）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("EjoyFramework/Core/UI/Camera")]
    public sealed class UICameraHelper : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("UI 相机渲染深度（越高越靠后渲染；UI 通常需要盖在主相机之上，默认 10）。")]
        private float m_Depth = 10f;

        [SerializeField]
        [Tooltip("仅渲染指定 layer（默认只渲染 \"UI\" layer）。-1 表示渲染全部。")]
        private string m_RenderLayerName = "UI";

        [SerializeField]
        [Tooltip("Orthographic size：默认正交相机大小（不影响 ScreenSpaceCamera 模式下的 UI 显示）。")]
        private float m_OrthographicSize = 5f;

        public Camera Camera { get; private set; }

        private void Awake()
        {
            Camera = GetComponent<Camera>();
            Configure();
        }

        /// <summary>
        /// 立即按 Inspector 字段配置 Camera。Edit-time 安全：Awake 未触发时也会自动取 Camera 引用，
        /// 这样 prefab 构建阶段直接调用本方法即可把正确状态持久化进 prefab，避免使用者在 Inspector
        /// 看到 perspective / cullingMask=Everything 等 Unity 默认值造成误解。
        /// 业务方运行时改字段后亦可调此方法刷新。
        /// </summary>
        public void Configure()
        {
            if (Camera == null) Camera = GetComponent<Camera>();
            if (Camera == null) return;

            Camera.orthographic = true;
            Camera.orthographicSize = m_OrthographicSize;
            Camera.clearFlags = CameraClearFlags.Depth;
            Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            Camera.depth = m_Depth;
            Camera.nearClipPlane = -100f;
            Camera.farClipPlane = 100f;
            Camera.allowHDR = false;
            Camera.allowMSAA = false;
            Camera.useOcclusionCulling = false;
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Camera.stereoTargetEye = StereoTargetEyeMask.None;
            }

            int layer = LayerMask.NameToLayer(m_RenderLayerName);
            if (layer < 0)
            {
                Log.Warning("UICameraHelper: layer '{0}' not found; falling back to UI-layer-5 bit.", m_RenderLayerName);
                Camera.cullingMask = 1 << 5;   // Unity 内置 UI layer 通常是 5；外加 fallback 比 -1(Everything) 安全得多
            }
            else
            {
                Camera.cullingMask = 1 << layer;
            }
        }
    }
}
