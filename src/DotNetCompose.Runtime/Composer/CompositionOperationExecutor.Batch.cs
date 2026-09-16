using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Composer
{
    internal static partial class CompositionOperationExecutor
    {
        internal static void ExecuteAll<TNode>(CompositionChangeSet changes,
            ComposerSlotTable.Writer writer, IApplier<TNode> applier) where TNode : class
        {
            ComposerSlotTable insertTable = changes.InsertTable;
            for (int index = 0; index < changes.Count; index++)
            {
                ref readonly CompositionOperation operation = ref changes.ExecutableAt(index);
                Execute(in operation, writer, applier, insertTable);
            }
        }
    }
}
