using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Composer
{
    internal static partial class CompositionOperationExecutor
    {
        private static void CreateNode(ComposerSlotTable.Writer writer, CompositionGroup group)
        {
            NodeReference node = group.Node;
            node.Value = node.Factory!();
            node.Factory = null;
            writer.UpdateNode(group.Anchor, node.Value);
        }

        private static void UpdateNode<TNode>(IApplier<TNode> applier, NodeUpdate update) where TNode : class =>
            applier.Apply(update.Action, update.Value);

        private static void Down<TNode>(IApplier<TNode> applier, CompositionGroup group) where TNode : class =>
            applier.Down((TNode)group.Node.Value!);

        private static void Up<TNode>(IApplier<TNode> applier) where TNode : class => applier.Up();

        private static void InsertTopDown<TNode>(IApplier<TNode> applier, CompositionGroup group,
            int nodeIndex) where TNode : class => applier.InsertTopDown(nodeIndex, (TNode)group.Node.Value!);

        private static void InsertBottomUp<TNode>(IApplier<TNode> applier, CompositionGroup group,
            int nodeIndex) where TNode : class => applier.InsertBottomUp(nodeIndex, (TNode)group.Node.Value!);

        private static void RemoveNode<TNode>(IApplier<TNode> applier, int nodeIndex,
            int count) where TNode : class => applier.Remove(nodeIndex, count);

        private static void MoveNode<TNode>(IApplier<TNode> applier, int sourceNodeIndex,
            int nodeIndex, int count) where TNode : class => applier.Move(sourceNodeIndex, nodeIndex, count);
    }
}
