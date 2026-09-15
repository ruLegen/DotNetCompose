using DotNetCompose.Runtime.SlotTable.GapBuffer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace DotNetCompose.Runtime.SlotTable
{
    public partial class ComposerSlotTable
    {
        public sealed class Writer
        {
            private static readonly GroupRecord EmptyGroup = new();
            private static readonly object Empty = new();
            public Writer(ComposerSlotTable table)
            {
                Table = table;
                _groups = table._groups;
                _slots = table._slots;

                _currentGroupDataAnchor = _slots.InsertStable(0, null);
            }

            public readonly ComposerSlotTable Table;
            private readonly SlotMapGapBuffer<GroupRecord> _groups;
            private readonly SlotMapGapBuffer<object?> _slots;

            private readonly Stack<GapBufferItemAnchor> _groupStacks = new Stack<GapBufferItemAnchor>();
            private readonly Stack<GapBufferItemAnchor> _slotStartsStack = new Stack<GapBufferItemAnchor>();
            private readonly Stack<int> _slotWriteCountStack = new Stack<int>();

            private GapBufferItemAnchor _currentGroupDataAnchor;
            private int _currentGroupSlotSize = 0;
            private int _totalSlotInsertion = 0;

            //Group management operations
            public void StartGroup() => StartGroup(0, Empty, Empty, false);
            public void StartGroup(int key) => StartGroup(key, Empty, Empty, false);
            public void StartGroup(int key, object? objectKey) => StartGroup(key, objectKey, Empty, false);
            public void StartNode(int key, object? node) => StartGroup(key, Empty, node, true);

            private void StartGroup(int key, object? objectKey, object? auxData, bool isNode)
            {
                int minSlotNeeded = 0;
                bool hasAux = auxData != Empty;
                bool hasObjectKey = objectKey != Empty;
                GroupFlags flags = GroupFlags.None;
                if (isNode)
                {
                    flags |= GroupFlags.Node;
                    minSlotNeeded++;
                }
                if (hasObjectKey)
                {
                    flags |= GroupFlags.ObjectKey;
                    minSlotNeeded++;
                }
                if (hasAux)
                {
                    flags |= GroupFlags.Aux;
                    minSlotNeeded++;
                }

                if (!_groupStacks.TryPeek(out GapBufferItemAnchor parentGroupAnchor))
                    parentGroupAnchor = GapBufferItemAnchor.Empty;

                GroupRecord groupRecord = new()
                {
                    Key = key,
                    Flags = flags,
                    Size = 1,
                    NodeCount = isNode ? 1 : 0,
                    ParentAnchor = parentGroupAnchor,
                    DataAnchor = GapBufferItemAnchor.Empty, // Set later
                };

                _slotWriteCountStack.Push(_currentGroupSlotSize);

                int slotStartIndex = _totalSlotInsertion;
                _currentGroupSlotSize = 0;

                if (minSlotNeeded > 0)
                {
                    if (isNode) _slots.Insert(slotStartIndex + (_currentGroupSlotSize++), auxData);
                    if (hasObjectKey) _slots.Insert(slotStartIndex + (_currentGroupSlotSize++), objectKey);
                    if (hasAux) _slots.Insert(slotStartIndex + (_currentGroupSlotSize++), auxData);

                    _totalSlotInsertion += _currentGroupSlotSize;
                }

                groupRecord.DataAnchor = _slots.Track(_slots.GetAddressOfIndex(slotStartIndex));

                GapBufferItemAnchor addedGroupAnchor = _groups.InsertStable(_groups.Count, groupRecord);
                _groupStacks.Push(addedGroupAnchor);
            }
            public void EndGroup()
            {
                GapBufferItemAnchor openedGroupAnchor = _groupStacks.Pop();
                ref GroupRecord currentGroup = ref _groups.GetRef(openedGroupAnchor);
                GapBufferItemAnchor parentGroupAnchor = currentGroup.ParentAnchor;
                if (parentGroupAnchor != GapBufferItemAnchor.Empty)
                {
                    ref GroupRecord parentGroup = ref _groups.GetRef(parentGroupAnchor);
                    parentGroup.Size += currentGroup.Size;
                    parentGroup.NodeCount += currentGroup.NodeCount;
                }
                _currentGroupSlotSize = _slotWriteCountStack.Pop();
                // update NodeCount
                // update Size
            }
            public void SkipGroup() { }
            public void RemoveGroup() { }


            public void AppendSlot(object? value)
            {
                GapBufferItemAnchor openedGroupAnchor = _groupStacks.Peek();
                ref GroupRecord currentGroup = ref _groups.GetRef(openedGroupAnchor);
                GapBufferItemAnchor dataStartAnchor = currentGroup.DataAnchor;
                int insertIndex = _slots.GetIndexOfAnchor(dataStartAnchor) + _currentGroupSlotSize;
                _slots.Insert(insertIndex, value);
                _currentGroupSlotSize++;
                _totalSlotInsertion++;
            }
            public void UpdateSlot(object? value)
            {
                GapBufferItemAnchor openedGroupAnchor = _groupStacks.Peek();
                ref GroupRecord currentGroup = ref _groups.GetRef(openedGroupAnchor);
                GapBufferItemAnchor dataStartAnchor = currentGroup.DataAnchor;
                int updateIndex = _slots.GetIndexOfAnchor(dataStartAnchor) + _currentGroupSlotSize-1;
                _slots.Set(_slots.GetAddressOfIndex(updateIndex), value);
            }

            //Slot/data operations
            //Navigation/traversal:
            //Node operations

            /////
            ///
            public void Close()
            {
                Table.CloseWriter(this);
                // Move Gap to the end
                // so reader could 
            }
        }
    }
}
