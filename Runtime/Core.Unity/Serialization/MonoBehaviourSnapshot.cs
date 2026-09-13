//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core;
using EjoyFramework.Core.Serialization;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Generated MonoBehaviour snapshot contract. Implementations restore state onto the current
    /// component instance; they never create MonoBehaviour instances.
    /// </summary>
    public interface IMonoBehaviourSnapshot
    {
        void CaptureSnapshot(ByteBuffer buffer, MonoBehaviourSnapshotContext context);

        void RestoreSnapshot(ByteBuffer buffer, MonoBehaviourSnapshotContext context);
    }

    /// <summary>
    /// Runtime services used by generated MonoBehaviour snapshot code.
    /// </summary>
    public sealed class MonoBehaviourSnapshotContext
    {
        public MonoBehaviourSnapshotContext(IUnityObjectReferenceResolver objectReferenceResolver)
        {
            ObjectReferenceResolver = objectReferenceResolver;
        }

        public IUnityObjectReferenceResolver ObjectReferenceResolver { get; private set; }
    }

    /// <summary>
    /// Converts UnityEngine.Object references to stable snapshot tokens and back.
    /// A token is scoped by the concrete resolver implementation; null objects are represented by
    /// a null token and do not require resolver access.
    /// </summary>
    public interface IUnityObjectReferenceResolver
    {
        string ToReference(UnityEngine.Object value);

        UnityEngine.Object FromReference(string reference, Type expectedType);
    }

    public static class MonoBehaviourSnapshotUtility
    {
        public static IUnityObjectReferenceResolver RequireObjectReferenceResolver(
            MonoBehaviourSnapshotContext context,
            string memberName)
        {
            if (context != null && context.ObjectReferenceResolver != null)
            {
                return context.ObjectReferenceResolver;
            }

            throw new FrameworkException(
                "MonoBehaviour snapshot member '" + memberName + "' requires an IUnityObjectReferenceResolver.");
        }
    }
}
