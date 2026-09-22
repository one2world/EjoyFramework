//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Navigation;
using EjoyFramework.Core.Streaming;
using UnityEngine;
using UnityEngine.AI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 基于 UnityEngine.AI.NavMesh + NavMeshAgent 的导航 helper 实现。
    /// 业务在场景中烘焙好 NavMesh，业务实体挂 NavAgentBinding 即可使用。
    /// </summary>
    public sealed class UnityNavMeshHelper : INavigationHelper
    {
        public void CalculatePath(Vector3Lite from, Vector3Lite to, int areaMask, Action<NavPathResult> onComplete)
        {
            if (onComplete == null) return;
            var path = new NavMeshPath();
            bool ok = NavMesh.CalculatePath(ToVec3(from), ToVec3(to), areaMask, path);
            var result = new NavPathResult();
            if (!ok)
            {
                result.Status = NavPathStatus.Error;
                result.ErrorMessage = "NavMesh.CalculatePath returned false (no source NavMesh).";
                onComplete(result);
                return;
            }
            result.Corners = ToLiteArray(path.corners);
            result.Length = ComputeLength(path.corners);
            switch (path.status)
            {
                case NavMeshPathStatus.PathComplete: result.Status = NavPathStatus.Success; break;
                case NavMeshPathStatus.PathPartial: result.Status = NavPathStatus.Partial; break;
                case NavMeshPathStatus.PathInvalid:
                default: result.Status = NavPathStatus.Unreachable; break;
            }
            onComplete(result);
        }

        public object AddNavMeshData(object navMeshData, Vector3Lite position)
        {
            var data = navMeshData as NavMeshData;
            if (data == null)
            {
                FrameworkLog.Error("UnityNavMeshHelper.AddNavMeshData: object is not a NavMeshData ({0}).", navMeshData == null ? "null" : navMeshData.GetType().Name);
                return null;
            }

            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data, ToVec3(position), Quaternion.identity);
            if (!instance.valid) return null;
            return new TileHandle { Instance = instance };
        }

        public void RemoveNavMeshData(object helperHandle)
        {
            var tile = helperHandle as TileHandle;
            if (tile == null) return;
            if (tile.Instance.valid) NavMesh.RemoveNavMeshData(tile.Instance);
        }

        private sealed class TileHandle
        {
            public NavMeshDataInstance Instance;
        }

        public object CreateAgent(NavAgentConfig config)
        {
            // 创建空 GameObject 持 NavMeshAgent；业务可改造为接受外部 transform。
            var go = new GameObject("[NavAgent] " + (config.Name ?? "anon"));
            go.transform.position = ToVec3(config.InitialPosition);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.speed = config.Speed;
            agent.angularSpeed = config.AngularSpeed;
            agent.acceleration = config.Acceleration;
            agent.stoppingDistance = config.StoppingDistance;
            agent.radius = config.Radius;
            agent.height = config.Height;
            agent.areaMask = config.AreaMask;
            return new AgentWrapper { Go = go, Agent = agent };
        }

        public void DestroyAgent(object helperHandle)
        {
            var w = (AgentWrapper)helperHandle;
            if (w?.Go != null) UnityEngine.Object.Destroy(w.Go);
        }

        public void SetAgentDestination(object helperHandle, Vector3Lite destination)
        {
            var w = (AgentWrapper)helperHandle;
            if (w?.Agent != null) w.Agent.SetDestination(ToVec3(destination));
        }

        public void WarpAgent(object helperHandle, Vector3Lite position)
        {
            var w = (AgentWrapper)helperHandle;
            if (w?.Agent != null) w.Agent.Warp(ToVec3(position));
        }

        public Vector3Lite GetAgentPosition(object helperHandle)
        {
            var w = (AgentWrapper)helperHandle;
            return w?.Go != null ? ToLite(w.Go.transform.position) : default;
        }

        public bool HasReachedDestination(object helperHandle)
        {
            var w = (AgentWrapper)helperHandle;
            if (w?.Agent == null) return false;
            if (w.Agent.pathPending) return false;
            return w.Agent.remainingDistance <= w.Agent.stoppingDistance && !w.Agent.hasPath;
        }

        public void StopAgent(object helperHandle)
        {
            var w = (AgentWrapper)helperHandle;
            if (w?.Agent != null) { w.Agent.isStopped = true; w.Agent.ResetPath(); }
        }

        // ===== util =====

        private sealed class AgentWrapper
        {
            public GameObject Go;
            public NavMeshAgent Agent;
        }

        private static Vector3 ToVec3(Vector3Lite v) => new Vector3(v.X, v.Y, v.Z);
        private static Vector3Lite ToLite(Vector3 v) => new Vector3Lite(v.x, v.y, v.z);
        private static Vector3Lite[] ToLiteArray(Vector3[] arr)
        {
            if (arr == null) return Array.Empty<Vector3Lite>();
            var r = new Vector3Lite[arr.Length];
            for (int i = 0; i < arr.Length; i++) r[i] = ToLite(arr[i]);
            return r;
        }
        private static float ComputeLength(Vector3[] corners)
        {
            if (corners == null || corners.Length < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < corners.Length; i++) total += Vector3.Distance(corners[i - 1], corners[i]);
            return total;
        }
    }
}
