using EjoyFramework.GamePlay.ThreeC;
using UnityEngine;

namespace EjoyFramework.GamePlay.Unity.ThreeC
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform m_Target;
        [SerializeField] private ThirdPersonInputSource m_Input;
        [SerializeField] private Vector3 m_TargetOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField] private float m_Distance = 5f;
        [SerializeField] private float m_PositionSmoothTime = 0.06f;
        [SerializeField] private float m_CollisionRadius = 0.2f;
        [SerializeField] private LayerMask m_CollisionMask = ~0;

        private OrbitCameraController m_Orbit;
        private Vector3 m_PositionVelocity;
        private float m_CurrentDistance;

        private void Awake()
        {
            m_Orbit = new OrbitCameraController(m_Distance);
            m_CurrentDistance = m_Distance;
        }

        private void LateUpdate()
        {
            if (m_Target == null || m_Input == null) return;
            using (ThreeCPerformanceCounters.Camera.Measure())
            {
                PlayerControlFrame control = m_Input.Current;
                OrbitCameraState state = m_Orbit.Step(in control, Time.unscaledDeltaTime);
                Quaternion rotation = Quaternion.Euler(state.Pitch, state.Yaw, 0f);
                Vector3 focus = m_Target.position + m_TargetOffset;
                Vector3 backward = rotation * Vector3.back;
                float allowedDistance = state.Distance;
                if (Physics.SphereCast(
                    focus,
                    m_CollisionRadius,
                    backward,
                    out RaycastHit hit,
                    state.Distance,
                    m_CollisionMask,
                    QueryTriggerInteraction.Ignore))
                {
                    allowedDistance = Mathf.Max(0.05f, hit.distance - m_CollisionRadius);
                }

                m_CurrentDistance = Mathf.MoveTowards(
                    m_CurrentDistance, allowedDistance, 20f * Time.unscaledDeltaTime);
                Vector3 desired = focus + backward * m_CurrentDistance;
                transform.position = Vector3.SmoothDamp(
                    transform.position, desired, ref m_PositionVelocity, m_PositionSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                transform.rotation = rotation;
            }
        }

        public void Bind(Transform target, ThirdPersonInputSource input)
        {
            m_Target = target;
            m_Input = input;
        }
    }
}
