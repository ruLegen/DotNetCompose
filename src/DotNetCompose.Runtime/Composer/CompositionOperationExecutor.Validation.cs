using System;

namespace DotNetCompose.Runtime.Composer
{
    internal static partial class CompositionOperationExecutor
    {
        internal static void Validate(in CompositionOperation operation)
        {
            switch (operation.Kind)
            {
                case CompositionOperationKind.InsertGroup:
                    RequireGroup(in operation);
                    RequireIndex(operation.GroupIndex, in operation);
                    RequirePositiveCount(in operation);
                    if (operation.Group!.TemporaryAnchor.IsEmpty) throw Incomplete(in operation);
                    return;
                case CompositionOperationKind.RemoveGroup:
                    RequireGroup(in operation);
                    RequireIndex(operation.GroupIndex, in operation);
                    RequirePositiveCount(in operation);
                    return;
                case CompositionOperationKind.MoveGroup:
                    RequireGroup(in operation);
                    RequireIndex(operation.GroupIndex, in operation);
                    RequireIndex(operation.SourceGroupIndex, in operation);
                    RequirePositiveCount(in operation);
                    return;
                case CompositionOperationKind.UpdateSlot:
                case CompositionOperationKind.AppendSlot:
                    RequireGroup(in operation);
                    RequireIndex(operation.GroupIndex, in operation);
                    RequireIndex(operation.SlotOffset, in operation);
                    return;
                case CompositionOperationKind.TrimSlots:
                    RequireGroup(in operation);
                    RequireIndex(operation.GroupIndex, in operation);
                    RequireIndex(operation.SlotOffset, in operation);
                    RequirePositiveCount(in operation);
                    return;
                case CompositionOperationKind.CreateNode:
                    RequireGroup(in operation);
                    RequireIndex(operation.NodeIndex, in operation);
                    if (operation.Group!.Node.Factory == null)
                        throw Incomplete(in operation);
                    return;
                case CompositionOperationKind.UpdateNode:
                    RequireGroup(in operation);
                    RequireIndex(operation.NodeIndex, in operation);
                    if (operation.Update == null) throw Incomplete(in operation);
                    return;
                case CompositionOperationKind.Down:
                    RequireGroup(in operation);
                    RequireIndex(operation.NodeIndex, in operation);
                    return;
                case CompositionOperationKind.Up:
                    return;
                case CompositionOperationKind.InsertTopDown:
                case CompositionOperationKind.InsertBottomUp:
                    RequireGroup(in operation);
                    RequireIndex(operation.NodeIndex, in operation);
                    if (operation.Count != 1) throw Incomplete(in operation);
                    return;
                case CompositionOperationKind.RemoveNode:
                    RequireIndex(operation.NodeIndex, in operation);
                    RequirePositiveCount(in operation);
                    return;
                case CompositionOperationKind.MoveNode:
                    RequireIndex(operation.NodeIndex, in operation);
                    RequireIndex(operation.SourceNodeIndex, in operation);
                    RequirePositiveCount(in operation);
                    return;
                default:
                    throw new NotSupportedException($"Composition operation {operation.Kind} is not supported.");
            }
        }

        private static void RequireGroup(in CompositionOperation operation)
        {
            if (operation.Group == null) throw Incomplete(in operation);
        }

        private static void RequireIndex(int? index, in CompositionOperation operation)
        {
            if (!index.HasValue || index.Value < 0) throw Incomplete(in operation);
        }

        private static void RequirePositiveCount(in CompositionOperation operation)
        {
            if (operation.Count <= 0) throw Incomplete(in operation);
        }

        private static InvalidOperationException Incomplete(in CompositionOperation operation) =>
            new InvalidOperationException($"Composition operation {operation.Kind} has incomplete execution data.");
    }
}
