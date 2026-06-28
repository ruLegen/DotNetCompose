using System;
using DotNetCompose.Runtime;

namespace DotNetCompose.Runtime.Composer
{
    internal class ChangeList : Changes
    {
        private readonly Operations _operations = new Operations();

        public int Size => _operations.Size;

        public override bool IsEmpty() => _operations.IsEmpty;

        public override void Clear() => _operations.Clear();

        public override void Execute(
            SlotTable slotStorage,
            IApplier<object> applier,
            RememberManager rememberManager)
        {
            if (IsEmpty()) return;
            slotStorage.Write<object?>(slots =>
            {
                Drain(applier, slots, rememberManager);
                return null;
            });
        }

        internal void Drain(IApplier<object> applier, SlotWriter writer, RememberManager rememberManager)
        {
            _operations.Drain(applier, writer, rememberManager);
        }

        public void PushEnsureRootStarted() =>
            _operations.Push(Operation.EnsureRootGroupStarted);

        public void PushEnsureGroupStarted(GapAnchor anchor) =>
            _operations.Push(Operation.EnsureGroupStarted, w => w.SetObject(0, anchor));

        public void PushEndCurrentGroup() =>
            _operations.Push(Operation.EndCurrentGroup);

        public void PushSetParentGroupKey(int key) =>
            _operations.Push(Operation.SetParentGroupKey, w => w.SetInt(0, key));

        public void PushSkipToEndOfCurrentGroup() =>
            _operations.Push(Operation.SkipToEndOfCurrentGroup);

        public void PushRemoveCurrentGroup() =>
            _operations.Push(Operation.RemoveCurrentGroup);

        public void PushDeactivateCurrentGroup() =>
            _operations.Push(Operation.DeactivateCurrentGroup);

        public void PushResetSlots() =>
            _operations.Push(Operation.ResetSlots);

        public void PushSetGroupFlags(GroupFlags flags) =>
            _operations.Push(Operation.SetGroupFlags, w => w.SetInt(0, (int)flags));

        public void PushUpdateValue(object? value, int groupSlotIndex) =>
            _operations.Push(Operation.UpdateValue, w =>
            {
                w.SetInt(0, groupSlotIndex);
                w.SetObject(0, value);
            });

        public void PushAppendValue(GapAnchor anchor, object? value) =>
            _operations.Push(Operation.AppendValue, w =>
            {
                w.SetObject(0, anchor);
                w.SetObject(1, value);
            });

        public void PushTrimValues(int count) =>
            _operations.Push(Operation.TrimParentValues, w => w.SetInt(0, count));

        public void PushUpdateAuxData(object? data) =>
            _operations.Push(Operation.UpdateAuxData, w => w.SetObject(0, data));

        public void PushInsertSlots(GapAnchor anchor, SlotTable from) =>
            _operations.Push(Operation.InsertSlots, w =>
            {
                w.SetObject(0, from);
                w.SetObject(1, anchor);
            });

        public void PushMoveCurrentGroup(int offset) =>
            _operations.Push(Operation.MoveCurrentGroup, w => w.SetInt(0, offset));

        public void PushUseNode() =>
            _operations.Push(Operation.UseCurrentNode);

        public void PushUpdateNode(object? value, Action<object, object?> block) =>
            _operations.Push(Operation.UpdateNode, w =>
            {
                w.SetObject(0, value);
                w.SetObject(1, block);
            });

        public void PushRemoveNode(int removeIndex, int count) =>
            _operations.Push(Operation.RemoveNode, w =>
            {
                w.SetInt(0, removeIndex);
                w.SetInt(1, count);
            });

        public void PushMoveNode(int from, int to, int count) =>
            _operations.Push(Operation.MoveNode, w =>
            {
                w.SetInt(0, from);
                w.SetInt(1, to);
                w.SetInt(2, count);
            });

        public void PushAdvanceSlotsBy(int distance) =>
            _operations.Push(Operation.AdvanceSlotsBy, w => w.SetInt(0, distance));

        public void PushUps(int count) =>
            _operations.Push(Operation.Ups, w => w.SetInt(0, count));

