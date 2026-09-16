using DotNetCompose.Runtime.SlotTable.GapBuffer;
using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.SlotTable
{
    public partial class ComposerSlotTable
    {
        /// <summary>
        /// Reads groups in preorder. CurrentGroup is the next group to enter or skip;
        /// Next reads the user slots of Parent, the last group entered.
        /// </summary>
        public sealed class Reader : IDisposable
        {
            internal Reader(ComposerSlotTable table)
            {
                Table = table;
                _groups = table._groups;
                _slots = table._slots;
                _size = _groups.Count;
                _currentEnd = _size;
            }

            internal readonly ComposerSlotTable Table;
            private readonly SlotMapGapBuffer<GroupRecord> _groups;
            private readonly SlotMapGapBuffer<object?> _slots;
            private readonly int _size;
            private readonly Stack<(int Slot, int End)> _slotStack = new();
            private int _currentGroup;
            private int _currentEnd;
            private int _parent = -1;
            private int _currentSlot;
            private int _currentSlotEnd;
            private int _emptyCount;
            private bool _hadNext;

            public bool Closed { get; private set; }
            public int Size { get { EnsureOpen(); return _size; } }
            public int CurrentGroup { get { EnsureOpen(); return _currentGroup; } }
            public int CurrentEnd { get { EnsureOpen(); return _currentEnd; } }
            public int Parent { get { EnsureOpen(); return _parent; } }
            public int GroupEnd => CurrentEnd;
            public bool InEmpty { get { EnsureOpen(); return _emptyCount > 0; } }
            public bool IsGroupEnd { get { EnsureOpen(); return _emptyCount > 0 || _currentGroup == _currentEnd; } }
            public bool HadNext { get { EnsureOpen(); return _hadNext; } }
            public int RemainingSlots { get { EnsureOpen(); return _currentSlotEnd - _currentSlot; } }
            public int Slot => GroupSlotIndex;
            public int GroupSlotIndex
            {
                get
                {
                    EnsureOpen();
                    return _parent < 0 || _currentSlotEnd == 0 ? 0 : _currentSlot - SlotStart(ReadGroup(_parent));
                }
            }

            public bool IsNode => GetIsNode(CurrentGroup);
            public int NodeCount => GetNodeCount(CurrentGroup);
            public int GroupSize => GetGroupSize(CurrentGroup);
            public int GroupSlotCount => GetSlotSize(CurrentGroup);
            public int ParentNodes { get { EnsureOpen(); return _parent < 0 ? 0 : GetNodeCount(_parent); } }
            public int GroupKey { get { EnsureOpen(); return _currentGroup < _currentEnd ? GetGroupKey(_currentGroup) : 0; } }
            public bool HasObjectKey { get { EnsureOpen(); return _currentGroup < _currentEnd && GetHasObjectKey(_currentGroup); } }
            public object? GroupObjectKey { get { EnsureOpen(); return _currentGroup < _currentEnd ? GetGroupObjectKey(_currentGroup) : null; } }
            public object? GroupAux { get { EnsureOpen(); return _currentGroup < _currentEnd ? GetGroupAux(_currentGroup) : 0; } }
            public object? GroupNode
            {
                get
                {
                    EnsureOpen();
                    if (_currentGroup >= _currentEnd) return null;
                    GroupRecord group = ReadGroup(_currentGroup);
                    return group.IsNode ? DataAt(group, 0) : Empty;
                }
            }

            public int GetParent(int groupIndex)
            {
                GroupRecord group = ReadGroup(groupIndex);
                return _groups.IndexOf(group.ParentAnchor);
            }
            public int ParentOf(int groupIndex) => GetParent(groupIndex);
            public bool GetIsNode(int groupIndex) => ReadGroup(groupIndex).IsNode;
            public int GetNodeCount(int groupIndex) => ReadGroup(groupIndex).NodeCount;
            public int GetGroupSize(int groupIndex) => ReadGroup(groupIndex).Size;
            public int GetGroupEnd(int groupIndex) => groupIndex + ReadGroup(groupIndex).Size;
            public int GetSlotSize(int groupIndex) => ReadGroup(groupIndex).SlotCount;
            public int GetGroupKey(int groupIndex) => ReadGroup(groupIndex).Key;
            public bool GetHasObjectKey(int groupIndex) => ReadGroup(groupIndex).HasObjectKey;
            public bool HasMark(int groupIndex) => ReadGroup(groupIndex).IsMarked;
            public bool ContainsMark(int groupIndex) => ReadGroup(groupIndex).ContainsMarked;

            public object? GetNode(int groupIndex)
            {
                GroupRecord group = ReadGroup(groupIndex);
                return group.IsNode ? DataAt(group, 0) : null;
            }

            public object? GetGroupObjectKey(int groupIndex)
            {
                GroupRecord group = ReadGroup(groupIndex);
                return group.HasObjectKey ? DataAt(group, group.IsNode ? 1 : 0) : null;
            }

            public object? GetGroupAux(int groupIndex)
            {
                GroupRecord group = ReadGroup(groupIndex);
                return group.HasAux ? DataAt(group, (group.IsNode ? 1 : 0) + (group.HasObjectKey ? 1 : 0)) : Empty;
            }

            /// <summary>Returns an existing group anchor belonging to this table.</summary>
            public GroupAnchor Anchor() => Anchor(CurrentGroup);
            public GroupAnchor Anchor(int groupIndex)
            {
                ReadGroup(groupIndex);
                return Table.Wrap(_groups.AnchorAt(groupIndex));
            }

            /// <summary>Returns zero for an empty or stale anchor; rejects an anchor from another table.</summary>
            public int GetGroupKey(GroupAnchor anchor)
            {
                EnsureOpen();
                if (anchor.IsEmpty) return 0;
                if (!ReferenceEquals(anchor.Owner, Table))
                    throw new ArgumentException("The group anchor belongs to another slot table.", nameof(anchor));
                return _groups.IsValidAnchor(anchor.Item) ? _groups.Get(anchor.Item).Key : 0;
            }

            /// <summary>Peeks relative to the next slot, without advancing; like Kotlin get, ignores empty mode.</summary>
            public object? Get(int slotOffset)
            {
                EnsureOpen();
                if (slotOffset < 0) throw new ArgumentOutOfRangeException(nameof(slotOffset));
                return slotOffset < _currentSlotEnd - _currentSlot ? SlotAt(_currentSlot + slotOffset) : Empty;
            }

            public object? GroupGet(int slotOffset) => GroupGet(CurrentGroup, slotOffset);
            /// <summary>Reads a user slot relative to the start of a group's user slots.</summary>
            public object? GroupGet(int groupIndex, int slotOffset)
            {
                GroupRecord record = ReadGroup(groupIndex);
                if (slotOffset < 0) throw new ArgumentOutOfRangeException(nameof(slotOffset));
                return slotOffset < record.SlotCount ? SlotAt(SlotStart(record) + slotOffset) : Empty;
            }

            public object? Next()
            {
                EnsureOpen();
                _hadNext = _emptyCount == 0 && _currentSlot < _currentSlotEnd;
                return _hadNext ? SlotAt(_currentSlot++) : Empty;
            }

            public void BeginEmpty()
            {
                EnsureOpen();
                _emptyCount++;
            }

            public void EndEmpty()
            {
                EnsureOpen();
                if (_emptyCount == 0) throw new InvalidOperationException("Unbalanced begin/end empty.");
                _emptyCount--;
            }

            public void StartGroup()
            {
                EnsureOpen();
                if (_emptyCount > 0) return;
                GroupRecord group = ReadCurrentGroup();
                if (_groups.IndexOf(group.ParentAnchor) != _parent)
                    throw new InvalidOperationException("The current group does not belong to the reader's parent.");
                _slotStack.Push((_currentSlot, _currentSlotEnd));
                _parent = _currentGroup;
                _currentEnd = _currentGroup + group.Size;
                _currentGroup++;
                _currentSlot = SlotStart(group);
                _currentSlotEnd = _currentSlot + group.SlotCount;
            }

            public void StartNode()
            {
                EnsureOpen();
                if (_emptyCount > 0) return;
                if (!ReadCurrentGroup().IsNode) throw new InvalidOperationException("Expected a node group.");
                StartGroup();
            }

            public void EndGroup()
            {
                EnsureOpen();
                if (_emptyCount > 0) return;
                if (_parent < 0 || _slotStack.Count == 0 || _currentGroup != _currentEnd)
                    throw new InvalidOperationException("EndGroup requires a started group at its end.");
                _parent = GetParent(_parent);
                _currentEnd = _parent < 0 ? _size : GetGroupEnd(_parent);
                (_currentSlot, _currentSlotEnd) = _slotStack.Pop();
            }

            public int SkipGroup()
            {
                EnsureNotEmpty();
                GroupRecord group = ReadCurrentGroup();
                _currentGroup += group.Size;
                return group.IsNode ? 1 : group.NodeCount;
            }

            public void SkipToGroupEnd()
            {
                EnsureNotEmpty();
                _currentGroup = _currentEnd;
                _currentSlot = _currentSlotEnd = 0;
            }

            /// <summary>Moves to a group (or Size). Does not discard the stack of explicitly started groups.</summary>
            public void Reposition(int groupIndex)
            {
                EnsureNotEmpty();
                if (groupIndex < 0 || groupIndex > _size) throw new ArgumentOutOfRangeException(nameof(groupIndex));
                int parent = groupIndex < _size ? GetParent(groupIndex) : -1;
                _currentGroup = groupIndex;
                if (parent != _parent)
                {
                    _parent = parent;
                    _currentEnd = parent < 0 ? _size : GetGroupEnd(parent);
                    _currentSlot = _currentSlotEnd = 0;
                }
            }

            /// <summary>Restores an enclosing group after repositioning, without restoring its slot cursor.</summary>
            public void RestoreParent(int groupIndex)
            {
                int end = GetGroupEnd(groupIndex);
                if (_currentGroup < groupIndex || _currentGroup > end)
                    throw new InvalidOperationException("The group does not enclose the current position.");
                _parent = groupIndex;
                _currentEnd = end;
                _currentSlot = _currentSlotEnd = 0;
            }

            public List<KeyInfo> ExtractKeys()
            {
                EnsureOpen();
                List<KeyInfo> result = new List<KeyInfo>();
                if (_emptyCount > 0) return result;
                for (int index = _currentGroup; index < _currentEnd;)
                {
                    GroupRecord group = ReadGroup(index);
                    result.Add(new KeyInfo(group.Key, GetGroupObjectKey(index), index,
                        group.IsNode ? 1 : group.NodeCount, result.Count));
                    index += group.Size;
                }
                return result;
            }

            public void Close()
            {
                if (Closed) return;
                Table.CloseReader(this);
                Closed = true;
            }

            public void Dispose() => Close();
            public override string ToString() => $"SlotReader(current={CurrentGroup}, key={GroupKey}, parent={Parent}, end={CurrentEnd})";

            private GroupRecord ReadGroup(int groupIndex)
            {
                EnsureOpen();
                if (groupIndex < 0 || groupIndex >= _size) throw new ArgumentOutOfRangeException(nameof(groupIndex));
                return _groups.GetAt(groupIndex);
            }

            private GroupRecord ReadCurrentGroup()
            {
                if (_currentGroup >= _currentEnd) throw new InvalidOperationException("There is no group left in the current parent.");
                return ReadGroup(_currentGroup);
            }

            private int SlotStart(GroupRecord group) => group.DataAnchor == GapBufferItemAnchor.Empty
                ? 0 : _slots.IndexOf(group.DataAnchor) + group.MetadataSlotCount;
            private object? DataAt(GroupRecord group, int slotOffset) => SlotAt(_slots.IndexOf(group.DataAnchor) + slotOffset);
            private object? SlotAt(int slotIndex) => _slots.GetAt(slotIndex);

            private void EnsureOpen()
            {
                if (Closed) throw new ObjectDisposedException(nameof(Reader));
            }

            private void EnsureNotEmpty()
            {
                EnsureOpen();
                if (_emptyCount > 0) throw new InvalidOperationException("Cannot skip or reposition while in an empty region.");
            }
        }
    }
}
