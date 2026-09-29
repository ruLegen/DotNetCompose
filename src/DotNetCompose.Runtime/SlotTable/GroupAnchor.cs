using System;
using System.Runtime.CompilerServices;
using DotNetCompose.Runtime.SlotTable.GapBuffer;

namespace DotNetCompose.Runtime.SlotTable
{
    /// <summary>Identifies a group in one slot table, even when its index changes.</summary>
    public readonly struct GroupAnchor : IEquatable<GroupAnchor>
    {
        internal GroupAnchor(ComposerSlotTable owner, GapBufferItemAnchor item)
        {
            Owner = owner;
            Item = item;
        }

        internal ComposerSlotTable? Owner { get; }
        internal GapBufferItemAnchor Item { get; }

        public static GroupAnchor Empty => default;
        public bool IsEmpty => Owner == null;

        public bool Equals(GroupAnchor other) => ReferenceEquals(Owner, other.Owner) && Item.Equals(other.Item);
        public override bool Equals(object? obj) => obj is GroupAnchor other && Equals(other);
        public override int GetHashCode() => Owner == null ? 0 : (RuntimeHelpers.GetHashCode(Owner) * 397) ^ Item.GetHashCode();
        public static bool operator ==(GroupAnchor left, GroupAnchor right) => left.Equals(right);
        public static bool operator !=(GroupAnchor left, GroupAnchor right) => !left.Equals(right);
        public override string ToString() => IsEmpty ? "GroupAnchor.Empty" : $"GroupAnchor({Item.Id}:{Item.Generation})";
    }
}
