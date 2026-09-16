using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.SlotTable.GapBuffer;

namespace DotNetCompose.Runtime.SlotTable
{
    public partial class ComposerSlotTable
    {
        public sealed partial class Writer
        {
            private readonly HashSet<GapBufferItemAnchor> _existingGroups = new HashSet<GapBufferItemAnchor>();
            public int CurrentGroup { get; private set; }

            public void Reposition(int groupIndex)
            {
                EnsureOpen();
                if (groupIndex < 0 || groupIndex > _groups.Count) throw new ArgumentOutOfRangeException(nameof(groupIndex));
                CurrentGroup = groupIndex;
            }

            public void StartGroup(GroupAnchor anchor)
            {
                EnsureOpen();
                GapBufferItemAnchor item = Table.Resolve(anchor);
                int index = _groups.IndexOf(item);
                if (!_existingGroups.Add(item)) throw new InvalidOperationException("Group is already open.");
                CurrentGroup = index + 1;
                _groupStack.Push(item);
            }

            private GroupRecord RecordAt(int groupIndex) => _groups.GetAt(groupIndex);

            private int DataBoundary(int groupIndex)
            {
                for (int i = groupIndex; i < _groups.Count; i++)
                {
                    GroupRecord group = RecordAt(i);
                    if (group.DataAnchor != GapBufferItemAnchor.Empty)
                        return _slots.IndexOf(group.DataAnchor);
                }
                return _slots.Count;
            }

            private void AdjustAncestors(GapBufferItemAnchor parent, int size, int nodes)
            {
                int remaining = _groups.Count;
                while (parent != GapBufferItemAnchor.Empty)
                {
                    if (remaining-- == 0) throw new InvalidOperationException("A group cannot be its own ancestor.");
                    ref GroupRecord group = ref _groups.GetRef(parent);
                    group.Size += size;
                    group.NodeCount += nodes;
                    if (group.IsNode) nodes = 0;
                    parent = group.ParentAnchor;
                }
            }

            private void ValidateInsertion(int insertionIndex, GapBufferItemAnchor parent)
            {
                int first = parent == GapBufferItemAnchor.Empty ? 0 : _groups.IndexOf(parent) + 1;
                int end = parent == GapBufferItemAnchor.Empty ? _groups.Count : first - 1 + _groups.Get(parent).Size;
                int boundary = first;
                while (boundary < insertionIndex && boundary < end)
                {
                    int size = RecordAt(boundary).Size;
                    if (size <= 0) throw new InvalidOperationException($"Invalid group size at {boundary}.");
                    boundary += size;
                }
                if (insertionIndex < first || insertionIndex > end || boundary != insertionIndex)
                    throw new ArgumentOutOfRangeException(nameof(insertionIndex), "Expected a child boundary of the parent.");
            }

            /// <summary>Imports a complete subtree. Returned anchors belong to the destination table.</summary>
            public IReadOnlyDictionary<GroupAnchor, GroupAnchor> ImportGroup(
                ComposerSlotTable source, GroupAnchor sourceAnchor, int insertionIndex, GroupAnchor parent)
            {
                EnsureOpen();
                if (ReferenceEquals(source, Table)) throw new ArgumentException("Use MoveGroup within one table.", nameof(source));
                GapBufferItemAnchor parentItem = Table.Resolve(parent, allowEmpty: true);
                GapBufferItemAnchor sourceItem = source.Resolve(sourceAnchor);
                ValidateInsertion(insertionIndex, parentItem);
                using Reader reader = source.OpenReader();
                int sourceIndex = source.IndexOf(sourceAnchor);
                GroupRecord root = source._groups.Get(sourceItem);
                int dataIndex = DataBoundary(insertionIndex);
                Dictionary<GapBufferItemAnchor, GapBufferItemAnchor> mapping = new Dictionary<GapBufferItemAnchor, GapBufferItemAnchor>();
                Dictionary<GroupAnchor, GroupAnchor> publicMapping = new Dictionary<GroupAnchor, GroupAnchor>();
                for (int i = 0; i < root.Size; i++)
                {
                    GapBufferItemAnchor oldAnchor = source._groups.AnchorAt(sourceIndex + i);
                    GroupRecord group = source._groups.Get(oldAnchor);
                    group.ParentAnchor = i == 0 ? parentItem : mapping[group.ParentAnchor];
                    int dataCount = group.MetadataSlotCount + group.SlotCount;
                    int sourceData = dataCount == 0 ? 0 : source._slots.IndexOf(group.DataAnchor);
                    group.DataAnchor = GapBufferItemAnchor.Empty;
                    for (int j = 0; j < dataCount; j++)
                    {
                        object? value = source._slots.GetAt(sourceData + j);
                        GapBufferItemAnchor dataAnchor = _slots.InsertTrackedAt(dataIndex++, value);
                        if (j == 0) group.DataAnchor = dataAnchor;
                    }
                    GapBufferItemAnchor newAnchor = _groups.InsertTrackedAt(insertionIndex + i, group);
                    mapping.Add(oldAnchor, newAnchor);
                    publicMapping.Add(source.Wrap(oldAnchor), Table.Wrap(newAnchor));
                }
                AdjustAncestors(parentItem, root.Size, root.IsNode ? 1 : root.NodeCount);
                return publicMapping;
            }

