//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Serialization;
using UnityEngine;

namespace EjoyFramework.Tests.CodeGenSamples
{
    public enum SnapshotSampleMode : byte
    {
        None = 0,
        Active = 1,
        Paused = 2,
    }

    public sealed class SnapshotNestedPayload : IBinarySerializable
    {
        public int Id;
        public string Label;
        public float Weight;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Id);
            buffer.WriteString(Label);
            buffer.WriteFloat(Weight);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Id = buffer.ReadInt();
            Label = buffer.ReadString();
            Weight = buffer.ReadFloat();
        }
    }

    public partial class MonoBehaviourSnapshotSampleComponent : MonoBehaviour
    {
        public int IntValue;
        public long LongValue;
        public float FloatValue;
        public double DoubleValue;
        public bool BoolValue;
        public char CharValue;
        public string StringValue;
        public SnapshotSampleMode Mode;
        public Vector2 Vector2Value;
        public Vector3 Vector3Value;
        public Vector4 Vector4Value;
        public Vector2Int Vector2IntValue;
        public Vector3Int Vector3IntValue;
        public Quaternion RotationValue;
        public Color ColorValue;
        public Color32 Color32Value;
        public Rect RectValue;
        public int[] IntArray;
        public Vector3[] Vector3Array;
        public List<string> StringList;
        public SnapshotNestedPayload Payload;
        public List<SnapshotNestedPayload> PayloadList;
        public GameObject Target;
        public List<GameObject> Targets;

        [SerializeField]
        private int m_PrivateInt;

        [SerializeField]
        private Vector3 m_PrivatePosition;

        [SerializeField]
        private List<int> m_PrivateScores;

        public int PrivateInt => m_PrivateInt;
        public Vector3 PrivatePosition => m_PrivatePosition;
        public List<int> PrivateScores => m_PrivateScores;

        public void SetPrivateState(int privateInt, Vector3 privatePosition, List<int> privateScores)
        {
            m_PrivateInt = privateInt;
            m_PrivatePosition = privatePosition;
            m_PrivateScores = privateScores;
        }
    }
}
