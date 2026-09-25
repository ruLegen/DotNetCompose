using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Composer
{
    internal enum CompositionGroupKind { Root, Group, Replaceable, Movable, Restart, Node, Provider }

    internal sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    // A staged group contains only the writes/read dependencies of one execution. Skipped
    // groups are shared with the committed result and must never be mutated during compose.
    internal sealed class CompositionGroup : IComposeUpdateScope
    {
        internal int Key;
        internal CompositionGroupKind Kind;
        internal object? ObjectKey;
        internal CompositionGroup? Previous;
        internal GroupAnchor Anchor = GroupAnchor.Empty;
        internal GroupAnchor TemporaryAnchor = GroupAnchor.Empty;
        internal List<object?> Slots = new List<object?>();
        internal List<CompositionGroup> Children = new List<CompositionGroup>();
        internal HashSet<object> Reads = new HashSet<object>(ReferenceComparer.Instance);
        internal Action<IComposerContext>? Restart;
        internal NodeReference Node = new NodeReference();
        internal List<NodeUpdate> Updates = new List<NodeUpdate>();
        internal CompositionLocalScope Locals = CompositionLocalScope.Empty;
        internal bool IsNode => Kind == CompositionGroupKind.Node;
        internal int Size
        {
            get { int size = 1; foreach (CompositionGroup child in Children) size += child.Size; return size; }
        }
        public void UpdateScope(Action<IComposerContext> scopeUpdater) => Restart = scopeUpdater ?? throw new ArgumentNullException(nameof(scopeUpdater));
        internal static CompositionGroup Draft(CompositionGroup old) => new CompositionGroup
        { Key = old.Key, Kind = old.Kind, ObjectKey = old.ObjectKey, Previous = old, Anchor = old.Anchor, Node = old.Node, Restart = old.Restart, Locals = old.Locals };
    }

    internal sealed class NodeReference
    {
        internal object? Value;
        internal Func<object>? Factory;
    }

    internal sealed class NodeUpdate
    {
        internal NodeUpdate(Action<object, object?> action, object? value) { Action = action; Value = value; }
        internal readonly Action<object, object?> Action;
        internal readonly object? Value;
    }
}
