using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Composer
{
    internal static partial class CompositionOperationExecutor
    {
        private static void UpdateSlot(ComposerSlotTable.Writer writer, CompositionGroup group,
            int slotOffset, object? value) => writer.SetSlot(group.Anchor, slotOffset, value);

        private static void AppendSlot(ComposerSlotTable.Writer writer, CompositionGroup group,
            object? value) => writer.AppendSlot(group.Anchor, value);

        private static void TrimSlots(ComposerSlotTable.Writer writer, CompositionGroup group,
            int remainingSlots) => writer.TrimSlots(group.Anchor, remainingSlots);
    }
}
