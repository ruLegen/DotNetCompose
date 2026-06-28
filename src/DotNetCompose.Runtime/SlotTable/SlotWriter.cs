using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    internal sealed class SlotWriter
    {
        private const int MinGroupCapacity = SlotTable.MinGroupCapacity;
        private const int MinSlotCapacity = SlotTable.MinSlotCapacity;
        private const int ParentAnchorPivot = -2;

        private GroupRecord[] _groups;
        private object?[] _slots;
        private List<GapAnchor> _anchors;

        private int _logicalGroupCount;
        private int _logicalSlotCount;
        private int _gapGroupStart;
        private int _gapGroupLen;
        private int _gapSlotsStart;
        private int _gapSlotsLen;
        private int _gapSlotsOwner;

        private int _currentGroup;
        private int _currentGroupEnd;
        private int _currentSlot;
        private int _currentSlotEnd;
        private int _nodeCount;
        private int _insertCount;

        private int[] _grpStack = new int[16];
        private int _grpStackPtr;
        private int[] _endStack = new int[16];
        private int _endStackPtr;
        private int[] _slotStack = new int[16];
        private int _slotStackPtr;
        private int[] _slotEndStack = new int[16];
        private int _slotEndStackPtr;
        private int[] _ncStack = new int[16];
        private int _ncStackPtr;
        private int[] _insStack = new int[16];
        private int _insStackPtr;

        internal SlotWriter(
            GroupRecord[] groups, object?[] slots,
            List<GapAnchor> anchors,
            int groupsSize, int slotsSize)
        {
            _groups = groups;
            _slots = slots;
            _anchors = anchors;
            _logicalGroupCount = groupsSize;
            _logicalSlotCount = slotsSize;

            _gapGroupStart = groupsSize;
            _gapGroupLen = groups.Length - groupsSize;
            _gapSlotsStart = slotsSize;
            _gapSlotsLen = slots.Length - slotsSize;
            _gapSlotsOwner = -1;

            _currentGroup = 1;
            _currentGroupEnd = groupsSize;
            var g0 = groups[0];
            _currentSlot = g0.DataAnchor;
            _currentSlotEnd = _currentSlot + SlotCount(g0);
            _nodeCount = 1;
            _insertCount = 0;
        }

        internal GroupRecord[] Groups => _groups;
        internal object?[] Slots => _slots;
        internal List<GapAnchor> Anchors => _anchors;
        internal int Size => _logicalGroupCount;
        internal int SlotsSize => _logicalSlotCount;
        internal int CurrentGroup => _currentGroup;
        internal int CurrentSlot => _currentSlot;

        private static int SlotCount(GroupRecord g)
        {
            if (g.IsNode) return 2;
            if (g.HasObjectKey || g.HasAux) return 1;
            return 0;
        }

        private static int SlotCountForFlags(GroupFlags f)
        {
            if ((f & GroupFlags.Node) != 0) return 2;
            if ((f & (GroupFlags.ObjectKey | GroupFlags.Aux)) != 0) return 1;
            return 0;
        }

        private int LogicalToGroupPhysical(int logicalIdx)
        {
            if (logicalIdx < _gapGroupStart) return logicalIdx;
            return logicalIdx + _gapGroupLen;
        }

        private int GroupPhysicalToLogical(int physicalIdx)
        {
            if (physicalIdx < _gapGroupStart) return physicalIdx;
            if (physicalIdx >= _gapGroupStart + _gapGroupLen) return physicalIdx - _gapGroupLen;
            return -1;
        }

        private int LogicalToSlotPhysical(int logicalIdx)
        {
            if (logicalIdx < _gapSlotsStart) return logicalIdx;
            return logicalIdx + _gapSlotsLen;
        }

        internal void Close(bool normalClose)
        {
            if (normalClose)
            {
                // Update root's Size to encompass all groups
                int rootPhys = LogicalToGroupPhysical(0);
                var root = _groups[rootPhys];
                root.Size = _logicalGroupCount;
                root.NodeCount = _nodeCount;
                _groups[rootPhys] = root;

                if (_gapGroupStart != _logicalGroupCount)
                    MoveGroupGapTo(_logicalGroupCount);
                if (_gapSlotsStart != _logicalSlotCount)
                    MoveSlotGapTo(_logicalSlotCount, -1);
            }
        }

        private void EnsureStacks()
        {
            if (_grpStackPtr >= _grpStack.Length)
            {
                int n = _grpStack.Length * 2;
                Array.Resize(ref _grpStack, n);
                Array.Resize(ref _endStack, n);
                Array.Resize(ref _slotStack, n);
                Array.Resize(ref _slotEndStack, n);
                Array.Resize(ref _ncStack, n);
                Array.Resize(ref _insStack, n);
            }
        }

        private void PushState(int g, int e, int s, int se, int nc, int ic)
        {
            EnsureStacks();
            _grpStack[_grpStackPtr++] = g;
            _endStack[_endStackPtr++] = e;
            _slotStack[_slotStackPtr++] = s;
            _slotEndStack[_slotEndStackPtr++] = se;
            _ncStack[_ncStackPtr++] = nc;
            _insStack[_insStackPtr++] = ic;
        }

        private void PopState(out int g, out int e, out int s, out int se, out int nc, out int ic)
        {
            ic = _insStack[--_insStackPtr];
            nc = _ncStack[--_ncStackPtr];
            se = _slotEndStack[--_slotEndStackPtr];
            s = _slotStack[--_slotStackPtr];
            e = _endStack[--_endStackPtr];
            g = _grpStack[--_grpStackPtr];
        }

        private void UpdateAnchorsInRange(int fromPhysical, int toPhysical, int delta)
        {
            for (int i = 0; i < _anchors.Count; i++)
            {
                var a = _anchors[i];
                if (a.Location >= fromPhysical && a.Location < toPhysical)
                    a.Location += delta;
            }
        }

        private void InvalidateAnchorsInRange(int fromLogical, int count)
        {
            for (int i = 0; i < _anchors.Count; i++)
            {
                var a = _anchors[i];
                int idx = GapAnchorToLogical(a);
                if (idx >= fromLogical && idx < fromLogical + count)
                    a.Location = int.MinValue;
                else if (idx >= fromLogical + count)
                    a.Location -= count;
            }
        }

        private int GapAnchorToLogical(GapAnchor a)
        {
            if (a.Location == int.MinValue) return -1;
            if (a.Location < 0) return _logicalGroupCount + a.Location;
            return a.Location;
        }

        internal void MoveGroupGapTo(int newGapStart)
        {
            if (newGapStart == _gapGroupStart) return;
            int oldStart = _gapGroupStart;
            int gapEnd = oldStart + _gapGroupLen;
            int delta = newGapStart - oldStart;

            if (delta > 0)
            {
                int moveCount = delta;
                int src = gapEnd;
                int dst = oldStart;
                Array.Copy(_groups, src, _groups, dst, moveCount);
            }
            else
            {
                int moveCount = -delta;
                int src = newGapStart;
                int dst = newGapStart + _gapGroupLen;
                Array.Copy(_groups, src, _groups, dst, moveCount);
            }

            _gapGroupStart = newGapStart;
        }

        internal void MoveSlotGapTo(int newGapStart, int ownerGroup)
        {
            if (newGapStart == _gapSlotsStart && ownerGroup == _gapSlotsOwner) return;
            int oldStart = _gapSlotsStart;
            int gapEnd = oldStart + _gapSlotsLen;
            int delta = newGapStart - oldStart;

            if (delta > 0)
            {
                int moveCount = delta;
                Array.Copy(_slots, gapEnd, _slots, oldStart, moveCount);
                ShiftDataAnchors(oldStart, oldStart + moveCount, -moveCount);
            }
            else
            {
                int moveCount = -delta;
                Array.Copy(_slots, newGapStart, _slots, newGapStart + _gapSlotsLen, moveCount);
                ShiftDataAnchors(newGapStart, oldStart, moveCount);
            }

            _gapSlotsStart = newGapStart;
            _gapSlotsOwner = ownerGroup;
        }

        private void ShiftDataAnchors(int fromPhysical, int toPhysical, int delta)
        {
            int totalPhysical = _groups.Length;
            for (int i = 0; i < totalPhysical; i++)
            {
                if (i >= _gapGroupStart && i < _gapGroupStart + _gapGroupLen)
                    continue;
                int logical = GroupPhysicalToLogical(i);
                if (logical < 0 || logical >= _logicalGroupCount) continue;
                int da = _groups[i].DataAnchor;
                if (da >= fromPhysical && da < toPhysical)
                {
                    var g = _groups[i];
                    g.DataAnchor = da + delta;
                    _groups[i] = g;
                }
            }
        }

        private void GrowGroups(int minExtra)
        {
            int capacity = _groups.Length;
            int logicalBeforeGap = _logicalGroupCount - _gapGroupLen;
            int neededPhysical = logicalBeforeGap + _logicalGroupCount + minExtra;
            neededPhysical = Math.Max(neededPhysical, _logicalGroupCount + _gapGroupLen + minExtra);

            if (neededPhysical <= capacity) return;
            int newCapacity = Math.Max(capacity * 2, Math.Max(neededPhysical, MinGroupCapacity));
            var newArr = new GroupRecord[newCapacity];
            int oldPhysical = _logicalGroupCount + _gapGroupLen;

            Array.Copy(_groups, 0, newArr, 0, _gapGroupStart);
            Array.Copy(_groups, _gapGroupStart + _gapGroupLen, newArr,
                       _gapGroupStart + (newCapacity - oldPhysical + _gapGroupLen),
                       _logicalGroupCount - _gapGroupStart);

            _gapGroupLen += newCapacity - oldPhysical;
            _groups = newArr;
        }

        private void GrowSlots(int minExtra)
        {
            int capacity = _slots.Length;
            int logicalBeforeGap = _logicalSlotCount - _gapSlotsLen;
            int neededPhysical = logicalBeforeGap + _logicalSlotCount + minExtra;
            neededPhysical = Math.Max(neededPhysical, _logicalSlotCount + _gapSlotsLen + minExtra);

            if (neededPhysical <= capacity) return;
            int newCapacity = Math.Max(capacity * 2, Math.Max(neededPhysical, MinSlotCapacity));
            var newArr = new object?[newCapacity];
            int oldPhysical = _logicalSlotCount + _gapSlotsLen;

            Array.Copy(_slots, 0, newArr, 0, _gapSlotsStart);
            Array.Copy(_slots, _gapSlotsStart + _gapSlotsLen, newArr,
                       _gapSlotsStart + (newCapacity - oldPhysical + _gapSlotsLen),
                       _logicalSlotCount - _gapSlotsStart);

            _gapSlotsLen += newCapacity - oldPhysical;
            _slots = newArr;
        }

        internal void InsertGroups(int count)
        {
            MoveGroupGapTo(_currentGroup);
            if (_gapGroupLen < count)
                GrowGroups(count - _gapGroupLen);

            _gapGroupStart += count;
            _gapGroupLen -= count;
            _logicalGroupCount += count;
        }

        internal void InsertSlots(int count)
        {
            MoveSlotGapTo(_currentSlot, _currentGroup);
            if (_gapSlotsLen < count)
                GrowSlots(count - _gapSlotsLen);

            _gapSlotsStart += count;
            _gapSlotsLen -= count;
            _logicalSlotCount += count;
        }

        internal void StartGroup(int key)
        {
            PushState(_currentGroup, _currentGroupEnd, _currentSlot, _currentSlotEnd,
                      _nodeCount, _insertCount);

            InsertGroups(1);
            int idx = _currentGroup;
            _groups[LogicalToGroupPhysical(idx)] = new GroupRecord
            {
                Key = key,
                Size = 1,
                ParentAnchor = EncodeParentAnchor(idx),
                DataAnchor = _currentSlot,
            };

            _insertCount = 0;
            _nodeCount = 0;
            _currentGroup = idx + 1;
            _currentGroupEnd = _logicalGroupCount;
            _currentSlotEnd = _currentSlot;
        }

        internal void StartNode(int key, object? node, object? objectKey)
        {
            PushState(_currentGroup, _currentGroupEnd, _currentSlot, _currentSlotEnd,
                      _nodeCount, _insertCount);

            InsertGroups(1);
            InsertSlots(2);

            int idx = _currentGroup;
            int phys = LogicalToGroupPhysical(idx);
            _groups[phys] = new GroupRecord
            {
                Key = key,
                Flags = GroupFlags.Node,
                Size = 1,
                ParentAnchor = EncodeParentAnchor(idx),
                DataAnchor = _currentSlot,
            };

            int slotPhys = LogicalToSlotPhysical(_currentSlot);
            _slots[slotPhys] = objectKey;
            _slots[slotPhys + 1] = node;

            _insertCount = 0;
            _nodeCount = 0;
            _currentGroup = idx + 1;
            _currentGroupEnd = _logicalGroupCount;
            _currentSlot += 2;
            _currentSlotEnd = _currentSlot;
        }

        internal void StartGroupReuse(int key)
        {
            PushState(_currentGroup, _currentGroupEnd, _currentSlot, _currentSlotEnd,
                      _nodeCount, _insertCount);

            int idx = _currentGroup;
            if (idx >= _logicalGroupCount)
            {
                InsertGroups(1);
                idx = _currentGroup;
                _groups[LogicalToGroupPhysical(idx)] = new GroupRecord
                {
                    Key = key,
                    Size = 1,
                    ParentAnchor = EncodeParentAnchor(idx),
                    DataAnchor = _currentSlot,
                };
            }
            else
            {
                int phys = LogicalToGroupPhysical(idx);
                var g = _groups[phys];
                g.Key = key;
                _groups[phys] = g;
            }

            _insertCount = 0;
            _nodeCount = 0;
            _currentGroup = idx + 1;
            _currentGroupEnd = _logicalGroupCount;
            _currentSlotEnd = _currentSlot;
        }

        internal void StartNodeReuse(int key, object? node, object? objectKey)
        {
            PushState(_currentGroup, _currentGroupEnd, _currentSlot, _currentSlotEnd,
                      _nodeCount, _insertCount);

            int idx = _currentGroup;
            if (idx >= _logicalGroupCount)
            {
                InsertGroups(1);
                InsertSlots(2);
                idx = _currentGroup;
                int phys = LogicalToGroupPhysical(idx);
                _groups[phys] = new GroupRecord
                {
                    Key = key,
                    Flags = GroupFlags.Node,
                    Size = 1,
                    ParentAnchor = EncodeParentAnchor(idx),
                    DataAnchor = _currentSlot,
                };
                int slotPhys = LogicalToSlotPhysical(_currentSlot);
                _slots[slotPhys] = objectKey;
                _slots[slotPhys + 1] = node;
                _currentSlot += 2;
            }
            else
            {
                int phys = LogicalToGroupPhysical(idx);
                var g = _groups[phys];
                g.Key = key;
                g.Flags = GroupFlags.Node;
                _groups[phys] = g;
                int slotPhys = LogicalToSlotPhysical(g.DataAnchor);
                _slots[slotPhys] = objectKey;
                _slots[slotPhys + 1] = node;
                _currentSlot = g.DataAnchor + 2;
            }

            _insertCount = 0;
            _nodeCount = 0;
            _currentGroup = idx + 1;
            _currentGroupEnd = _logicalGroupCount;
            _currentSlotEnd = _currentSlot;
        }

        internal void EndGroup()
        {
            PopState(out int parentGroup, out int parentEnd,
                     out int parentSlot, out int parentSlotEnd,
                     out int parentNodeCount, out int parentInsertCount);

            int start = parentGroup;
            int size = _currentGroup - start;

            int phys = LogicalToGroupPhysical(start);
            var g = _groups[phys];
            g.Size = size;

            int nc = _nodeCount;
            if (g.IsNode) nc += 1;
            g.NodeCount = nc;
            _groups[phys] = g;

            _currentGroup = start + size;
            _currentGroupEnd = parentEnd;
            _currentSlot = _currentSlotEnd;

            _nodeCount = parentNodeCount + nc;
            _insertCount = parentInsertCount + size;
        }

        internal void SkipGroup()
        {
            int phys = LogicalToGroupPhysical(_currentGroup);
            var g = _groups[phys];
            int size = g.Size;
            int nc = g.NodeCount;
            int sc = SlotCount(g);

            _currentGroup += size;
            if (_currentGroup > _logicalGroupCount)
                _currentGroup = _logicalGroupCount;

            _nodeCount += nc;
            _insertCount += size;
            _currentSlot += sc;
        }

        internal object? Update(object? value)
        {
            int phys = LogicalToGroupPhysical(_currentGroup);
            var g = _groups[phys];
            int targetSlot = g.IsNode ? g.DataAnchor + 1 : _currentSlot;

            MoveSlotGapTo(targetSlot, _currentGroup);
            if (targetSlot >= _logicalSlotCount)
                InsertSlots(1);

            int slotPhys = LogicalToSlotPhysical(targetSlot);
            object? prev = _slots[slotPhys];
            _slots[slotPhys] = value;

            _currentSlot = targetSlot + 1;
            if (_currentSlot > _currentSlotEnd)
                _currentSlotEnd = _currentSlot;

            return prev;
        }

        internal object? Skip()
        {
            object? prev = null;
            if (_currentSlot < _logicalSlotCount)
            {
                int phys = LogicalToSlotPhysical(_currentSlot);
                prev = _slots[phys];
            }
            _currentSlot++;
            if (_currentSlot > _currentSlotEnd)
                _currentSlotEnd = _currentSlot;
            return prev;
        }

        internal object? UpdateAux(object? value)
        {
            MoveSlotGapTo(_currentSlot, _currentGroup);
            InsertSlots(1);

            int phys = LogicalToGroupPhysical(_currentGroup);
            _groups[phys].Flags |= GroupFlags.Aux;

            int slotPhys = LogicalToSlotPhysical(_currentSlot);
            object? prev = _slots[slotPhys];
            _slots[slotPhys] = value;

            _currentSlot++;
            if (_currentSlot > _currentSlotEnd)
                _currentSlotEnd = _currentSlot;

            return prev;
        }

        internal void Set(int slotOffset, object? value)
        {
            int phys = LogicalToGroupPhysical(_currentGroup);
            int slotPhys = LogicalToSlotPhysical(_groups[phys].DataAnchor + slotOffset);
            _slots[slotPhys] = value;
        }

        internal int CurrentParentGroup => _grpStackPtr > 0 ? _grpStack[_grpStackPtr - 1] : -1;

        internal void SetParentGroupKey(int key)
        {
            int parentLogical = CurrentParentGroup;
            if (parentLogical < 0) return;
            int phys = LogicalToGroupPhysical(parentLogical);
            var g = _groups[phys];
            g.Key = key;
            _groups[phys] = g;
        }

        internal object? PeekSlot(int slotOffset)
        {
            int phys = LogicalToGroupPhysical(_currentGroup);
            int idx = _groups[phys].DataAnchor + slotOffset;
            if (idx < _logicalSlotCount)
            {
                int slotPhys = LogicalToSlotPhysical(idx);
                return _slots[slotPhys];
            }
            return null;
        }

        internal object? UpdateParent(object? value)
        {
            int parentLogical = CurrentParentGroup;
            if (parentLogical < 0) return null;

            int phys = LogicalToGroupPhysical(parentLogical);
            var g = _groups[phys];
            int targetSlot = g.IsNode ? g.DataAnchor + 1 : g.DataAnchor;

            if (targetSlot >= _logicalSlotCount)
            {
                MoveSlotGapTo(targetSlot, parentLogical);
                InsertSlots(1);
            }

            int slotPhys = LogicalToSlotPhysical(targetSlot);
            object? prev = _slots[slotPhys];
            _slots[slotPhys] = value;

            _currentSlot = targetSlot + 1;
            if (_currentSlot > _currentSlotEnd)
                _currentSlotEnd = _currentSlot;

            return prev;
        }

        internal object? UpdateParentAt(object? value, int slotOffset)
        {
            int parentLogical = CurrentParentGroup;
            if (parentLogical < 0) return null;

            int phys = LogicalToGroupPhysical(parentLogical);
            var g = _groups[phys];
            int targetSlot = (g.IsNode ? g.DataAnchor + 1 : g.DataAnchor) + slotOffset;

            while (targetSlot >= _logicalSlotCount)
            {
                MoveSlotGapTo(targetSlot, parentLogical);
                InsertSlots(1);
            }

            int slotPhys = LogicalToSlotPhysical(targetSlot);
            object? prev = _slots[slotPhys];
            _slots[slotPhys] = value;

            _currentSlot = targetSlot + 1;
            if (_currentSlot > _currentSlotEnd)
                _currentSlotEnd = _currentSlot;

            return prev;
        }

        internal object? PeekParentSlot(int slotOffset)
        {
            int parentLogical = CurrentParentGroup;
            if (parentLogical < 0) return null;

            int phys = LogicalToGroupPhysical(parentLogical);
            int idx = _groups[phys].DataAnchor + slotOffset;
            if (idx < _logicalSlotCount)
            {
                int slotPhys = LogicalToSlotPhysical(idx);
                return _slots[slotPhys];
            }
            return null;
        }

        internal GapAnchor Anchor(int index = -1)
        {
            int idx = index >= 0 ? index : Math.Min(_currentGroup, Math.Max(0, _logicalGroupCount - 1));
            int location = idx - _logicalGroupCount;
            var anchor = new GapAnchor(location);
            _anchors.Add(anchor);
            return anchor;
        }

        internal int AnchorIndex(GapAnchor a)
        {
            return GapAnchorToLogical(a);
        }

        internal void Seek(GapAnchor anchor)
        {
            int idx = GapAnchorToLogical(anchor);
            if (idx < 0) throw new InvalidOperationException("Anchor is invalid");
            _currentGroup = idx;
            _currentGroupEnd = _logicalGroupCount;
            int phys = LogicalToGroupPhysical(idx);
            var g = _groups[phys];
            _currentSlot = g.DataAnchor;
            _currentSlotEnd = _currentSlot + SlotCount(g);
        }

        internal void AdvanceBy(int amount)
        {
            _currentGroup += amount;
            if (_currentGroup > _logicalGroupCount)
                _currentGroup = _logicalGroupCount;
        }

        internal void RemoveGroup()
        {
            int phys = LogicalToGroupPhysical(_currentGroup);
            var g = _groups[phys];
            int size = g.Size;

            MoveGroupGapTo(_currentGroup);

            InvalidateAnchorsInRange(_currentGroup, size);

            for (int i = _currentGroup; i < _currentGroup + size; i++)
            {
                int p = LogicalToGroupPhysical(i);
                _groups[p] = default;
            }

            _gapGroupStart = _currentGroup;
            _gapGroupLen += size;
            _logicalGroupCount -= size;

            _currentGroupEnd = _logicalGroupCount;
        }

        internal void MoveGroup(int offset)
        {
            if (offset == 0) return;
            int srcLogical = _currentGroup;
            int phys = LogicalToGroupPhysical(srcLogical);
            int gSize = _groups[phys].Size;
            int size = 1;

            var savedGroup = _groups[LogicalToGroupPhysical(srcLogical)];

            int firstChild = 1;
            if (_grpStackPtr > 0)
                firstChild = _grpStack[_grpStackPtr - 1] + 1;

            int dstLogical = srcLogical + offset;
            if (dstLogical + size > _logicalGroupCount)
                dstLogical = firstChild;
            else if (dstLogical < firstChild)
                dstLogical = _logicalGroupCount - size;
            if (dstLogical == srcLogical) return;

            int farEnd = Math.Max(srcLogical + size, dstLogical + size);
            if (farEnd > _logicalGroupCount)
                farEnd = _logicalGroupCount;
            MoveGroupGapTo(farEnd);

            if (dstLogical > srcLogical)
            {
                int middleCount = dstLogical - srcLogical;
                Array.Copy(_groups, srcLogical + size, _groups, srcLogical, middleCount);
            }
            else
            {
                int middleCount = srcLogical - dstLogical;
                Array.Copy(_groups, dstLogical, _groups, dstLogical + size, middleCount);
            }

            savedGroup.ParentAnchor = EncodeParentAnchor(dstLogical);
            _groups[dstLogical] = savedGroup;

            int rangeStart = Math.Min(srcLogical, dstLogical);
            int rangeEnd = Math.Max(srcLogical + size, dstLogical + size);
            for (int i = rangeStart; i < rangeEnd; i++)
            {
                if (i == dstLogical) continue;
                var g = _groups[i];
                g.ParentAnchor = EncodeParentAnchor(i);
                _groups[i] = g;
            }

            _currentGroup = dstLogical;
        }

        internal void AppendSlot(GapAnchor anchor, object? value)
        {
            int idx = GapAnchorToLogical(anchor);
            if (idx < 0) return;
            int phys = LogicalToGroupPhysical(idx);
            int slotCount = SlotCount(_groups[phys]);
            int anchorSlot = _groups[phys].DataAnchor + slotCount;

            MoveSlotGapTo(anchorSlot, idx);
            InsertSlots(1);

            int slotPhys = LogicalToSlotPhysical(anchorSlot);
            _slots[slotPhys] = value;
        }

        internal void TrimTailSlots(int count)
        {
            if (count <= 0) return;
            int newSlotCount = _logicalSlotCount - count;
            if (newSlotCount < _currentSlot)
                _currentSlot = newSlotCount;
            _logicalSlotCount = newSlotCount;
            if (_gapSlotsStart > _logicalSlotCount)
                _gapSlotsStart = _logicalSlotCount;
        }

        private int EncodeParentAnchor(int logicalIndex)
        {
            if (logicalIndex < _gapGroupStart)
                return logicalIndex;
            int size = _logicalGroupCount - _gapGroupLen;
            return -(size - logicalIndex - ParentAnchorPivot);
        }

        internal void StartGroupExt(int key, GroupFlags flags, int slotCount)
        {
            PushState(_currentGroup, _currentGroupEnd, _currentSlot, _currentSlotEnd,
                      _nodeCount, _insertCount);

            InsertGroups(1);
            if (slotCount > 0)
                InsertSlots(slotCount);

            int idx = _currentGroup;
            int phys = LogicalToGroupPhysical(idx);
            _groups[phys] = new GroupRecord
            {
                Key = key,
                Flags = flags,
                Size = 1,
                ParentAnchor = EncodeParentAnchor(idx),
                DataAnchor = _currentSlot,
            };

            _insertCount = 0;
            _nodeCount = 0;
            _currentGroup = idx + 1;
            _currentGroupEnd = _logicalGroupCount;
            if (slotCount > 0)
            {
                _currentSlot += slotCount;
                _currentSlotEnd = _currentSlot;
            }
        }

        internal void EnsureStarted(int index)
        {
            if (index >= _logicalGroupCount)
            {
                PushState(_currentGroup, _currentGroupEnd, _currentSlot, _currentSlotEnd,
                          _nodeCount, _insertCount);
                InsertGroups(1);
                _groups[LogicalToGroupPhysical(index)] = new GroupRecord
                {
                    Key = index,
                    Size = 1,
                    ParentAnchor = EncodeParentAnchor(index),
                    DataAnchor = _currentSlot,
                };
                _nodeCount = 0;
                _insertCount = 0;
                _currentGroup = index + 1;
                _currentGroupEnd = _logicalGroupCount;
                _currentSlotEnd = _currentSlot;
            }
            else
            {
                // Reposition writer to the group at index (existing group)
                PushState(_currentGroup, _currentGroupEnd, _currentSlot, _currentSlotEnd,
                          _nodeCount, _insertCount);
                int phys = LogicalToGroupPhysical(index);
                var g = _groups[phys];
                _currentGroup = index + 1;
                _currentGroupEnd = _logicalGroupCount;
                _currentSlot = g.DataAnchor;
                _currentSlotEnd = _currentSlot + SlotCount(g);
                _nodeCount = 0;
                _insertCount = 1;
            }
        }

        internal void EnsureStarted(GapAnchor anchor)
        {
            int idx = GapAnchorToLogical(anchor);
            if (idx >= 0)
                EnsureStarted(idx);
        }

        internal void SkipToGroupEnd()
        {
            int phys = LogicalToGroupPhysical(_currentGroup);
            int size = _groups[phys].Size;
            _currentGroup += size;
            if (_currentGroup > _logicalGroupCount)
                _currentGroup = _logicalGroupCount;
        }

        internal void Reset()
        {
            MoveGroupGapTo(_logicalGroupCount);
            MoveSlotGapTo(_logicalSlotCount, -1);
            _currentGroup = 1;
            _currentGroupEnd = _logicalGroupCount;
            var g0 = _groups[0];
            _currentSlot = g0.DataAnchor;
            _currentSlotEnd = _currentSlot + SlotCount(g0);
            _nodeCount = 1;
            _insertCount = 0;
        }

        internal void BeginInsert()
        {
            _insertCount = 0;
            _nodeCount = 0;
        }

        internal void MoveFrom(SlotTable sourceTable, int sourceIndex, bool removeFromSource)
        {
            int groupCount = sourceTable.GroupSize(sourceIndex);
            if (groupCount <= 0) return;

            InsertGroups(groupCount);

            sourceTable.Read<int>(reader =>
            {
                CopyGroupsFromReader(reader, sourceIndex, groupCount);
                return 0;
            });

            if (removeFromSource)
            {
                sourceTable.Write<int>(writer =>
                {
                    writer.SeekToGroup(sourceIndex);
                    writer.RemoveGroup();
                    return 0;
                });
            }
        }

        private void CopyGroupsFromReader(SlotReader reader, int startIndex, int groupCount)
        {
            for (int i = 0; i < groupCount; i++)
            {
                reader.Reposition(startIndex + i);

                int phys = LogicalToGroupPhysical(_currentGroup + i);
                int sc = reader.CurrentSlotEnd - reader.CurrentSlot;
                if (sc > 0)
                    InsertSlots(sc);

                int key = reader.GroupKey;
                var flags = reader.GroupFlags;
                int nc = reader.NodeCount;
                int size = reader.GroupSize;

                _groups[phys] = new GroupRecord
                {
                    Key = key,
                    Flags = flags,
                    Size = size,
                    ParentAnchor = EncodeParentAnchor(_currentGroup + i),
                    DataAnchor = _currentSlot,
                    NodeCount = nc,
                };

                for (int s = 0; s < sc; s++)
                {
                    int slotPhys = LogicalToSlotPhysical(_currentSlot + s);
                    _slots[slotPhys] = reader.Next();
                }
                _currentSlot += sc;
            }
        }

        internal SlotTable ExtractGroup()
        {
            int index = _currentGroup;
            int phys = LogicalToGroupPhysical(index);
            var g = _groups[phys];
            int size = g.Size;

            var table = new SlotTable();
            table.Write<int>(writer =>
            {
                CopyGroupToWriter(writer, index, g);
                return 0;
            });

            return table;
        }

        private void CopyGroupToWriter(SlotWriter writer, int logicalIdx, GroupRecord g)
        {
            writer.StartGroupExt(g.Key, g.Flags, SlotCount(g));

            int sc = SlotCount(g);
            for (int s = 0; s < sc; s++)
            {
                int slotPhys = LogicalToSlotPhysical(g.DataAnchor + s);
                writer.Update(_slots[slotPhys]);
            }

            for (int i = 1; i < g.Size; i++)
            {
                int childIdx = logicalIdx + i;
                int childPhys = LogicalToGroupPhysical(childIdx);
                var childG = _groups[childPhys];
                CopyGroupToWriter(writer, childIdx, childG);
            }

            writer.EndGroup();
        }

        internal void SetGroupFlags(GroupFlags flags)
        {
            int phys = LogicalToGroupPhysical(_currentGroup);
            var g = _groups[phys];
            g.Flags |= flags;
            _groups[phys] = g;
        }

        internal void SeekToGroup(int logicalIndex)
        {
            if (logicalIndex < 0 || logicalIndex >= _logicalGroupCount)
                throw new ArgumentOutOfRangeException(nameof(logicalIndex));
            _currentGroup = logicalIndex;
            _currentGroupEnd = _logicalGroupCount;
            int phys = LogicalToGroupPhysical(logicalIndex);
            var g = _groups[phys];
            _currentSlot = g.DataAnchor;
            _currentSlotEnd = _currentSlot + SlotCount(g);
        }

        internal void EndInsert()
        {
        }

        internal void UpdateNode(GapAnchor anchor, object? node)
        {
            int idx = GapAnchorToLogical(anchor);
            if (idx < 0) return;
            int phys = LogicalToGroupPhysical(idx);
            var g = _groups[phys];
            int slotPhys = LogicalToSlotPhysical(g.DataAnchor + 1);
            if (slotPhys < _logicalSlotCount)
                _slots[slotPhys] = node;
        }

        internal object? Node(GapAnchor anchor)
        {
            int idx = GapAnchorToLogical(anchor);
            if (idx < 0) return null;
            int phys = LogicalToGroupPhysical(idx);
            var g = _groups[phys];
            int slotPhys = LogicalToSlotPhysical(g.DataAnchor + 1);
            if (slotPhys < _logicalSlotCount)
                return _slots[slotPhys];
            return null;
        }

        internal void RemoveCurrentGroup(RememberManager rememberManager)
        {
            RemoveGroup();
        }

        internal void DeactivateCurrentGroup(RememberManager rememberManager)
        {
            RemoveGroup();
        }
    }
}
