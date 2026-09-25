using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.SlotTable;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Composer
{
    internal sealed class Composer<TNode> : IComposerContext where TNode : class
    {
        private sealed class Frame
        {
            internal Frame(CompositionGroup group, CompositionLocalScope locals, bool providersInvalid)
            {
                Group = group;
                OldChildren = group.Previous?.Children ?? new List<CompositionGroup>();
                Locals = locals;
                ProvidersInvalid = providersInvalid;
            }
            internal readonly CompositionGroup Group;
            internal readonly List<CompositionGroup> OldChildren;
            internal readonly HashSet<CompositionGroup> Used = new HashSet<CompositionGroup>();
            internal Dictionary<(int, CompositionGroupKind, object?), Queue<CompositionGroup>>? Pending;
            internal int ChildCursor;
            internal int SlotCursor;
            internal int LastSlot = -1;
            internal bool NodeChosen;
            internal bool Skipped;
            internal CompositionLocalScope Locals;
            internal bool ProvidersInvalid;
        }

        private readonly ComposerSlotTable _table;
        private readonly ComposerSlotTable.Reader _reader;
        private readonly HashSet<object> _invalid;
        private readonly Stack<Frame> _stack = new Stack<Frame>();
        internal readonly List<object?> CreatedValues = new List<object?>();
        internal readonly HashSet<object> HandledWrites = new HashSet<object>(ReferenceComparer.Instance);
        private bool _closed;
        internal Composer(ComposerSlotTable table, HashSet<object> invalid)
        { _table = table; _reader = table.OpenReader(); _invalid = invalid; }

        private Frame Current
        {
            get
            {
                if (_closed) throw new ObjectDisposedException(nameof(Composer<TNode>));
                if (_stack.Count == 0) throw new InvalidOperationException("No composition is active.");
                return _stack.Peek();
            }
        }

        internal CompositionGroup Compute(CompositionGroup? previous, ComposableAction content, bool recompose)
        {
            CompositionGroup root = previous == null
                ? new CompositionGroup { Kind = CompositionGroupKind.Root }
                : CompositionGroup.Draft(previous);
            root.Locals = CompositionLocalScope.Empty;
            _stack.Push(new Frame(root, CompositionLocalScope.Empty, false));
            using (ComposeScope.EnterContext(this))
            {
                if (recompose && previous != null && !previous.Reads.Overlaps(_invalid)) SkipToGroupEnd();
                else content(this, ComposableArgumentsState.Empty, ComposableArgumentsDefaultState.Empty);
            }
            if (_stack.Count != 1) throw new InvalidOperationException("Unbalanced composition groups.");
            _stack.Pop();
            return root;
        }

        internal void RecordRead(object state)
        {
            foreach (Frame frame in _stack)
            {
                if (frame.Group.Kind == CompositionGroupKind.Restart || frame.Group.Kind == CompositionGroupKind.Root)
                { frame.Group.Reads.Add(state); return; }
            }
        }

        private CompositionGroup? Match(Frame frame, int key, CompositionGroupKind kind, object? dataKey)
        {
            if (frame.Pending == null && frame.ChildCursor < frame.OldChildren.Count)
            {
                CompositionGroup candidate = frame.OldChildren[frame.ChildCursor];
                if (candidate.Key == key && candidate.Kind == kind && Equals(candidate.ObjectKey, dataKey))
                { frame.ChildCursor++; frame.Used.Add(candidate); return candidate; }
            }
            if (frame.Pending == null)
            {
                frame.Pending = new Dictionary<(int, CompositionGroupKind, object?), Queue<CompositionGroup>>();
                List<KeyInfo> keys = new List<KeyInfo>();
                Dictionary<int, CompositionGroup> childrenByLocation = new Dictionary<int, CompositionGroup>();
                if (frame.Group.Previous != null && frame.ChildCursor < frame.OldChildren.Count)
                {
                    _reader.Reposition(_table.IndexOf(frame.OldChildren[frame.ChildCursor].Anchor));
                    keys = _reader.ExtractKeys();
                    foreach (CompositionGroup child in frame.OldChildren)
                        if (!frame.Used.Contains(child)) childrenByLocation.Add(_table.IndexOf(child.Anchor), child);
                }
                foreach (KeyInfo info in keys)
                {
                    if (!childrenByLocation.TryGetValue(info.Location, out CompositionGroup? child)) continue;
                    (int, CompositionGroupKind, object?) lookup = (info.Key, child.Kind, child.ObjectKey);
                    if (!frame.Pending.TryGetValue(lookup, out Queue<CompositionGroup>? queue))
                    { queue = new Queue<CompositionGroup>(); frame.Pending.Add(lookup, queue); }
                    queue.Enqueue(child);
                }
            }
            if (frame.Pending.TryGetValue((key, kind, dataKey), out Queue<CompositionGroup>? matches) && matches.Count > 0)
            { CompositionGroup match = matches.Dequeue(); frame.Used.Add(match); return match; }
            return null;
        }

        private void Start(int key, CompositionGroupKind kind, object? dataKey = null)
        {
            Frame parent = Current;
            if (parent.Skipped) throw new InvalidOperationException("Cannot emit children after skipping a group.");
            CompositionGroup? old = Match(parent, key, kind, dataKey);
            CompositionGroup group = old == null
                ? new CompositionGroup { Key = key, Kind = kind, ObjectKey = dataKey }
                : CompositionGroup.Draft(old);
            group.Locals = parent.Locals;
            parent.Group.Children.Add(group);
            _stack.Push(new Frame(group, parent.Locals, parent.ProvidersInvalid));
        }

        private CompositionGroup End(CompositionGroupKind kind, int? key = null)
        {
            Frame frame = Current;
            if (_stack.Count <= 1 || frame.Group.Kind != kind || (key.HasValue && frame.Group.Key != key.Value))
                throw new InvalidOperationException("Unbalanced group operation.");
            if (frame.Group.IsNode && !frame.NodeChosen) throw new InvalidOperationException("CreateNode or UseNode was not called.");
            _stack.Pop();
            return frame.Group;
        }

        public void StartRoot() { if (Current.Group.Kind != CompositionGroupKind.Root) throw new InvalidOperationException("Root is already active."); }
        public void EndRoot() { if (_stack.Count != 1) throw new InvalidOperationException("Unbalanced root."); }
        public void StartGroup(int key) => Start(key, CompositionGroupKind.Group);
        public void EndGroup() => End(CompositionGroupKind.Group);
        public void StartRestartableGroup(int key) => Start(key, CompositionGroupKind.Restart);
        public IComposeUpdateScope? EndRestartableGroup(int key)
        {
            bool skipped = Current.Skipped;
            CompositionGroup group = End(CompositionGroupKind.Restart, key);
            return skipped ? null : group;
        }
        public void StartReplaceableGroup(int key) => Start(key, CompositionGroupKind.Replaceable);
        public void EndReplaceableGroup(int key) => End(CompositionGroupKind.Replaceable, key);
        public void StartMovableGroup(int key) => StartMovableGroup(key, null);
        public void StartMovableGroup(int key, object? dataKey) => Start(key, CompositionGroupKind.Movable, dataKey);
        public void EndMovableGroup(int key) => End(CompositionGroupKind.Movable, key);
        public void StartNode(int key = 0) => Start(key, CompositionGroupKind.Node);
        public void EndNode() => End(CompositionGroupKind.Node);
        public bool Inserting => Current.Group.Previous == null;
        public bool IsComposing => !_closed && _stack.Count > 0;
        public bool Skipping => !Inserting && !Current.ProvidersInvalid && !Current.Group.Previous!.Reads.Overlaps(_invalid);

        public object? RememberedValue()
        {
            Frame frame = Current;
            int offset = frame.SlotCursor++;
            object? value = frame.Group.Previous == null ? ComposerSlotTable.Empty
                : _reader.GroupGet(_table.IndexOf(frame.Group.Anchor), offset);
            frame.Group.Slots.Add(value);
            frame.LastSlot = offset;
            return value;
        }

        public void UpdateRememberedValue(object? value)
        {
            Frame frame = Current;
            if (frame.LastSlot < 0) throw new InvalidOperationException("Read a slot before updating it.");
            frame.Group.Slots[frame.LastSlot] = value;
            CreatedValues.Add(value);
        }

        public bool Changed<T>(T value)
        {
            object? old = RememberedValue();
            bool same = !ReferenceEquals(old, ComposerSlotTable.Empty)
                && (old is T typed ? EqualityComparer<T>.Default.Equals(typed, value) : old == null && value is null);
            if (!same) UpdateRememberedValue(value);
            return !same;
        }

        public void CreateNode<T>(Func<T> factory) where T : class
        {
            Frame frame = Current;
            if (!frame.Group.IsNode || frame.NodeChosen || !Inserting) throw new InvalidOperationException("Unexpected CreateNode.");
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (!typeof(TNode).IsAssignableFrom(typeof(T))) throw new InvalidOperationException("Node type is incompatible with the applier.");
            frame.Group.Node.Factory = () => factory() ?? throw new InvalidOperationException("Node factory returned null.");
            frame.NodeChosen = true;
        }

        public void UseNode()
        {
            Frame frame = Current;
            if (!frame.Group.IsNode || frame.NodeChosen || Inserting) throw new InvalidOperationException("Unexpected UseNode.");
            frame.NodeChosen = true;
        }

        public void ApplyNode<T>(Action<T> block, object? value)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            AddUpdate(new NodeUpdate((node, ignored) => block((T)node), value));
        }

        public void ApplyNode<T, TValue>(TValue value, Action<T, TValue> block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            if (Changed(value)) AddUpdate(new NodeUpdate((node, argument) => block((T)node, (TValue)argument!), value));
        }

        private void AddUpdate(NodeUpdate update)
        {
            Frame frame = Current;
            if (!frame.Group.IsNode || !frame.NodeChosen) throw new InvalidOperationException("Select a node before updating it.");
            frame.Group.Updates.Add(update);
        }

        public void ComposeContent(ComposableAction content) => content(this, default, default);

        public void StartProvider(ProvidedValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            StartProviderScope(new[] { value });
        }

        public void EndProvider() => End(CompositionGroupKind.Provider);

        public void StartProviders(IReadOnlyList<ProvidedValue> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            StartProviderScope(values);
        }

        public void EndProviders() => End(CompositionGroupKind.Provider);

        public T Consume<T>(CompositionLocal<T> key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return key.Read(Current.Locals);
        }

        private void StartProviderScope(IReadOnlyList<ProvidedValue> values)
        {
            const int providerKey = 0x4C6F636C;
            Start(providerKey, CompositionGroupKind.Provider);
            Frame frame = Current;
            bool inserting = frame.Group.Previous == null;
            CompositionLocalScope parentScope = frame.Locals;
            object? remembered = RememberedValue();
            CompositionLocalProviderState? previous = ReferenceEquals(remembered, ComposerSlotTable.Empty)
                ? null
                : remembered as CompositionLocalProviderState
                    ?? throw new InvalidOperationException("Invalid CompositionLocal provider state.");

            Dictionary<CompositionLocal, CompositionLocalValueHolder> ownValues = CompositionLocalScope.CreateValueMap();
            for (int index = 0; index < values.Count; index++)
            {
                ProvidedValue provided = values[index]
                    ?? throw new ArgumentException("A CompositionLocal provider value cannot be null.", nameof(values));
                CompositionLocal local = provided.CompositionLocal;
                if (!provided.CanOverride && parentScope.Contains(local)) continue;
                CompositionLocalValueHolder? oldHolder = null;
                previous?.Values.TryGetValue(local, out oldHolder);
                CompositionLocalValueHolder holder = local.UpdatedValueHolder(provided, oldHolder, out IStateObject? changedState);
                ownValues[local] = holder;
                if (changedState != null)
                {
                    _invalid.Add(changedState);
                    HandledWrites.Add(changedState);
                }
            }

            CompositionLocalScope scope = CompositionLocalScope.Merge(parentScope, ownValues, previous?.Scope);
            CompositionLocalProviderState state;
            if (previous != null && ReferenceEquals(scope, previous.Scope) && HasSameValues(previous.Values, ownValues))
                state = previous;
            else
                state = new CompositionLocalProviderState(ownValues, scope);

            if (!ReferenceEquals(state, previous)) UpdateRememberedValue(state);
            frame.Locals = scope;
            frame.Group.Locals = scope;
            frame.ProvidersInvalid = !inserting && previous != null && !ReferenceEquals(previous.Scope, scope);
        }

        private static bool HasSameValues(
            Dictionary<CompositionLocal, CompositionLocalValueHolder> first,
            Dictionary<CompositionLocal, CompositionLocalValueHolder> second)
        {
            if (first.Count != second.Count) return false;
            foreach (KeyValuePair<CompositionLocal, CompositionLocalValueHolder> item in first)
                if (!second.TryGetValue(item.Key, out CompositionLocalValueHolder? value) ||
                    !item.Value.IsEquivalentTo(value))
                    return false;
            return true;
        }

        public void SkipToGroupEnd()
        {
            Frame frame = Current;
            CompositionGroup old = frame.Group.Previous ?? throw new InvalidOperationException("Cannot skip a new group.");
            if (frame.Group.Children.Count != 0 || frame.Skipped) throw new InvalidOperationException("Skip must precede child traversal.");
            for (int i = frame.SlotCursor; i < old.Slots.Count; i++) frame.Group.Slots.Add(_reader.GroupGet(_table.IndexOf(old.Anchor), i));
            frame.Group.Reads.UnionWith(old.Reads);
            foreach (CompositionGroup child in old.Children) frame.Group.Children.Add(RecomposeChild(child));
            frame.Skipped = true;
        }

        private CompositionGroup RecomposeChild(CompositionGroup old)
        {
            if (old.Restart != null && old.Reads.Overlaps(_invalid))
            {
                CompositionGroup containerOld = new CompositionGroup();
                containerOld.Children.Add(old);
                CompositionGroup container = CompositionGroup.Draft(containerOld);
                container.Locals = old.Locals;
                _stack.Push(new Frame(container, old.Locals, false));
                int depth = _stack.Count;
                old.Restart(this);
                if (_stack.Count != depth || container.Children.Count != 1)
                    throw new InvalidOperationException("Restart must emit its original group.");
                _stack.Pop();
                return container.Children[0];
            }
            CompositionGroup? draft = null;
            for (int i = 0; i < old.Children.Count; i++)
            {
                CompositionGroup child = RecomposeChild(old.Children[i]);
                if (!ReferenceEquals(child, old.Children[i]) && draft == null)
                {
                    draft = CompositionGroup.Draft(old);
                    draft.Slots = new List<object?>(old.Slots);
                    draft.Reads = new HashSet<object>(old.Reads, ReferenceComparer.Instance);
                    draft.Children = new List<CompositionGroup>(old.Children);
                }
                if (draft != null) draft.Children[i] = child;
            }
            return draft ?? old;
        }

        public void Dispose() { if (_closed) return; _closed = true; _reader.Dispose(); }
    }
}
