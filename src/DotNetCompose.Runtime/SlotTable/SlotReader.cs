using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime
{
    internal sealed class SlotReader
    {
        private readonly GroupRecord[] _groups;
        private readonly int _groupsSize;
        private readonly object?[] _slots;
        private readonly int _slotsSize;
        private int _currentGroup;
        private int _currentEnd;
        private int _currentSlot;
        private int _currentSlotEnd;
        private int _emptyCount;

        private readonly Stack<int> _parentStack = new Stack<int>();
        private readonly Stack<int> _endStack = new Stack<int>();
        private readonly Stack<int> _slotStack = new Stack<int>();
        private readonly Stack<int> _slotEndStack = new Stack<int>();
        private readonly Stack<int> _emptyStack = new Stack<int>();
        private readonly Stack<int> _childStack = new Stack<int>();
        private bool _atParent;

        internal SlotReader(GroupRecord[] groups, int groupsSize, object?[] slots, int slotsSize)
        {
            _groups = groups;
            _groupsSize = groupsSize;
            _slots = slots;
            _slotsSize = slotsSize;
            _currentGroup = 0;
            _currentEnd = groupsSize;
            _currentSlot = groups[0].DataAnchor;
            _currentSlotEnd = _currentSlot + SlotCount(groups[0]);
            _emptyCount = 0;
            _atParent = true;
        }

        private static int SlotCount(GroupRecord g)
        {
            if (g.IsNode) return 2;
            if (g.HasObjectKey || g.HasAux) return 1;
            return 0;
        }

        public int CurrentGroup => _currentGroup;
        public int CurrentEnd => _currentEnd;
        public int CurrentSlot => _currentSlot;
        public int CurrentSlotEnd => _currentSlotEnd;

        public bool IsNode => _groups[_currentGroup].IsNode;

        public bool IsNodeAt(int group) => _groups[group].IsNode;
        public int NodeCount => _groups[_currentGroup].NodeCount;
        public int GroupSize {
            get {
                int sz = _groups[_currentGroup].Size;
                return sz;
            }
        }
        public int GroupSizeAt(int idx) => _groups[idx].Size;
        public int GroupKey => _groups[_currentGroup].Key;
        public GroupFlags GroupFlags => _groups[_currentGroup].Flags;
        public bool HasObjectKey => _groups[_currentGroup].HasObjectKey;
        public object? GroupNode => (_groups[_currentGroup].IsNode && _groups[_currentGroup].DataAnchor + 1 < _slotsSize)
            ? _slots[_groups[_currentGroup].DataAnchor + 1]
            : null;
        public bool IsGroupEnd => _emptyCount > 0 || _currentGroup >= _currentEnd;
        public int EmptyCount => _emptyCount;

        public bool IsRoot => _currentGroup == 0;

        public int Parent => _parentStack.Count > 0 ? _parentStack.Peek() : -1;

        public int Size => _groupsSize;

        public GapAnchor Anchor(int location) => new GapAnchor(location);

        public void StartGroup()
        {
            _parentStack.Push(_currentGroup);
            _endStack.Push(_currentEnd);
            _slotStack.Push(_currentSlot);
            _slotEndStack.Push(_currentSlotEnd);
            _emptyStack.Push(_emptyCount);

            int child = _atParent ? _currentGroup + 1 : _currentGroup;
            _childStack.Push(child);
            ref var g = ref _groups[child];
            _currentGroup = child;
            _currentEnd = Math.Min(_currentEnd, child + g.Size);
            _currentSlot = g.DataAnchor;
            _currentSlotEnd = _currentSlot + SlotCount(g);
            _emptyCount = 0;
            _atParent = true;
        }

        public void EndGroup()
        {
            int childStart = _childStack.Pop();
            int childSize = _groups[childStart].Size;

            _emptyCount = _emptyStack.Pop();
            _currentSlotEnd = _slotEndStack.Pop();
            _currentSlot = _slotStack.Pop();
            _currentEnd = _endStack.Pop();
            _currentGroup = _parentStack.Pop();

            int nextSibling = childStart + childSize;
            if (nextSibling >= _currentEnd)
                _currentGroup = _currentEnd;
            else
                _currentGroup = nextSibling - 1;
            _atParent = true;

            if (_currentGroup < _currentEnd && _currentGroup < _groupsSize)
            {
                ref var next = ref _groups[_currentGroup];
                _currentSlot = next.DataAnchor;
                _currentSlotEnd = _currentSlot + SlotCount(next);
            }
            else
            {
                _currentSlot = _currentSlotEnd;
            }
        }

        public int SkipGroup()
        {
            ref var g = ref _groups[_currentGroup];
            int nc = g.NodeCount;
            int size = g.Size;

            int nextGroup = _currentGroup + size;
            _currentGroup = nextGroup;

            if (_currentGroup >= _currentEnd && _endStack.Count > 0)
                _currentEnd = _endStack.Peek();

            if (_currentGroup < _currentEnd)
            {
                ref var next = ref _groups[_currentGroup];
                _currentSlot = next.DataAnchor;
                _currentSlotEnd = _currentSlot + SlotCount(next);
            }
            else
            {
                _currentSlot = _currentSlotEnd;
            }

            return nc;
        }

        public void SkipToGroupEnd()
        {
            _currentGroup = _currentEnd;
            if (_currentGroup < _groupsSize)
            {
                ref var g = ref _groups[_currentGroup];
                _currentSlot = g.DataAnchor;
                _currentSlotEnd = _currentSlot + SlotCount(g);
            }
        }

        public void Reposition(int logicalIndex)
        {
            if (logicalIndex < 0 || logicalIndex >= _groupsSize)
                throw new ArgumentOutOfRangeException(nameof(logicalIndex));
            _currentGroup = logicalIndex;
            _currentEnd = _groupsSize;
            ref var g = ref _groups[logicalIndex];
            _currentSlot = g.DataAnchor;
            _currentSlotEnd = _currentSlot + SlotCount(g);
            _emptyCount = 0;
            _atParent = true;
        }

        public object? Next()
        {
            if (_emptyCount > 0)
            {
                _emptyCount--;
                return null;
            }
            if (_currentSlot < _currentSlotEnd)
            {
                return _slots[_currentSlot++];
            }
            return null;
        }

        public object? Peek()
        {
            if (_emptyCount > 0 || _currentSlot >= _currentSlotEnd)
                return null;
            return _slots[_currentSlot];
        }

        public object? Get(int slotOffset)
        {
            int slotIndex = _groups[_currentGroup].DataAnchor + slotOffset;
            return _slots[slotIndex];
        }

        public object? GroupGet(int group, int slotOffset)
        {
            return _slots[_groups[group].DataAnchor + slotOffset];
        }

        public int GroupIndexOf(int group)
        {
            return _groups[group].Key;
        }

        public int GroupCount(int group)
        {
            return _groups[group].NodeCount;
        }

        public int GroupSizeOf(int group)
        {
            return _groups[group].Size;
        }

        public bool HasAuxAt(int group)
        {
            if (group < 0 || group >= _groupsSize) return false;
            return _groups[group].HasAux;
        }

        public object? GroupGetAux(int group)
        {
            if (group < 0 || group >= _groupsSize) return null;
            ref var g = ref _groups[group];
            if (!g.HasAux) return null;
            int slotIndex = g.DataAnchor;
            if (g.IsNode) slotIndex += 2;
            else if (g.HasObjectKey) slotIndex += 1;
            if (slotIndex < _slotsSize)
                return _slots[slotIndex];
            return null;
        }

        /// <summary>
        /// Returns the logical index of the parent group for the given group.
        /// Uses the parent anchor encoding with ParentAnchorPivot = -2.
        /// </summary>
        public int GetParentGroup(int group)
        {
            if (group <= 0 || group >= _groupsSize) return -1;
            ref var g = ref _groups[group];
            int anchor = g.ParentAnchor;
            // Decode: if anchor > -2 it's a direct index, otherwise relative to size
            if (anchor > -2)
                return anchor;
            return _groupsSize + anchor + 2;
        }
    }
}
