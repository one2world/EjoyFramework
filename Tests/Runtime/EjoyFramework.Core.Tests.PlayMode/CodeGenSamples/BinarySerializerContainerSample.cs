//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace EjoyFramework.Tests.CodeGenSamples
{
    public partial class BinarySerializerContainerSample
    {
        public Dictionary<string, Vector3> Points;
        public HashSet<string> Tags;
        public Queue<string> Names;
        public Stack<int> History;
        public Matrix4x4 Matrix;
        public LayerMask Mask;
    }
}
