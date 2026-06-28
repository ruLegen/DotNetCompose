using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.Composer
{
    internal class ComposerChangeListWriter
    {
        private const int InvalidGroupLocation = -2;

        private readonly GapComposer _composer;
        private readonly SlotReader _reader;
        private bool _startedGroup;
        private readonly Stack<int> _startedGroups = new Stack<int>();
        private int _writersReaderDelta;
        private int _pendingUps;
        private readonly Stack<object?> _pendingDownNodes = new Stack<object?>();
        private int _removeFrom = -1;
        private int _moveFrom = -1;
        private int _moveTo = -1;
        private int _moveCount = 0;

        public ComposerChangeListWriter(GapComposer composer, ChangeList changeList, SlotReader reader)
        {
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            ChangeList = changeList ?? throw new ArgumentNullException(nameof(changeList));
        }

        public ChangeList ChangeList { get; set; }

        public bool ImplicitRootStart { get; set; } = true;

        public bool PastParent => _reader.Parent - _writersReaderDelta < 0;

        private void PushApplierOperationPreamble()
        {
            PushPendingUpsAndDowns();
        }

        private void PushSlotEditingOperationPreamble()
        {
            RealizeOperationLocation();
            RecordSlotEditing();
        }

        private void PushSlotTableOperationPreamble(bool useParentSlot = false)
        {
            if (useParentSlot)
            {
                EnsureRootStarted();

                var location = _reader.Parent;
                if (location > 0 && PeekOr(_startedGroups, InvalidGroupLocation) != location)
                {
                    var anchor = _reader.Anchor(location);
                    _startedGroups.Push(location);
                    ChangeList.PushEnsureGroupStarted(anchor);
                }
            }
            RealizeOperationLocation(useParentSlot);
        }

        public void MoveReaderRelativeTo(int location)
        {
            _writersReaderDelta += location - _reader.CurrentGroup;
        }

        public void MoveReaderToAbsolute(int location)
        {
            _writersReaderDelta = location;
        }

        public void RecordSlotEditing()
        {
            if (_reader.Size > 0 && _reader.CurrentGroup < _reader.Size)
            {
                var location = _reader.Parent;

                if (PeekOr(_startedGroups, InvalidGroupLocation) != location)
                {
                    EnsureRootStarted();

                    if (location > 0)
                    {
                        var anchor = _reader.Anchor(location);
                        _startedGroups.Push(location);
                        EnsureGroupStarted(anchor);
                    }
                }
            }
        }

        internal void EnsureRootStarted()
        {
            if (!_startedGroup && ImplicitRootStart)
            {
                ChangeList.PushEnsureRootStarted();
                _startedGroup = true;
            }
        }

        internal void EnsureGroupStarted(GapAnchor anchor)
        {
            ChangeList.PushEnsureGroupStarted(anchor);
            _startedGroup = true;
        }

        private void RealizeOperationLocation(bool forParent = false)
        {
            var location = forParent ? _reader.Parent : _reader.CurrentGroup;
            var distance = location - _writersReaderDelta;
            if (distance < 0)
                throw new InvalidOperationException("Tried to seek backward");
            if (distance > 0)
            {
                ChangeList.PushAdvanceSlotsBy(distance);
                _writersReaderDelta = location;
            }
        }

        public void WithChangeList(ChangeList newChangeList, Action block)
        {
            var previousChangeList = ChangeList;
            try
            {
                ChangeList = newChangeList;
                block();
            }
            finally
            {
                ChangeList = previousChangeList;
            }
        }

        public void WithoutImplicitRootStart(Action block)
        {
            var previousImplicitRootStart = ImplicitRootStart;
            try
            {
                ImplicitRootStart = false;
                block();
            }
            finally
            {
                ImplicitRootStart = previousImplicitRootStart;
            }
        }

        public void Remember(RememberObserverHolder value)
        {
            ChangeList.PushRemember(value);
        }

        public void RememberPausingScope(RecomposeScopeImpl scope)
        {
            ChangeList.PushRememberPausingScope(scope);
        }

        public void StartResumingScope(RecomposeScopeImpl scope)
        {
            ChangeList.PushStartResumingScope(scope);
        }

        public void EndResumingScope(RecomposeScopeImpl scope)
        {
            ChangeList.PushEndResumingScope(scope);
        }

        public void UpdateValue(object? value, int groupSlotIndex)
        {
            PushSlotTableOperationPreamble(useParentSlot: true);
            ChangeList.PushUpdateValue(value, groupSlotIndex);
        }

        public void UpdateAnchoredValue(object? value, GapAnchor anchor, int groupSlotIndex)
        {
            ChangeList.PushUpdateAnchoredValue(value, anchor, groupSlotIndex);
        }

        public void AppendValue(GapAnchor anchor, object? value)
        {
            ChangeList.PushAppendValue(anchor, value);
        }

        public void TrimValues(int count)
        {
            if (count > 0)
            {
                PushSlotEditingOperationPreamble();
                ChangeList.PushTrimValues(count);
            }
        }

        public void ResetSlots()
        {
            ChangeList.PushResetSlots();
        }

        public void UpdateAuxData(object? data)
        {
            PushSlotTableOperationPreamble();
            ChangeList.PushUpdateAuxData(data);
        }

        public void SetGroupFlags(GroupFlags flags)
        {
            PushSlotEditingOperationPreamble();
            ChangeList.PushSetGroupFlags(flags);
        }

        public void EndRoot()
        {
            if (_startedGroup)
            {
                PushSlotTableOperationPreamble();
                PushSlotTableOperationPreamble();
                ChangeList.PushEndCurrentGroup();
                _startedGroup = false;
            }
        }

        public void EndCurrentGroup()
        {
            var location = _reader.Parent;
            var currentStartedGroup = PeekOr(_startedGroups, -1);
            if (currentStartedGroup > location)
                throw new InvalidOperationException("Missed recording an endGroup");
            if (PeekOr(_startedGroups, -1) == location)
            {
                PushSlotTableOperationPreamble();
                _startedGroups.Pop();
                ChangeList.PushEndCurrentGroup();
            }
        }

        public void StartGroup(int key)
        {
            UpdateValue(key, 0);
            ChangeList.PushSetParentGroupKey(key);
        }

        public void StartNode(int key, object? node)
        {
            ChangeList.PushStartNode(key, node, null);
        }

        public void StartNodeReuse(int key, object? node)
        {
            ChangeList.PushStartNodeReuse(key, node, null);
        }

        public void SkipToEndOfCurrentGroup()
        {
            ChangeList.PushSkipToEndOfCurrentGroup();
        }

        public void RemoveCurrentGroup()
        {
            PushSlotEditingOperationPreamble();
            ChangeList.PushRemoveCurrentGroup();
            _writersReaderDelta += _reader.GroupSize;
        }

        public void InsertSlots(GapAnchor anchor, SlotTable from)
        {
            PushPendingUpsAndDowns();
            PushSlotEditingOperationPreamble();
            RealizeNodeMovementOperations();
            ChangeList.PushInsertSlots(anchor, from);
        }

        public void InsertSlots(GapAnchor anchor, SlotTable from, FixupList fixups)
        {
            PushPendingUpsAndDowns();
            PushSlotEditingOperationPreamble();
            RealizeNodeMovementOperations();
            ChangeList.PushInsertSlots(anchor, from, fixups);
        }

        public void MoveCurrentGroup(int offset)
        {
            PushSlotEditingOperationPreamble();
            ChangeList.PushMoveCurrentGroup(offset);
        }

        public void EndCompositionScope(Action<IComposition>? action, IComposition? composition)
        {
            ChangeList.PushEndCompositionScope(action, composition);
        }

        public void UseNode(object? node)
        {
            PushApplierOperationPreamble();
            ChangeList.PushUseNode();
        }

        public void UpdateNode<T, V>(V value, Action<T, V> block)
        {
            PushApplierOperationPreamble();
            ChangeList.PushUpdateNode(value, (object curr, object? val) => block((T)curr, (V)val!));
        }

        public void RemoveNode(int nodeIndex, int count)
        {
            if (count > 0)
            {
                if (nodeIndex < 0)
                    throw new InvalidOperationException($"Invalid remove index {nodeIndex}");
                if (_removeFrom == nodeIndex)
                {
                    _moveCount += count;
                }
                else
                {
                    RealizeNodeMovementOperations();
                    _removeFrom = nodeIndex;
                    _moveCount = count;
                }
            }
        }

        public void MoveNode(int from, int to, int count)
        {
            if (count > 0)
            {
                if (_moveCount > 0 && _moveFrom == from - _moveCount && _moveTo == to - _moveCount)
                {
                    _moveCount += count;
                }
                else
                {
                    RealizeNodeMovementOperations();
                    _moveFrom = from;
                    _moveTo = to;
                    _moveCount = count;
                }
            }
        }

        public void ReleaseMovableContent()
        {
            PushPendingUpsAndDowns();
            if (_startedGroup)
            {
                SkipToEndOfCurrentGroup();
                EndRoot();
            }
        }

        public void EndNodeMovement()
        {
            RealizeNodeMovementOperations();
        }

        public void EndNodeMovementAndDeleteNode(int nodeIndex, int group)
        {
            EndNodeMovement();
            PushPendingUpsAndDowns();
            int nodeCount = _reader.IsNodeAt(group) ? 1 : _reader.NodeCount;
            if (nodeCount > 0)
            {
                RemoveNode(nodeIndex, nodeCount);
            }
        }

        private void RealizeNodeMovementOperations()
        {
            if (_moveCount > 0)
            {
                if (_removeFrom >= 0)
                {
                    RealizeRemoveNode(_removeFrom, _moveCount);
                    _removeFrom = -1;
                }
                else
                {
                    RealizeMoveNode(_moveTo, _moveFrom, _moveCount);
                    _moveFrom = -1;
                    _moveTo = -1;
                }
                _moveCount = 0;
            }
        }

        private void RealizeRemoveNode(int removeFrom, int moveCount)
        {
            PushApplierOperationPreamble();
            ChangeList.PushRemoveNode(removeFrom, moveCount);
        }

        private void RealizeMoveNode(int to, int from, int count)
        {
            PushApplierOperationPreamble();
            ChangeList.PushMoveNode(to, from, count);
        }

        public void MoveUp()
        {
            RealizeNodeMovementOperations();
            if (_pendingDownNodes.Count > 0)
            {
                _pendingDownNodes.Pop();
            }
            else
            {
                _pendingUps++;
            }
        }

        public void MoveDown(object? node)
        {
            RealizeNodeMovementOperations();
            _pendingDownNodes.Push(node);
        }

        private void PushPendingUpsAndDowns()
        {
            if (_pendingUps > 0)
            {
                ChangeList.PushUps(_pendingUps);
                _pendingUps = 0;
            }

            if (_pendingDownNodes.Count > 0)
            {
                var nodes = _pendingDownNodes.ToArray();
                ChangeList.PushDowns(nodes);
                _pendingDownNodes.Clear();
            }
        }

        public void SideEffect(Action effect)
        {
            ChangeList.PushSideEffect(effect);
        }

        public void DetermineMovableContentNodeIndex(IntRef effectiveNodeIndexOut, GapAnchor anchor)
        {
            PushPendingUpsAndDowns();
            ChangeList.PushDetermineMovableContentNodeIndex(effectiveNodeIndexOut, anchor);
        }

        public void CopyNodesToNewAnchorLocation(object?[] nodes, IntRef effectiveNodeIndex)
        {
            ChangeList.PushCopyNodesToNewAnchorLocation(nodes, effectiveNodeIndex);
        }

        public void CopySlotTableToAnchorLocation(
            MovableContentState? resolvedState,
            CompositionContext parentContext,
            MovableContentStateReference from,
            MovableContentStateReference to)
        {
            ChangeList.PushCopySlotTableToAnchorLocation(resolvedState, parentContext, from, to);
        }

        public void ReleaseMovableGroupAtCurrent(
            IControlledComposition composition,
            CompositionContext parentContext,
            MovableContentStateReference reference)
        {
            ChangeList.PushReleaseMovableGroupAtCurrent(composition, parentContext, reference);
        }

        public void EndMovableContentPlacement()
        {
            PushPendingUpsAndDowns();
            ChangeList.PushEndMovableContentPlacement();
            _writersReaderDelta = 0;
        }

        public void IncludeOperationsIn(ChangeList other, IntRef? effectiveNodeIndex = null)
        {
            ChangeList.PushExecuteOperationsIn(other, effectiveNodeIndex);
        }

        public void FinalizeComposition()
        {
            PushPendingUpsAndDowns();
            if (_startedGroups.Count > 0)
                throw new InvalidOperationException("Missed recording an endGroup()");
        }

        public void ResetTransientState()
        {
            _startedGroup = false;
            _startedGroups.Clear();
            _writersReaderDelta = 0;
            ImplicitRootStart = true;
            _pendingUps = 0;
            _pendingDownNodes.Clear();
            _removeFrom = -1;
            _moveFrom = -1;
            _moveTo = -1;
            _moveCount = 0;
        }

        public void DeactivateCurrentGroup()
        {
            PushSlotTableOperationPreamble();
            ChangeList.PushDeactivateCurrentGroup();
        }

        private static T PeekOr<T>(Stack<T> stack, T defaultValue)
        {
            return stack.Count > 0 ? stack.Peek() : defaultValue;
        }

        // temporary diagnostic
    }
}
