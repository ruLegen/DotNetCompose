using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Composer
{
    internal sealed class CompositionChangeBuilder
    {
        internal readonly List<CompositionOperation> Operations = new List<CompositionOperation>();
        private readonly ComposerSlotTable _insertTable = new ComposerSlotTable();
        internal ComposerSlotTable InsertTable => _insertTable;

        internal CompositionChangeBuilder(CompositionGroup? old, CompositionGroup next)
        {
            using (ComposerSlotTable.Writer writer = _insertTable.OpenWriter()) PrepareInserts(writer, next);
            if (old == null) Insert(next, 0, null);
            else DiffGroup(old, next, 0);
            DiffNodes(old?.Children ?? new List<CompositionGroup>(), next.Children);
        }

        private static void WriteNew(ComposerSlotTable.Writer writer, CompositionGroup group)
        {
            if (group.IsNode) writer.StartNode(group.Key, null);
            else if (group.Kind == CompositionGroupKind.Movable) writer.StartGroup(group.Key, group.ObjectKey);
            else writer.StartGroup(group.Key);
            group.TemporaryAnchor = writer.CurrentAnchor;
            foreach (object? slot in group.Slots) writer.AppendSlot(slot);
            foreach (CompositionGroup child in group.Children) WriteNew(writer, child);
            writer.EndGroup();
        }

        private static void PrepareInserts(ComposerSlotTable.Writer writer, CompositionGroup group)
        {
            if (group.Anchor.IsEmpty) WriteNew(writer, group);
            else foreach (CompositionGroup child in group.Children) PrepareInserts(writer, child);
        }

        private void Insert(CompositionGroup group, int insertionIndex, CompositionGroup? parent)
        {
            Operations.Add(CompositionOperation.InsertGroup(group, insertionIndex, parent));
        }

        private void DiffGroup(CompositionGroup old, CompositionGroup next, int groupIndex)
        {
            if (ReferenceEquals(old, next)) return;
            for (int slotOffset = 0; slotOffset < next.Slots.Count; slotOffset++)
            {
                object? value = next.Slots[slotOffset];
                if (slotOffset >= old.Slots.Count)
                    Operations.Add(CompositionOperation.AppendSlot(next, groupIndex, slotOffset, value));
                else if (!ReferenceEquals(old.Slots[slotOffset], value))
                    Operations.Add(CompositionOperation.UpdateSlot(next, groupIndex, slotOffset, value));
            }
            if (old.Slots.Count > next.Slots.Count)
                Operations.Add(CompositionOperation.TrimSlots(next, groupIndex, next.Slots.Count,
                    old.Slots.Count - next.Slots.Count));

            List<CompositionGroup> remaining = new List<CompositionGroup>(old.Children);
            int position = groupIndex + 1;
            foreach (CompositionGroup child in next.Children)
            {
                CompositionGroup? previous = child.Previous ?? (remaining.Contains(child) ? child : null);
                int found = previous == null ? -1 : remaining.IndexOf(previous);
                if (found < 0) Insert(child, position, next);
                else
                {
                    if (found > 0)
                    {
                        int sourceGroupIndex = position;
                        for (int index = 0; index < found; index++) sourceGroupIndex += remaining[index].Size;
                        Operations.Add(CompositionOperation.MoveGroup(child, position, sourceGroupIndex));
                    }
                    remaining.RemoveAt(found);
                    DiffGroup(previous!, child, position);
                }
                position += child.Size;
            }
            foreach (CompositionGroup removed in remaining)
                Operations.Add(CompositionOperation.RemoveGroup(removed, position));
        }

        private static List<CompositionGroup> Nodes(List<CompositionGroup> groups)
        {
            List<CompositionGroup> nodes = new List<CompositionGroup>();
            foreach (CompositionGroup group in groups)
            {
                if (group.IsNode) nodes.Add(group);
                else nodes.AddRange(Nodes(group.Children));
            }
            return nodes;
        }

        private void DiffNodes(List<CompositionGroup> oldGroups, List<CompositionGroup> newGroups)
        {
            List<CompositionGroup> working = Nodes(oldGroups);
            List<CompositionGroup> desired = Nodes(newGroups);
            for (int nodeIndex = 0; nodeIndex < desired.Count; nodeIndex++)
            {
                CompositionGroup node = desired[nodeIndex];
                int found = -1;
                for (int candidateIndex = nodeIndex; candidateIndex < working.Count; candidateIndex++)
                {
                    if (!ReferenceEquals(working[candidateIndex].Node, node.Node)) continue;
                    found = candidateIndex;
                    break;
                }
                if (found < 0)
                {
                    Operations.Add(CompositionOperation.CreateNode(node, nodeIndex));
                    Operations.Add(CompositionOperation.InsertTopDown(node, nodeIndex));
                    working.Insert(nodeIndex, node);
                }
                else if (found != nodeIndex)
                {
                    Operations.Add(CompositionOperation.MoveNode(nodeIndex, found, 1));
                    CompositionGroup moved = working[found];
                    working.RemoveAt(found);
                    working.Insert(nodeIndex, moved);
                }
                CompositionGroup? old = found < 0 ? null : working[nodeIndex];
                if (!ReferenceEquals(old, node))
                {
                    int start = Operations.Count;
                    Operations.Add(CompositionOperation.Down(node, nodeIndex));
                    foreach (NodeUpdate update in node.Updates)
                        Operations.Add(CompositionOperation.UpdateNode(node, nodeIndex, update));
                    DiffNodes(old?.Children ?? new List<CompositionGroup>(), node.Children);
                    if (Operations.Count == start + 1 && old != null)
                        Operations.RemoveAt(start);
                    else Operations.Add(CompositionOperation.Up());
                }
                if (found < 0)
                    Operations.Add(CompositionOperation.InsertBottomUp(node, nodeIndex));
            }
            if (working.Count > desired.Count)
                Operations.Add(CompositionOperation.RemoveNode(desired.Count, working.Count - desired.Count));
        }
    }
}
