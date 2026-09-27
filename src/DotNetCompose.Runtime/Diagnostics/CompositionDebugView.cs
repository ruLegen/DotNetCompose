using System;
using System.Diagnostics;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.SlotTable;

[assembly: DebuggerDisplay("Disposed = {IsDisposed}, Faulted = {IsFaulted}, PendingChanges = {HasPendingChanges}", Target = typeof(Composition<>))]
[assembly: DebuggerTypeProxy(typeof(DotNetCompose.Runtime.Diagnostics.CompositionDebugView<>), Target = typeof(Composition<>))]
[assembly: DebuggerDisplay("Groups = {Size}", Target = typeof(ComposerSlotTable))]
[assembly: DebuggerTypeProxy(typeof(DotNetCompose.Runtime.Diagnostics.ComposerSlotTableDebugView), Target = typeof(ComposerSlotTable))]
[assembly: DebuggerDisplay("Operations = {Count}, Consumed = {IsConsumed}", Target = typeof(CompositionChangeSet))]
[assembly: DebuggerTypeProxy(typeof(DotNetCompose.Runtime.Diagnostics.CompositionChangeSetDebugView), Target = typeof(CompositionChangeSet))]

namespace DotNetCompose.Runtime.Diagnostics
{
    internal sealed class CompositionDebugView<TNode> where TNode : class
    {
        public CompositionDebugView(Composition<TNode> composition) => _composition = composition;

        private readonly Composition<TNode> _composition;

        public string State => _composition.IsDisposed ? "Disposed" : _composition.IsFaulted ? "Faulted" : "Active";
        public bool IsDisposed => _composition.IsDisposed;
        public bool IsFaulted => _composition.IsFaulted;
        public bool HasInvalidations => _composition.HasInvalidations;
        public bool HasPendingChanges => _composition.HasPendingChanges;
        public CompositionChangeSet? PendingChanges => _composition.PendingChanges;
        public CompositionDiagnosticsSnapshot Diagnostics => CompositionDiagnosticsRuntime.GetSnapshot(_composition);
        public ComposerSlotTable SlotTable => _composition.SlotTable;
    }

    internal sealed class ComposerSlotTableDebugView
    {
        private readonly ComposerSlotTable _table;

        internal ComposerSlotTableDebugView(ComposerSlotTable table) => _table = table;

        public int Size => _table.Size;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public DebugSlotGroup[] Groups
        {
            get
            {
                try
                {
                    using ComposerSlotTable.Reader reader = _table.OpenReader();
                    DebugSlotGroup[] groups = new DebugSlotGroup[reader.Size];
                    for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                    {
                        int slotCount = reader.GetSlotSize(groupIndex);
                        object?[] slots = new object?[slotCount];
                        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
                            slots[slotIndex] = reader.GroupGet(groupIndex, slotIndex);
                        groups[groupIndex] = new DebugSlotGroup(
                            groupIndex,
                            reader.GetParent(groupIndex),
                            reader.GetGroupKey(groupIndex),
                            reader.GetGroupSize(groupIndex),
                            reader.GetNodeCount(groupIndex),
                            slots);
                    }
                    return groups;
                }
                catch (InvalidOperationException)
                {
                    return Array.Empty<DebugSlotGroup>();
                }
            }
        }
    }

    [DebuggerDisplay("[{Index}] key={Key}, parent={Parent}, size={Size}, slots={Slots.Length}")]
    internal sealed class DebugSlotGroup
    {
        internal DebugSlotGroup(int index, int parent, int key, int size, int nodeCount, object?[] slots)
        {
            Index = index;
            Parent = parent;
            Key = key;
            Size = size;
            NodeCount = nodeCount;
            Slots = slots;
        }

        public int Index { get; }
        public int Parent { get; }
        public int Key { get; }
        public int Size { get; }
        public int NodeCount { get; }
        public object?[] Slots { get; }
    }

    internal sealed class CompositionChangeSetDebugView
    {
        private readonly CompositionChangeSet _changes;

        internal CompositionChangeSetDebugView(CompositionChangeSet changes) => _changes = changes;

        public bool IsConsumed => _changes.IsConsumed;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public CompositionOperation[] Operations
        {
            get
            {
                CompositionOperation[] result = new CompositionOperation[_changes.Count];
                for (int index = 0; index < result.Length; index++)
                    result[index] = _changes[index];
                return result;
            }
        }
    }
}
