//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Marks a partial MonoBehaviour for generated snapshot serialization.
    ///
    /// The generated implementation writes Unity-serialized fields into a ByteBuffer and restores
    /// them onto an existing component instance. UnityEngine.Object references are resolved through
    /// IUnityObjectReferenceResolver instead of being instantiated by the serializer.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class GenerateMonoBehaviourSnapshotAttribute : Attribute { }
}
