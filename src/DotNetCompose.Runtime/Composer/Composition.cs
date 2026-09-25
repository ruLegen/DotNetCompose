using System;
using System.Collections.Generic;
using System.Threading;
using DotNetCompose.Runtime.Effects;
using DotNetCompose.Runtime.SlotTable;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Composer
{
    public sealed class Composition<TNode> : IControlledComposition where TNode : class
    {
        private readonly IApplier<TNode> _applier;
        private readonly Recomposer? _recomposer;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly object _gate = new object();
        private readonly ObserverHandle _observer;
        private readonly HashSet<object> _changed = new HashSet<object>(ReferenceComparer.Instance);
        private HashSet<object> _observed = new HashSet<object>(ReferenceComparer.Instance);
        private HashSet<object> _computingInvalid = new HashSet<object>(ReferenceComparer.Instance);
        private HashSet<object> _initialComputingInvalid = new HashSet<object>(ReferenceComparer.Instance);
        private HashSet<object> _handledWrites = new HashSet<object>(ReferenceComparer.Instance);
        private CompositionGroup? _root;
        private CompositionGroup? _pendingRoot;
        private MutableSnapshot? _snapshot;
        private ComposableAction? _content;
        private ComposableAction? _pendingContent;
        private List<object?> _created = new List<object?>();
        private volatile bool _busy;
        private int _committedVersion;
        private bool _faulted;
        private bool _disposed;

        public Composition(IApplier<TNode> applier, Recomposer? recomposer = null)
        {
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            _recomposer = recomposer;
            recomposer?.VerifyAccess();
            _observer = Snapshot.RegisterApplyObserver(OnStateChanged);
            recomposer?.Register(this);
        }

        /// <summary>Read-only inspection is allowed while a batch is pending. External writes invalidate the batch.</summary>
        public ComposerSlotTable SlotTable { get; } = new ComposerSlotTable();
        public bool IsDisposed => _disposed;
        public bool IsFaulted => _faulted;
        public bool HasPendingChanges => PendingChanges != null;
        public CompositionChangeSet? PendingChanges { get; private set; }
        public bool HasInvalidations { get { lock (_gate) return _changed.Overlaps(_observed); } }

        private void OnStateChanged(HashSet<IStateObject> states, Snapshot snapshot)
        {
            lock (_gate)
            {
                if (_disposed) return;
                foreach (IStateObject state in states)
                    if (!(ReferenceEquals(snapshot, _snapshot) && _handledWrites.Contains(state)) &&
                        (_busy || HasPendingChanges || _observed.Contains(state)))
                        _changed.Add(state);
            }
            _recomposer?.RequestWork();
        }

        public void SetContent(ComposableAction content) { ComposeContent(content); ApplyChanges(); }
        public void ComposeContent(ComposableAction content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            Compute(content, false);
        }

        public bool Recompose()
        {
            VerifyReady();
            Snapshot.SendApplyNotifications();
            if (_content == null || !HasInvalidations) return false;
            Compute(_content, true);
            return true;
        }

        private void Compute(ComposableAction content, bool recompose)
        {
            VerifyReady();
            Snapshot.SendApplyNotifications();
            lock (_gate)
            {
                _initialComputingInvalid = new HashSet<object>(_changed, ReferenceComparer.Instance);
                _computingInvalid = new HashSet<object>(_initialComputingInvalid, ReferenceComparer.Instance);
                _changed.Clear();
            }
            _busy = true;
            try
            {
                using Composer<TNode> composer = new Composer<TNode>(SlotTable, _computingInvalid);
                _created = composer.CreatedValues;
                _handledWrites = composer.HandledWrites;
                _snapshot = Snapshot.TakeMutableSnapshot(composer.RecordRead, state => { });
                _pendingRoot = _snapshot.Enter(() => composer.Compute(_root, content, recompose));
                CompositionChangeBuilder builder = new CompositionChangeBuilder(_root, _pendingRoot);
                _pendingContent = content;
                PendingChanges = new CompositionChangeSet(this, SlotTable.Version,
                    builder.Operations, builder.InsertTable);
            }
            catch
            {
                CancelPending();
                throw;
            }
            finally { _busy = false; }
        }

        public void ApplyChanges()
        {
            VerifyAccess();
            ApplyChanges(PendingChanges ?? throw new InvalidOperationException("No changes are pending."));
        }

        public void ApplyChanges(CompositionChangeSet changes)
        {
            VerifyAccess();
            if (!ReferenceEquals(changes.Owner, this) || !ReferenceEquals(changes, PendingChanges) || changes.IsConsumed)
                throw new InvalidOperationException("This batch is not the pending batch of this composition.");
            if (changes.Version != SlotTable.Version) throw new InvalidOperationException("The slot table changed after composition.");
            changes.Validate();
            if (changes.Count > 0) SlotTable.EnsureCanWrite();
            _busy = true;
            bool applying = false;
            try
            {
                SnapshotApplyResult result = _snapshot!.Apply();
                if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                _snapshot.Dispose();
                _snapshot = null;
                if (changes.Count > 0)
                {
                    using ComposerSlotTable.Writer writer = SlotTable.OpenWriter();
                    applying = true;
                    _applier.OnBeginChanges();
                    CompositionOperationExecutor.ExecuteAll(changes, writer, _applier);
                    _applier.OnEndChanges();
                }
                List<IRememberObserver> before = Observers(_root);
                List<IRememberObserver> after = Observers(_pendingRoot);
                _root = _pendingRoot;
                _content = _pendingContent;
                _committedVersion = SlotTable.Version;
                CommitGroups(_root!);
                HashSet<object> observed = new HashSet<object>(ReferenceComparer.Instance);
                CollectReads(_root!, observed);
                lock (_gate) _observed = observed;
                changes.Consume();
                ClearPending();
                applying = true;
                DispatchRemember(before, after);
                AbandonCreated(after);
                _created.Clear();
            }
            catch
            {
                if (applying) _faulted = true;
                CancelPending();
                throw;
            }
            finally { _busy = false; }
            if (HasInvalidations) _recomposer?.RequestWork();
        }

        public void DiscardChanges()
        {
            VerifyAccess();
            _busy = true;
            try { CancelPending(); }
            finally { _busy = false; }
            if (HasInvalidations) _recomposer?.RequestWork();
        }

        private void CancelPending()
        {
            _snapshot?.Dispose();
            _snapshot = null;
            PendingChanges?.Consume();
            lock (_gate) _changed.UnionWith(_initialComputingInvalid);
            ClearPending();
            try { AbandonCreated(Observers(_root)); }
            finally { _created.Clear(); }
        }

        private void ClearPending()
        {
            PendingChanges = null;
            _pendingRoot = null;
            _pendingContent = null;
            _computingInvalid.Clear();
            _initialComputingInvalid.Clear();
            _handledWrites.Clear();
        }

        private static void CommitGroups(CompositionGroup group)
        {
            group.Previous = null;
            group.Updates.Clear();
            foreach (CompositionGroup child in group.Children) CommitGroups(child);
        }

        private static void CollectReads(CompositionGroup group, HashSet<object> reads)
        {
            reads.UnionWith(group.Reads);
            foreach (CompositionGroup child in group.Children) CollectReads(child, reads);
        }

        private static List<IRememberObserver> Observers(CompositionGroup? root)
        {
            List<IRememberObserver> result = new List<IRememberObserver>();
            if (root == null) return result;
            foreach (object? value in root.Slots) if (value is IRememberObserver observer) result.Add(observer);
            foreach (CompositionGroup child in root.Children) result.AddRange(Observers(child));
            return result;
        }

        private static void DispatchRemember(List<IRememberObserver> old, List<IRememberObserver> next)
        {
            List<IRememberObserver> added = new List<IRememberObserver>(next);
            List<IRememberObserver> removed = new List<IRememberObserver>();
            foreach (IRememberObserver observer in old)
            {
                int index = added.FindIndex(item => ReferenceEquals(item, observer));
                if (index >= 0) added.RemoveAt(index); else removed.Add(observer);
            }
            for (int i = removed.Count - 1; i >= 0; i--) removed[i].OnForgotten();
            foreach (IRememberObserver observer in added) observer.OnRemembered();
        }

        private void AbandonCreated(List<IRememberObserver> retained)
        {
            HashSet<object> visited = new HashSet<object>(ReferenceComparer.Instance);
            foreach (object? value in _created)
                if (value is IRememberObserver observer && visited.Add(observer) && !retained.Exists(item => ReferenceEquals(item, observer)))
                    observer.OnAbandoned();
        }

        private void VerifyAccess()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Composition<TNode>));
            if (_faulted) throw new InvalidOperationException("The composition failed during application and must be disposed.");
            if (_busy) throw new InvalidOperationException("Composition operations cannot be reentered.");
            if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Use the composition's owning context.");
        }

        private void VerifyReady()
        {
            VerifyAccess();
            if (HasPendingChanges) throw new InvalidOperationException("Apply or discard the pending changes first.");
            if (_root != null && SlotTable.Version != _committedVersion)
                throw new InvalidOperationException("The owned slot table was modified externally.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_thread != Environment.CurrentManagedThreadId || _busy) throw new InvalidOperationException("Dispose on the idle owning context.");
            _disposed = true;
            _observer.Dispose();
            _recomposer?.Unregister(this);
            try { CancelPending(); }
            finally
            {
                try { DispatchRemember(Observers(_root), new List<IRememberObserver>()); }
                finally
                {
                    _root = null;
                    _content = null;
                    lock (_gate) { _changed.Clear(); _observed.Clear(); }
                    try { _applier.Clear(); }
                    finally
                    {
                        using ComposerSlotTable.Writer writer = SlotTable.OpenWriter();
                        while (SlotTable.Size > 0) { writer.Reposition(0); writer.RemoveGroup(); }
                    }
                }
            }
        }
    }
}
