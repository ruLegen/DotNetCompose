using System;
using DotNetCompose.Runtime;

namespace DotNetCompose.Runtime.Composer
{
    internal abstract class Operation
    {
        public abstract int IntCount { get; }
        public abstract int ObjectCount { get; }

        public abstract void Execute(
            IApplier<object> applier,
            SlotWriter slots,
            RememberManager rememberManager,
            Func<int, int> getInt,
            Func<int, object?> getObject
        );

        // Singletons
        public static readonly Operation EnsureRootGroupStarted = new EnsureRootGroupStartedOp();
        public static readonly Operation EndCurrentGroup = new EndCurrentGroupOp();
        public static readonly Operation RemoveCurrentGroup = new RemoveCurrentGroupOp();
        public static readonly Operation SkipToEndOfCurrentGroup = new SkipToEndOfCurrentGroupOp();
        public static readonly Operation ResetSlots = new ResetSlotsOp();
        public static readonly Operation DeactivateCurrentGroup = new DeactivateCurrentGroupOp();
        public static readonly Operation UseCurrentNode = new UseCurrentNodeOp();
        public static readonly Operation UpdateNode = new UpdateNodeOp();
        public static readonly Operation EnsureGroupStarted = new EnsureGroupStartedOp();
        public static readonly Operation AppendValue = new AppendValueOp();
        public static readonly Operation UpdateAuxData = new UpdateAuxDataOp();
        public static readonly Operation InsertSlots = new InsertSlotsOp();
        public static readonly Operation InsertSlotsWithFixups = new InsertSlotsWithFixupsOp();
        public static readonly Operation InsertNodeFixup = new InsertNodeFixupOp();
        public static readonly Operation PostInsertNodeFixup = new PostInsertNodeFixupOp();
        public static readonly Operation SideEffect = new SideEffectOp();
        public static readonly Operation Remember = new RememberOp();
        public static readonly Operation RememberPausingScope = new RememberPausingScopeOp();
        public static readonly Operation StartResumingScope = new StartResumingScopeOp();
        public static readonly Operation EndResumingScope = new EndResumingScopeOp();
        public static readonly Operation ApplyChangeList = new ApplyChangeListOp();
        public static readonly Operation Downs = new DownsOp();
        public static readonly Operation Ups = UpsOp.Instance;
        public static readonly Operation AdvanceSlotsBy = AdvanceSlotsByOp.Instance;
        public static readonly Operation SetParentGroupKey = new SetParentGroupKeyOp();
        public static readonly Operation UpdateValue = UpdateValueOp.Instance;
        public static readonly Operation RemoveNode = RemoveNodeOp.Instance;
        public static readonly Operation MoveNode = MoveNodeOp.Instance;
        public static readonly Operation TrimParentValues = TrimParentValuesOp.Instance;
        public static readonly Operation MoveCurrentGroup = MoveCurrentGroupOp.Instance;
        public static readonly Operation UpdateAnchoredValue = UpdateAnchoredValueOp.Instance;
        public static readonly Operation EndCompositionScope = EndCompositionScopeOp.Instance;
        public static readonly Operation DetermineMovableContentNodeIndex = DetermineMovableContentNodeIndexOp.Instance;
        public static readonly Operation CopyNodesToNewAnchorLocation = CopyNodesToNewAnchorLocationOp.Instance;
        public static readonly Operation CopySlotTableToAnchorLocation = CopySlotTableToAnchorLocationOp.Instance;
        public static readonly Operation ReleaseMovableGroupAtCurrent = ReleaseMovableGroupAtCurrentOp.Instance;
        public static readonly Operation EndMovableContentPlacement = EndMovableContentPlacementOp.Instance;
        public static readonly Operation ExecuteOperationsIn = ExecuteOperationsInOp.Instance;
        public static readonly Operation SetGroupFlags = new SetGroupFlagsOp();

        public static readonly Operation StartNode = new StartNodeOp();
        public static readonly Operation StartNodeReuse = new StartNodeReuseOp();
    }

    sealed class EnsureRootGroupStartedOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.EnsureStarted(0);
        }
    }

    sealed class EnsureGroupStartedOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.EnsureStarted((GapAnchor)getObject(0)!);
        }
    }

    sealed class EndCurrentGroupOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.EndGroup();
        }
    }

    sealed class AdvanceSlotsByOp : Operation
    {
        public static readonly AdvanceSlotsByOp Instance = new();
        public override int IntCount => 1;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.AdvanceBy(getInt(0));
        }
    }

    sealed class DownsOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            var nodes = (Array)getObject(0)!;
            for (int i = 0; i < nodes.Length; i++)
                applier.Down((object)nodes.GetValue(i)!);
        }
    }

    sealed class UpsOp : Operation
    {
        public static readonly UpsOp Instance = new();
        public override int IntCount => 1;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            int count = getInt(0);
            for (int i = 0; i < count; i++)
                applier.Up();
        }
    }

    sealed class UpdateNodeOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            var value = getObject(0);
            var block = (Action<object, object?>)getObject(1)!;
            applier.Apply(block, value);
        }
    }

    sealed class RemoveNodeOp : Operation
    {
        public static readonly RemoveNodeOp Instance = new();
        public override int IntCount => 2;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            applier.Remove(getInt(0), getInt(1));
        }
    }

    sealed class MoveNodeOp : Operation
    {
        public static readonly MoveNodeOp Instance = new();
        public override int IntCount => 3;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            applier.Move(getInt(0), getInt(1), getInt(2));
        }
    }

    sealed class UpdateValueOp : Operation
    {
        public static readonly UpdateValueOp Instance = new();
        public override int IntCount => 1;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
            int slotOffset = getInt(0);
            slots.UpdateParentAt(getObject(0), slotOffset);
        }
    }

    sealed class SetParentGroupKeyOp : Operation
    {
        public override int IntCount => 1;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.SetParentGroupKey(getInt(0));
        }
    }

    sealed class AppendValueOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.AppendSlot((GapAnchor)getObject(0)!, getObject(1));
        }
    }

    sealed class TrimParentValuesOp : Operation
    {
        public static readonly TrimParentValuesOp Instance = new();
        public override int IntCount => 1;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.TrimTailSlots(getInt(0));
        }
    }

    sealed class RemoveCurrentGroupOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.RemoveCurrentGroup(rm);
        }
    }

    sealed class MoveCurrentGroupOp : Operation
    {
        public static readonly MoveCurrentGroupOp Instance = new();
        public override int IntCount => 1;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.MoveGroup(getInt(0));
        }
    }

    sealed class SkipToEndOfCurrentGroupOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.SkipToGroupEnd();
        }
    }

    sealed class ResetSlotsOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.Reset();
        }
    }

    sealed class InsertSlotsOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            var insertTable = (SlotTable)getObject(0)!;
            var anchor = (GapAnchor)getObject(1)!;
            slots.BeginInsert();
            slots.MoveFrom(insertTable, AnchorIndex(insertTable, anchor), false);
            slots.EndInsert();
        }

        private static int AnchorIndex(SlotTable table, GapAnchor a)
        {
            int physicalIdx = a.Location;
            if (physicalIdx < 0) return table.GroupsSize + physicalIdx;
            return physicalIdx;
        }
    }

    sealed class InsertSlotsWithFixupsOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 3;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            var insertTable = (SlotTable)getObject(0)!;
            var anchor = (GapAnchor)getObject(1)!;
            var fixups = (Operations)getObject(2)!;
            insertTable.Write<int>(writer =>
            {
                fixups.Drain(applier, writer, rm);
                return 0;
            });
            slots.BeginInsert();
            slots.MoveFrom(insertTable, AnchorIndex(insertTable, anchor), false);
            slots.EndInsert();
        }

        private static int AnchorIndex(SlotTable table, GapAnchor a)
        {
            if (a.Location < 0) return table.GroupsSize + a.Location;
            return a.Location;
        }
    }

    sealed class InsertNodeFixupOp : Operation
    {
        public override int IntCount => 1;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            var node = ((Func<object?>)getObject(0)!).Invoke();
            var groupAnchor = (GapAnchor)getObject(1)!;
            int insertIndex = getInt(0);
            slots.UpdateNode(groupAnchor, node);
            applier.InsertTopDown(insertIndex, node!);
            applier.Down(node!);
        }
    }

    sealed class PostInsertNodeFixupOp : Operation
    {
        public override int IntCount => 1;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            var groupAnchor = (GapAnchor)getObject(0)!;
            int insertIndex = getInt(0);
            applier.Up();
            var nodeToInsert = slots.Node(groupAnchor);
            applier.InsertBottomUp(insertIndex, nodeToInsert!);
        }
    }

    sealed class UpdateAuxDataOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.UpdateAux(getObject(0));
        }
    }

    sealed class DeactivateCurrentGroupOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.DeactivateCurrentGroup(rm);
        }
    }

    sealed class UseCurrentNodeOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            applier.Reuse();
        }
    }

    sealed class SideEffectOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            rm.SideEffect((Action)getObject(0)!);
        }
    }

    sealed class RememberOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            var obj = getObject(0);
            if (obj is RememberObserverHolder holder)
            {
                rm.Remember(holder.Observer);
            }
        }
    }

    sealed class RememberPausingScopeOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
        }
    }

    sealed class StartResumingScopeOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
        }
    }

    sealed class EndResumingScopeOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 1;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
        }
    }

    sealed class ApplyChangeListOp : Operation
    {
        public override int IntCount => 0;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
            var childChanges = (Changes)getObject(0)!;
            childChanges.Execute(null!, applier, rm);
        }
    }

    sealed class UpdateAnchoredValueOp : Operation
    {
        public static readonly UpdateAnchoredValueOp Instance = new();
        public override int IntCount => 1;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.UpdateParent(getObject(0));
        }
    }

    sealed class EndCompositionScopeOp : Operation
    {
        public static readonly EndCompositionScopeOp Instance = new();
        public override int IntCount => 0;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                      Func<int, int> getInt, Func<int, object?> getObject)
        {
        }
    }

    sealed class DetermineMovableContentNodeIndexOp : Operation
    {
        public static readonly DetermineMovableContentNodeIndexOp Instance = new();
        public override int IntCount => 0;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
        }
    }

    sealed class CopyNodesToNewAnchorLocationOp : Operation
    {
        public static readonly CopyNodesToNewAnchorLocationOp Instance = new();
        public override int IntCount => 0;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
        }
    }

    sealed class CopySlotTableToAnchorLocationOp : Operation
    {
        public static readonly CopySlotTableToAnchorLocationOp Instance = new();
        public override int IntCount => 0;
        public override int ObjectCount => 4;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
            var from = (MovableContentStateReference)getObject(2)!;
            if (from?.SlotStorage == null) return;

            slots.BeginInsert();
            slots.MoveFrom(from.SlotStorage, 1, false);
            slots.EndInsert();
        }
    }

    sealed class ReleaseMovableGroupAtCurrentOp : Operation
    {
        public static readonly ReleaseMovableGroupAtCurrentOp Instance = new();
        public override int IntCount => 0;
        public override int ObjectCount => 3;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
            var parentContext = (CompositionContext)getObject(1)!;
            var reference = (MovableContentStateReference)getObject(2)!;

            if (reference.Anchor != null && reference.Anchor.Valid)
            {
                slots.Seek(reference.Anchor);
                var table = slots.ExtractGroup();
                var stateRef = new MovableContentStateReference(
                    reference.Content, reference.Parameter,
                    table, reference.Anchor, reference.Locals);
                parentContext.InsertMovableContentState(stateRef);
            }
        }
    }

    sealed class EndMovableContentPlacementOp : Operation
    {
        public static readonly EndMovableContentPlacementOp Instance = new();
        public override int IntCount => 0;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
        }
    }

    sealed class SetGroupFlagsOp : Operation
    {
        public override int IntCount => 1;
        public override int ObjectCount => 0;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
            var flags = (GroupFlags)getInt(0);
            slots.SetGroupFlags(flags);
        }
    }

    sealed class ExecuteOperationsInOp : Operation
    {
        public static readonly ExecuteOperationsInOp Instance = new();
        public override int IntCount => 0;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
            var changeList = (Changes)getObject(0)!;
            changeList.Execute(null!, applier, rm);
        }
    }

    sealed class StartNodeOp : Operation
    {
        public override int IntCount => 1;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.StartNode(getInt(0), getObject(0), getObject(1));
        }
    }

    sealed class StartNodeReuseOp : Operation
    {
        public override int IntCount => 1;
        public override int ObjectCount => 2;
        public override void Execute(IApplier<object> applier, SlotWriter slots, RememberManager rm,
                                       Func<int, int> getInt, Func<int, object?> getObject)
        {
            slots.StartNodeReuse(getInt(0), getObject(0), getObject(1));
        }
    }
}
