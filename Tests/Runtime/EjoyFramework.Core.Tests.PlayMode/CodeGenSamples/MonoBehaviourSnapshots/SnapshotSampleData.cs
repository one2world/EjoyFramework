//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Serialization;

namespace EjoyFramework.Tests.CodeGenSamples.MonoBehaviourSnapshots
{
    public enum SnapshotFaction : byte
    {
        Neutral = 0,
        Guard = 1,
        Demon = 2,
    }

    [Flags]
    public enum SnapshotAbilityFlags : ushort
    {
        None = 0,
        Melee = 1 << 0,
        Ranged = 1 << 1,
        Control = 1 << 2,
        Ultimate = 1 << 3,
    }

    [Serializable]
    public sealed class SnapshotStats : IBinarySerializable
    {
        public int Level;
        public float Health;
        public string Title;
        public SnapshotFaction Faction;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Level);
            buffer.WriteFloat(Health);
            buffer.WriteString(Title);
            buffer.WriteByte((byte)Faction);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Level = buffer.ReadInt();
            Health = buffer.ReadFloat();
            Title = buffer.ReadString();
            Faction = (SnapshotFaction)buffer.ReadByte();
        }
    }

    [Serializable]
    public struct SnapshotRange : IBinarySerializable
    {
        public short Min;
        public short Max;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteShort(Min);
            buffer.WriteShort(Max);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Min = buffer.ReadShort();
            Max = buffer.ReadShort();
        }
    }
}