        public void PushDowns(Array nodes)
        {
            if (nodes.Length > 0)
                _operations.Push(Operation.Downs, w => w.SetObject(0, nodes));
        }

        public void PushSideEffect(Action effect) =>
            _operations.Push(Operation.SideEffect, w => w.SetObject(0, effect));

        public void PushRemember(object value) =>
            _operations.Push(Operation.Remember, w => w.SetObject(0, value));

        public void PushRememberPausingScope(object scope) =>
            _operations.Push(Operation.RememberPausingScope, w => w.SetObject(0, scope));

        public void PushStartResumingScope(object scope) =>
            _operations.Push(Operation.StartResumingScope, w => w.SetObject(0, scope));

        public void PushEndResumingScope(object scope) =>
            _operations.Push(Operation.EndResumingScope, w => w.SetObject(0, scope));

        public void PushApplyChangeList(Changes childChanges) =>
            _operations.Push(Operation.ApplyChangeList, w => w.SetObject(0, childChanges));

        public void PushUpdateAnchoredValue(object? value, GapAnchor anchor, int groupSlotIndex) =>
            _operations.Push(Operation.UpdateAnchoredValue, w =>
            {
                w.SetInt(0, groupSlotIndex);
                w.SetObject(0, value);
                w.SetObject(1, anchor);
            });

        public void PushInsertSlots(GapAnchor anchor, SlotTable from, FixupList fixups) =>
            _operations.Push(Operation.InsertSlotsWithFixups, w =>
            {
                w.SetObject(0, from);
                w.SetObject(1, anchor);
                w.SetObject(2, fixups.Operations);
            });

        public void PushEndCompositionScope(Action<IComposition>? action, IComposition? composition) =>
            _operations.Push(Operation.EndCompositionScope, w =>
            {
                w.SetObject(0, action);
                w.SetObject(1, composition);
            });

        public void PushDetermineMovableContentNodeIndex(IntRef effectiveNodeIndexOut, GapAnchor anchor) =>
            _operations.Push(Operation.DetermineMovableContentNodeIndex, w =>
            {
                w.SetObject(0, effectiveNodeIndexOut);
                w.SetObject(1, anchor);
            });

        public void PushCopyNodesToNewAnchorLocation(object?[] nodes, IntRef effectiveNodeIndex) =>
            _operations.Push(Operation.CopyNodesToNewAnchorLocation, w =>
            {
                w.SetObject(0, nodes);
                w.SetObject(1, effectiveNodeIndex);
            });

        public void PushCopySlotTableToAnchorLocation(
            MovableContentState? resolvedState,
            CompositionContext parentContext,
            MovableContentStateReference from,
            MovableContentStateReference to) =>
            _operations.Push(Operation.CopySlotTableToAnchorLocation, w =>
            {
                w.SetObject(0, resolvedState);
                w.SetObject(1, parentContext);
                w.SetObject(2, from);
                w.SetObject(3, to);
            });

        public void PushReleaseMovableGroupAtCurrent(
            IControlledComposition composition,
            CompositionContext parentContext,
            MovableContentStateReference reference) =>
            _operations.Push(Operation.ReleaseMovableGroupAtCurrent, w =>
            {
                w.SetObject(0, composition);
                w.SetObject(1, parentContext);
                w.SetObject(2, reference);
            });

        public void PushEndMovableContentPlacement() =>
            _operations.Push(Operation.EndMovableContentPlacement);

        public void PushExecuteOperationsIn(ChangeList changeList, IntRef? effectiveNodeIndex = null) =>
            _operations.Push(Operation.ExecuteOperationsIn, w =>
            {
                w.SetObject(0, changeList);
                w.SetObject(1, effectiveNodeIndex);
            });

        public void PushStartNode(int key, object? node, object? metadata) =>
            _operations.Push(Operation.StartNode, w =>
            {
                w.SetInt(0, key);
                w.SetObject(0, node);
                w.SetObject(1, metadata);
            });

        public void PushStartNodeReuse(int key, object? node, object? metadata) =>
            _operations.Push(Operation.StartNodeReuse, w =>
            {
                w.SetInt(0, key);
                w.SetObject(0, node);
                w.SetObject(1, metadata);
            });
    }
}
