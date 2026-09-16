using System.Collections.Generic;
using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Composer
{
    internal static partial class CompositionOperationExecutor
    {
        private static void InsertGroup(ComposerSlotTable.Writer writer, ComposerSlotTable insertTable,
            CompositionGroup group, CompositionGroup? parent, int insertionIndex)
        {
            IReadOnlyDictionary<GroupAnchor, GroupAnchor> mapping = writer.ImportGroup(
                insertTable, group.TemporaryAnchor, insertionIndex, parent?.Anchor ?? GroupAnchor.Empty);
            AssignAnchors(group, mapping);
        }

        private static void AssignAnchors(CompositionGroup group,
            IReadOnlyDictionary<GroupAnchor, GroupAnchor> mapping)
        {
            group.Anchor = mapping[group.TemporaryAnchor];
            foreach (CompositionGroup child in group.Children) AssignAnchors(child, mapping);
        }

        private static void RemoveGroup(ComposerSlotTable.Writer writer, CompositionGroup group) =>
            writer.RemoveGroup(group.Anchor);

        private static void MoveGroup(ComposerSlotTable.Writer writer, CompositionGroup group, int insertionIndex) =>
            writer.MoveGroup(group.Anchor, insertionIndex);
    }
}
