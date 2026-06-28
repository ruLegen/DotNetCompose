using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.Snapshots
{
    internal class TransparentObserverMutableSnapshot : MutableSnapshot
    {
        private readonly MutableSnapshot? _parentSnapshot;
        private readonly bool _ownsParentSnapshot;
        internal long ThreadId { get; }
        internal override bool CanBeReused => ThreadId == Environment.CurrentManagedThreadId;

        internal TransparentObserverMutableSnapshot(
            MutableSnapshot? parentSnapshot,
            Action<object>? specifiedReadObserver,
            Action<object>? specifiedWriteObserver,
            bool mergeParentObservers = true,
            bool ownsParentSnapshot = false)
            : base(
                parentSnapshot?.Id ?? NextSnapshotId++,
                parentSnapshot?.Invalid ?? SnapshotIdSet.Empty,
                MergeReadObserver(specifiedReadObserver,
                    parentSnapshot?.ReadObserver, mergeParentObservers),
                MergeWriteObserver(specifiedWriteObserver,
                    parentSnapshot?.WriteObserver))
        {
            _parentSnapshot = parentSnapshot;
            _ownsParentSnapshot = ownsParentSnapshot;
            ThreadId = Environment.CurrentManagedThreadId;
        }

        public override Snapshot Root => _parentSnapshot?.Root ?? this;
        public override bool ReadOnly => _parentSnapshot?.ReadOnly ?? false;

        internal override HashSet<IStateObject>? Modified
        {
            get => _parentSnapshot?.Modified;
            set { if (_parentSnapshot != null) _parentSnapshot.Modified = value; }
        }

        internal override int WriteCountValue
        {
            get => _parentSnapshot?.WriteCountValue ?? 0;
            set { if (_parentSnapshot != null) _parentSnapshot.WriteCountValue = value; }
        }

        internal override void RecordModified(IStateObject state)
        {
            _parentSnapshot?.RecordModified(state);
        }

        internal override void NotifyObjectsInitialized()
        {
            _parentSnapshot?.NotifyObjectsInitialized();
        }

        public override SnapshotApplyResult Apply()
        {
            throw new InvalidOperationException(
                "Cannot apply a transparent observer snapshot");
        }

        public override void Dispose()
        {
            if (!Disposed)
            {
                Disposed = true;
                if (_ownsParentSnapshot)
                    _parentSnapshot?.Dispose();
            }
        }

        internal override void CloseLocked()
        {
            if (!_ownsParentSnapshot)
            {
                OpenSnapshots = OpenSnapshots.Clear(Id);
            }
            else
            {
                base.CloseLocked();
            }
        }
    }

    internal class TransparentObserverSnapshot : Snapshot
    {
        private readonly Snapshot? _parentSnapshot;
        private readonly bool _ownsParentSnapshot;
        internal long ThreadId { get; }
        internal override bool CanBeReused => ThreadId == Environment.CurrentManagedThreadId;

        internal override void CloseLocked() { }

        internal override Action<object>? ReadObserver { get; set; }
        internal override Action<object>? WriteObserver { get; set; }
        internal override int WriteCountValue
        {
            get => 0;
            set { }
        }
        internal override HashSet<IStateObject>? Modified { get; set; }

        public override Snapshot Root => _parentSnapshot?.Root ?? this;
        public override bool ReadOnly => true;

        internal TransparentObserverSnapshot(
            Snapshot? parentSnapshot,
            Action<object>? readObserver,
            bool ownsParentSnapshot = false)
            : base(
                parentSnapshot?.Id ?? NextSnapshotId++,
                parentSnapshot?.Invalid ?? SnapshotIdSet.Empty)
        {
            ReadObserver = MergeReadObserver(readObserver,
                parentSnapshot?.ReadObserver, true);
            _parentSnapshot = parentSnapshot;
            _ownsParentSnapshot = ownsParentSnapshot;
            ThreadId = Environment.CurrentManagedThreadId;
        }

        public override Snapshot TakeNestedSnapshot(Action<object>? readObserver = null)
        {
            if (readObserver == null) return this;
            return new TransparentObserverSnapshot(this, readObserver);
        }

        public override bool HasPendingChanges() => false;
        internal override void RecordModified(IStateObject state) { }
        internal override void NotifyObjectsInitialized() { }

        public override void Dispose()
        {
            if (!Disposed)
            {
                Disposed = true;
                if (_ownsParentSnapshot)
                    _parentSnapshot?.Dispose();
            }
        }
    }
}
