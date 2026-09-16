using DotNetCompose.Runtime.SlotTable.GapBuffer;
using System;
using System.Runtime.InteropServices;

namespace DotNetCompose.Runtime
{
    [Flags]
    internal enum GroupFlags : int
    {
        None = 0,
        Node = 1,
        ObjectKey = 2,
        Aux = 4,
        Mark = 8,
        ContainsMark = 16,
        IsMovableContent = 32,
        HasMovableContent = 64,
    }

    [StructLayout(LayoutKind.Auto)]
    internal struct GroupRecord
    {
        public int Key;
        public GroupFlags Flags;
        public int NodeCount;
        public GapBufferItemAnchor ParentAnchor;
        public int Size;
        public GapBufferItemAnchor DataAnchor;
        // Only user slots; node, object key and aux are stored before these slots.
        public int SlotCount;

        public int MetadataSlotCount => (IsNode ? 1 : 0) + (HasObjectKey ? 1 : 0) + (HasAux ? 1 : 0);

        public bool IsNode => (Flags & GroupFlags.Node) != 0;
        public bool HasObjectKey => (Flags & GroupFlags.ObjectKey) != 0;
        public bool HasAux => (Flags & GroupFlags.Aux) != 0;
        public bool IsMarked => (Flags & GroupFlags.Mark) != 0;
        public bool ContainsMarked => (Flags & GroupFlags.ContainsMark) != 0;
        public bool IsMovable => (Flags & GroupFlags.IsMovableContent) != 0;
        public bool HasMovable => (Flags & GroupFlags.HasMovableContent) != 0;
    }
}
