using System;

namespace EjoyFramework.Core.Ecs
{
    /// <summary>世界内的带版本实体句柄。默认值无效；不持有组件或表现对象。</summary>
    public readonly struct Entity : IEquatable<Entity>
    {
        public long WorldId { get; }
        public int Index { get; }
        public uint Version { get; }

        internal Entity(long worldId, int index, uint version)
        {
            WorldId = worldId;
            Index = index;
            Version = version;
        }

        public bool Equals(Entity other) => WorldId == other.WorldId && Index == other.Index && Version == other.Version;
        public override bool Equals(object obj) => obj is Entity other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((WorldId.GetHashCode() * 397) ^ Index) * 397 ^ (int)Version; }
        }
        public static bool operator ==(Entity left, Entity right) => left.Equals(right);
        public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
        public override string ToString() => $"Entity({WorldId}:{Index}:{Version})";
    }
}
