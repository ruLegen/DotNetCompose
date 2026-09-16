using DotNetCompose.Runtime.SlotTable.GapBuffer;
using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.SlotTable
{
    public partial class ComposerSlotTable
    {
        /// <summary>Appends groups and writes the slots of the currently open group.</summary>
        public sealed class Writer : IDisposable
        {
            internal Writer(ComposerSlotTable table)
            {
                Table = table;
                _groups = table._groups;
                _slots = table._slots;
            }

            public readonly ComposerSlotTable Table;
            private readonly SlotMapGapBuffer<GroupRecord> _groups;
            private readonly SlotMapGapBuffer<object?> _slots;
            private readonly Stack<GapBufferItemAnchor> _groupStack = new();
            public bool Closed { get; private set; }

            public void StartGroup() => StartGroup(0);
            public void StartGroup(int key) => StartGroupCore(key, Empty, Empty, Empty, false);
            public void StartGroup(int key, object? objectKey) => StartGroupCore(key, objectKey, Empty, Empty, false);
            public void StartGroup(int key, object? objectKey, object? aux) => StartGroupCore(key, objectKey, aux, Empty, false);
            public void StartNode(int key, object? node) => StartGroupCore(key, Empty, Empty, node, true);

            private void StartGroupCore(int key, object? objectKey, object? aux, object? node, bool isNode)
            {
                EnsureOpen();
                GroupRecord group = new GroupRecord
                {
                    Key = key,
                    Size = 1,
                    ParentAnchor = _groupStack.TryPeek(out GapBufferItemAnchor parent) ? parent : GapBufferItemAnchor.Empty,
                    DataAnchor = GapBufferItemAnchor.Empty,
                    Flags = (isNode ? GroupFlags.Node : GroupFlags.None)
                        | (!ReferenceEquals(objectKey, Empty) ? GroupFlags.ObjectKey : GroupFlags.None)
                        | (!ReferenceEquals(aux, Empty) ? GroupFlags.Aux : GroupFlags.None)
                };

                // New groups are appended in preorder. Their data initially goes at the end.
                if (group.IsNode) AppendMetadata(ref group, node);
                if (group.HasObjectKey) AppendMetadata(ref group, objectKey);
                if (group.HasAux) AppendMetadata(ref group, aux);
                _groupStack.Push(_groups.InsertStable(_groups.Count, group));
            }

            private void AppendMetadata(ref GroupRecord group, object? value)
            {
                if (group.DataAnchor == GapBufferItemAnchor.Empty)
                    group.DataAnchor = _slots.InsertStable(_slots.Count, value);
                else
                    _slots.Insert(_slots.Count, value);
            }

            public void EndGroup()
            {
                EnsureGroup();
                GroupRecord group = _groups.Get(_groupStack.Pop());
                if (group.ParentAnchor != GapBufferItemAnchor.Empty)
                {
                    ref GroupRecord parent = ref _groups.GetRef(group.ParentAnchor);
                    parent.Size += group.Size;
                    parent.NodeCount += group.IsNode ? 1 : group.NodeCount;
                }
            }

            public void SkipGroup()
            {
                EnsureOpen();
                throw new NotSupportedException("Editing existing groups is not implemented.");
            }

            public void RemoveGroup()
            {
                EnsureOpen();
                throw new NotSupportedException("Editing existing groups is not implemented.");
            }

            public void AppendSlot(object? value)
            {
                EnsureGroup();
                GapBufferItemAnchor anchor = _groupStack.Peek();
                ref GroupRecord group = ref _groups.GetRef(anchor);
                if (group.DataAnchor == GapBufferItemAnchor.Empty)
                {
                    // Empty parents can acquire their first slot after their children were written.
                    // Locate the boundary without anchoring a gap or a non-existent item.
                    int insertIndex = _slots.Count;
                    for (int i = _groups.GetIndexOfAnchor(anchor) + 1; i < _groups.Count; i++)
                    {
                        GroupRecord next = _groups.Get(_groups.GetAddressOfIndex(i));
                        if (next.DataAnchor != GapBufferItemAnchor.Empty)
                        {
                            insertIndex = _slots.GetIndexOfAnchor(next.DataAnchor);
                            break;
                        }
                    }
                    group.DataAnchor = _slots.InsertStable(insertIndex, value);
                }
                else
                {
                    int index = _slots.GetIndexOfAnchor(group.DataAnchor) + group.MetadataSlotCount + group.SlotCount;
                    _slots.Insert(index, value);
                }
                group.SlotCount++;
            }

            /// <summary>Updates the last user slot appended to the current group.</summary>
            public void UpdateSlot(object? value)
            {
                EnsureGroup();
                GroupRecord group = _groups.Get(_groupStack.Peek());
                if (group.SlotCount == 0)
                    throw new InvalidOperationException("The current group has no user slot to update.");
                int index = _slots.GetIndexOfAnchor(group.DataAnchor) + group.MetadataSlotCount + group.SlotCount - 1;
                _slots.Set(_slots.GetAddressOfIndex(index), value);
            }

            public void Close()
            {
                if (Closed) return;
                if (_groupStack.Count != 0)
                    throw new InvalidOperationException("Cannot close a writer with unclosed groups.");
                Table.CloseWriter(this);
                Closed = true;
            }

            public void Dispose() => Close();

            private void EnsureOpen()
            {
                if (Closed) throw new ObjectDisposedException(nameof(Writer));
            }

            private void EnsureGroup()
            {
                EnsureOpen();
                if (_groupStack.Count == 0)
                    throw new InvalidOperationException("No group is open.");
            }
        }
    }
}
