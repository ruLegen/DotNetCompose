using System;
using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Composer
{
    internal static partial class CompositionOperationExecutor
    {
        internal static void Execute<TNode>(in CompositionOperation operation,
            ComposerSlotTable.Writer writer, IApplier<TNode> applier, ComposerSlotTable insertTable)
            where TNode : class
        {
            switch (operation.Kind)
            {
                case CompositionOperationKind.InsertGroup:
                    InsertGroup(writer, insertTable, operation.Group!, operation.Parent, operation.GroupIndex!.Value);
                    return;
                case CompositionOperationKind.RemoveGroup:
                    RemoveGroup(writer, operation.Group!);
                    return;
                case CompositionOperationKind.MoveGroup:
                    MoveGroup(writer, operation.Group!, operation.GroupIndex!.Value);
                    return;
                case CompositionOperationKind.UpdateSlot:
                    UpdateSlot(writer, operation.Group!, operation.SlotOffset!.Value, operation.Value);
                    return;
                case CompositionOperationKind.AppendSlot:
                    AppendSlot(writer, operation.Group!, operation.Value);
                    return;
                case CompositionOperationKind.TrimSlots:
                    TrimSlots(writer, operation.Group!, operation.SlotOffset!.Value);
                    return;
                case CompositionOperationKind.CreateNode:
                    CreateNode(writer, operation.Group!);
                    return;
                case CompositionOperationKind.UpdateNode:
                    UpdateNode(applier, operation.Update!);
                    return;
                case CompositionOperationKind.Down:
                    Down(applier, operation.Group!);
                    return;
                case CompositionOperationKind.Up:
                    Up(applier);
                    return;
                case CompositionOperationKind.InsertTopDown:
                    InsertTopDown(applier, operation.Group!, operation.NodeIndex!.Value);
                    return;
                case CompositionOperationKind.InsertBottomUp:
                    InsertBottomUp(applier, operation.Group!, operation.NodeIndex!.Value);
                    return;
                case CompositionOperationKind.RemoveNode:
                    RemoveNode(applier, operation.NodeIndex!.Value, operation.Count);
                    return;
                case CompositionOperationKind.MoveNode:
                    MoveNode(applier, operation.SourceNodeIndex!.Value, operation.NodeIndex!.Value, operation.Count);
                    return;
                default:
                    throw new NotSupportedException($"Composition operation {operation.Kind} is not supported.");
            }
        }
    }
}
