using System;
using System.Collections;
using System.Collections.Generic;
using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Composer
{
    public enum CompositionOperationKind
    {
        InsertGroup, RemoveGroup, MoveGroup, UpdateSlot, AppendSlot, TrimSlots,
        CreateNode, UpdateNode, Down, Up, InsertTopDown, InsertBottomUp, RemoveNode, MoveNode
    }

    /// <summary>An immutable operation. Positions refer to the sequence at this operation's execution.</summary>
    public readonly struct CompositionOperation
    {
        // These references are not exposed through CompositionChangeSet's public API.
        private readonly CompositionGroup? _group;
        private readonly CompositionGroup? _parent;
        private readonly NodeUpdate? _update;

        internal CompositionOperation(CompositionOperationKind kind, int? groupIndex = null,
            int? sourceGroupIndex = null, int? nodeIndex = null, int? sourceNodeIndex = null,
            int? slotOffset = null, int count = 0, int key = 0, object? value = null,
            CompositionGroup? group = null, CompositionGroup? parent = null, NodeUpdate? update = null)
        {
            Kind = kind;
            GroupIndex = groupIndex;
            SourceGroupIndex = sourceGroupIndex;
            NodeIndex = nodeIndex;
            SourceNodeIndex = sourceNodeIndex;
            SlotOffset = slotOffset;
            Count = count;
            Key = key;
            Value = value;
            _group = group;
            _parent = parent;
            _update = update;
        }

        public CompositionOperationKind Kind { get; }
        public int? GroupIndex { get; }
        public int? SourceGroupIndex { get; }
        public int? NodeIndex { get; }
        public int? SourceNodeIndex { get; }
        public int? SlotOffset { get; }
        public int Count { get; }
        public int Key { get; }
        public object? Value { get; }

        internal CompositionGroup? Group => _group;
        internal CompositionGroup? Parent => _parent;
        internal NodeUpdate? Update => _update;

        internal static CompositionOperation InsertGroup(CompositionGroup group, int insertionIndex, CompositionGroup? parent) =>
            new CompositionOperation(CompositionOperationKind.InsertGroup, groupIndex: insertionIndex,
                count: group.Size, key: group.Key, group: group, parent: parent);

        internal static CompositionOperation RemoveGroup(CompositionGroup group, int groupIndex) =>
            new CompositionOperation(CompositionOperationKind.RemoveGroup, groupIndex: groupIndex,
                count: group.Size, key: group.Key, group: group);

        internal static CompositionOperation MoveGroup(CompositionGroup group, int insertionIndex, int sourceGroupIndex) =>
            new CompositionOperation(CompositionOperationKind.MoveGroup, groupIndex: insertionIndex,
                sourceGroupIndex: sourceGroupIndex, count: group.Size, key: group.Key, group: group);

        internal static CompositionOperation UpdateSlot(CompositionGroup group, int groupIndex, int slotOffset, object? value) =>
            new CompositionOperation(CompositionOperationKind.UpdateSlot, groupIndex: groupIndex,
                slotOffset: slotOffset, key: group.Key, value: value, group: group);

        internal static CompositionOperation AppendSlot(CompositionGroup group, int groupIndex, int slotOffset, object? value) =>
            new CompositionOperation(CompositionOperationKind.AppendSlot, groupIndex: groupIndex,
                slotOffset: slotOffset, key: group.Key, value: value, group: group);

        internal static CompositionOperation TrimSlots(CompositionGroup group, int groupIndex, int remainingSlots, int removedCount) =>
            new CompositionOperation(CompositionOperationKind.TrimSlots, groupIndex: groupIndex,
                slotOffset: remainingSlots, count: removedCount, key: group.Key, group: group);

        internal static CompositionOperation CreateNode(CompositionGroup group, int nodeIndex) =>
            new CompositionOperation(CompositionOperationKind.CreateNode, nodeIndex: nodeIndex,
                key: group.Key, group: group);

        internal static CompositionOperation UpdateNode(CompositionGroup group, int nodeIndex, NodeUpdate update) =>
            new CompositionOperation(CompositionOperationKind.UpdateNode, nodeIndex: nodeIndex,
                key: group.Key, value: update.Value, group: group, update: update);

        internal static CompositionOperation Down(CompositionGroup group, int nodeIndex) =>
            new CompositionOperation(CompositionOperationKind.Down, nodeIndex: nodeIndex, group: group);

        internal static CompositionOperation Up() => new CompositionOperation(CompositionOperationKind.Up);

        internal static CompositionOperation InsertTopDown(CompositionGroup group, int nodeIndex) =>
            new CompositionOperation(CompositionOperationKind.InsertTopDown, nodeIndex: nodeIndex,
                count: 1, group: group);

        internal static CompositionOperation InsertBottomUp(CompositionGroup group, int nodeIndex) =>
            new CompositionOperation(CompositionOperationKind.InsertBottomUp, nodeIndex: nodeIndex,
                count: 1, group: group);

        internal static CompositionOperation RemoveNode(int nodeIndex, int count) =>
            new CompositionOperation(CompositionOperationKind.RemoveNode, nodeIndex: nodeIndex, count: count);

        internal static CompositionOperation MoveNode(int nodeIndex, int sourceNodeIndex, int count) =>
            new CompositionOperation(CompositionOperationKind.MoveNode, nodeIndex: nodeIndex,
                sourceNodeIndex: sourceNodeIndex, count: count);

        internal CompositionOperation PublicView() =>
            new CompositionOperation(Kind, GroupIndex, SourceGroupIndex, NodeIndex, SourceNodeIndex,
                SlotOffset, Count, Key, Value);

        public override string ToString()
        {
            List<string> parts = new List<string> { Kind.ToString() };
            if (GroupIndex.HasValue) parts.Add($"group={GroupIndex.Value}");
            if (SourceGroupIndex.HasValue) parts.Add($"sourceGroup={SourceGroupIndex.Value}");
            if (NodeIndex.HasValue) parts.Add($"node={NodeIndex.Value}");
            if (SourceNodeIndex.HasValue) parts.Add($"sourceNode={SourceNodeIndex.Value}");
            if (SlotOffset.HasValue) parts.Add($"slot={SlotOffset.Value}");
            if (Count != 0) parts.Add($"count={Count}");
            if (Key != 0) parts.Add($"key={Key}");
            return string.Join(", ", parts);
        }
    }

    /// <summary>A single-use, in-memory batch owned and executed by its composition.</summary>
    public sealed class CompositionChangeSet : IReadOnlyList<CompositionOperation>
    {
        private readonly CompositionOperation[] _operations;
        private ComposerSlotTable? _insertTable;

        internal CompositionChangeSet(object owner, int version, List<CompositionOperation> operations,
            ComposerSlotTable insertTable)
        {
            Owner = owner;
            Version = version;
            _operations = operations.ToArray();
            _insertTable = insertTable ?? throw new ArgumentNullException(nameof(insertTable));
        }

        internal object Owner { get; }
        internal int Version { get; }
        internal ComposerSlotTable InsertTable => _insertTable ??
            throw new InvalidOperationException("The prepared changes have already been consumed.");
        public bool IsConsumed { get; private set; }
        public int Count => _operations.Length;
        public CompositionOperation this[int index] => _operations[index].PublicView();

        internal ref readonly CompositionOperation ExecutableAt(int index) => ref _operations[index];

        internal void Validate()
        {
            for (int index = 0; index < _operations.Length; index++)
                CompositionOperationExecutor.Validate(in _operations[index]);
        }

        internal void Consume()
        {
            if (IsConsumed) return;
            for (int index = 0; index < _operations.Length; index++)
                _operations[index] = _operations[index].PublicView();
            _insertTable = null;
            IsConsumed = true;
        }

        public IEnumerator<CompositionOperation> GetEnumerator()
        {
            for (int index = 0; index < _operations.Length; index++) yield return this[index];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public interface IControlledComposition : IComposition
    {
        void ComposeContent(ComposableAction content);
        bool Recompose();
        void ApplyChanges();
        void DiscardChanges();
        bool HasInvalidations { get; }
        bool HasPendingChanges { get; }
        CompositionChangeSet? PendingChanges { get; }
    }
}
