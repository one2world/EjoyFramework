//------------------------------------------------------------
// EjoyGame
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Serialization;

namespace EjoyFramework.Tests.AssemblyCSharpCodeGenSamples
{
    public enum AssemblyCSharpSnapshotFaction : byte
    {
        Neutral = 0,
        Guard = 1,
        Demon = 2,
    }

    [Flags]
    public enum AssemblyCSharpSnapshotAbilityFlags : ushort
    {
        None = 0,
        Melee = 1 << 0,
        Ranged = 1 << 1,
        Shield = 1 << 2,
        Ultimate = 1 << 3,
    }

    [Serializable]
    public sealed class AssemblyCSharpSnapshotStats : IBinarySerializable
    {
        public int Level;
        public float Health;
        public string Label;
        public AssemblyCSharpSnapshotFaction Faction;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Level);
            buffer.WriteFloat(Health);
            buffer.WriteString(Label);
            buffer.WriteByte((byte)Faction);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Level = buffer.ReadInt();
            Health = buffer.ReadFloat();
            Label = buffer.ReadString();
            Faction = (AssemblyCSharpSnapshotFaction)buffer.ReadByte();
        }
    }

    [Serializable]
    public struct AssemblyCSharpSnapshotRange : IBinarySerializable
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

    [Serializable]
    public abstract class AssemblyCSharpSnapshotManagedEffect
    {
        public int Id;
    }

    [Serializable]
    public sealed class AssemblyCSharpSnapshotDamageEffect
        : AssemblyCSharpSnapshotManagedEffect, IBinarySerializable
    {
        public int Damage;
        public string Element;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Id);
            buffer.WriteInt(Damage);
            buffer.WriteString(Element);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Id = buffer.ReadInt();
            Damage = buffer.ReadInt();
            Element = buffer.ReadString();
        }
    }

    [Serializable]
    public sealed class AssemblyCSharpSnapshotBuffEffect
        : AssemblyCSharpSnapshotManagedEffect, IBinarySerializable
    {
        public float Multiplier;
        public AssemblyCSharpSnapshotRange Duration;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Id);
            buffer.WriteFloat(Multiplier);
            Duration.Serialize(buffer);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Id = buffer.ReadInt();
            Multiplier = buffer.ReadFloat();
            Duration.Deserialize(buffer);
        }
    }

    [Serializable]
    public abstract class AssemblyCSharpSnapshotManagedEffect<T>
    {
        public T Value;
    }

    [Serializable]
    public sealed class AssemblyCSharpSnapshotIntEffect
        : AssemblyCSharpSnapshotManagedEffect<int>, IBinarySerializable
    {
        public int Extra;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(Value);
            buffer.WriteInt(Extra);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            Value = buffer.ReadInt();
            Extra = buffer.ReadInt();
        }
    }

    [Serializable]
    public class AssemblyCSharpSnapshotConcreteEffect : IBinarySerializable
    {
        public int BaseValue;

        public void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(BaseValue);
        }

        public void Deserialize(ByteBuffer buffer)
        {
            BaseValue = buffer.ReadInt();
        }
    }

    [Serializable]
    public sealed class AssemblyCSharpSnapshotConcreteDerivedEffect : AssemblyCSharpSnapshotConcreteEffect
    {
        public int DerivedValue;

        public new void Serialize(ByteBuffer buffer)
        {
            buffer.WriteInt(BaseValue);
            buffer.WriteInt(DerivedValue);
        }

        public new void Deserialize(ByteBuffer buffer)
        {
            BaseValue = buffer.ReadInt();
            DerivedValue = buffer.ReadInt();
        }
    }
}