            public void RemoveGroup(GroupAnchor anchor)
            {
                EnsureOpen();
                GapBufferItemAnchor item = Table.Resolve(anchor);
                int index = _groups.IndexOf(item);
                GroupRecord root = _groups.Get(item);
                if (root.Size <= 0) throw new InvalidOperationException($"Invalid group size at {index}.");
                for (int i = 0; i < root.Size; i++)
                {
                    GroupRecord group = RecordAt(index);
                    int count = group.MetadataSlotCount + group.SlotCount;
                    int data = count == 0 ? 0 : _slots.IndexOf(group.DataAnchor);
                    for (int j = 0; j < count; j++) _slots.RemoveAt(data);
                    _groups.RemoveAt(index);
                }
                AdjustAncestors(root.ParentAnchor, -root.Size, -(root.IsNode ? 1 : root.NodeCount));
                CurrentGroup = Math.Min(CurrentGroup, _groups.Count);
            }

            /// <summary>Moves a subtree to a sibling boundary in the original table.</summary>
            public void MoveGroup(GroupAnchor anchor, int insertionIndex)
            {
                EnsureOpen();
                GapBufferItemAnchor item = Table.Resolve(anchor);
                int from = _groups.IndexOf(item);
                GroupRecord group = _groups.Get(item);
                ValidateInsertion(insertionIndex, group.ParentAnchor);
                if (insertionIndex == from || insertionIndex == from + group.Size) return;
                int dataFrom = DataBoundary(from);
                int dataEnd = DataBoundary(from + group.Size);
                int dataTo = DataBoundary(insertionIndex);
                _slots.MoveRange(dataFrom, dataTo, dataEnd - dataFrom);
                _groups.MoveRange(from, insertionIndex, group.Size);
            }

            public void SetSlot(GroupAnchor anchor, int slotOffset, object? value)
            {
                EnsureOpen();
                GroupRecord group = _groups.Get(Table.Resolve(anchor));
                if (slotOffset < 0 || slotOffset >= group.SlotCount) throw new ArgumentOutOfRangeException(nameof(slotOffset));
                int index = _slots.IndexOf(group.DataAnchor) + group.MetadataSlotCount + slotOffset;
                _slots.SetAt(index, value);
            }

            public void AppendSlot(GroupAnchor anchor, object? value)
            {
                EnsureOpen();
                GapBufferItemAnchor item = Table.Resolve(anchor);
                _groupStack.Push(item);
                try { AppendSlot(value); }
                finally { _groupStack.Pop(); }
            }

            public void TrimSlots(GroupAnchor anchor, int count)
            {
                EnsureOpen();
                ref GroupRecord group = ref _groups.GetRef(Table.Resolve(anchor));
                if (count < 0 || count > group.SlotCount) throw new ArgumentOutOfRangeException(nameof(count));
                if (count == group.SlotCount) return;
                int index = _slots.IndexOf(group.DataAnchor) + group.MetadataSlotCount + count;
                for (int i = count; i < group.SlotCount; i++) _slots.RemoveAt(index);
                group.SlotCount = count;
                if (count + group.MetadataSlotCount == 0) group.DataAnchor = GapBufferItemAnchor.Empty;
            }

            public void UpdateNode(GroupAnchor anchor, object? node)
            {
                EnsureOpen();
                GroupRecord group = _groups.Get(Table.Resolve(anchor));
                if (!group.IsNode) throw new InvalidOperationException("Expected a node group.");
                _slots.Set(group.DataAnchor, node);
            }

            public void UpdateAux(GroupAnchor anchor, object? aux)
            {
                EnsureOpen();
                GroupRecord group = _groups.Get(Table.Resolve(anchor));
                if (!group.HasAux) throw new InvalidOperationException("Group has no auxiliary slot.");
                int index = _slots.IndexOf(group.DataAnchor) + (group.IsNode ? 1 : 0) + (group.HasObjectKey ? 1 : 0);
                _slots.SetAt(index, aux);
            }
        }
    }
}
