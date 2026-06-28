using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.Snapshots
{
    public class MutableSnapshot : Snapshot
    {
        internal override Action<object>? ReadObserver { get; set; }
        internal override Action<object>? WriteObserver { get; set; }
        internal override int WriteCountValue { get; set; }
        internal override HashSet<IStateObject>? Modified { get; set; }

        private int _snapshots = 1;
        public bool Applied { get; internal set; }

        public override Snapshot Root => this;
        public override bool ReadOnly => false;

        internal MutableSnapshot(long id, SnapshotIdSet invalid,
            Action<object>? readObserver, Action<object>? writeObserver)
            : base(id, invalid)
        {
            ReadObserver = readObserver;
            WriteObserver = writeObserver;
        }

        public override bool HasPendingChanges() => Modified?.Count > 0;

        public virtual MutableSnapshot TakeNestedMutableSnapshot(
            Action<object>? readObserver = null,
            Action<object>? writeObserver = null)
        {
            ValidateNotDisposed();
            return Advance(() =>
            {
                return Sync(() =>
                {
                    var newId = NextSnapshotId++;
                    OpenSnapshots = OpenSnapshots.Set(newId);
                    var currentInvalid = Invalid;
                    Invalid = currentInvalid.Set(newId);
                    return new NestedMutableSnapshot(
                        newId,
                        currentInvalid,
                        MergeReadObserver(readObserver, ReadObserver),
                        MergeWriteObserver(writeObserver, WriteObserver),
                        this
                    );
                });
            });
        }

        public override Snapshot TakeNestedSnapshot(Action<object>? readObserver = null)
        {
            return TakeNestedMutableSnapshot(readObserver, null);
        }

        public virtual SnapshotApplyResult Apply()
        {
            var modified = Modified;

            var observers = new List<Action<HashSet<IStateObject>, Snapshot>>();
            HashSet<IStateObject>? globalModified = null;

            Sync(() =>
            {
                ValidateOpen(this);
                var previousGlobal = GlobalSnapshot;

                if (modified == null || modified.Count == 0)
                {
                    CloseLocked();
                    var prevMod = previousGlobal.Modified;
                    AdvanceGlobalSnapshot();
                    if (prevMod != null && prevMod.Count > 0)
                    {
                        observers.AddRange(ApplyObservers);
                        globalModified = prevMod;
                    }
                }
                else
                {
                    var result = InnerApplyLocked(this, modified,
                        OpenSnapshots.Clear(GlobalSnapshot.Id));
                    if (!result.Succeeded)
                        return result;

                    CloseLocked();
                    var prevMod = previousGlobal.Modified;
                    AdvanceGlobalSnapshot();
                    Modified = null;
                    previousGlobal.Modified = null;
                    observers.AddRange(ApplyObservers);
                    globalModified = prevMod;
                }
                return null;
            });

            Applied = true;

            if (globalModified != null && globalModified.Count > 0)
            {
                PendingApplyObserverCount++;
                try
                {
                    foreach (var obs in observers)
                    {
                        try { obs(globalModified, this); }
                        catch { }
                    }
                }
                finally { PendingApplyObserverCount--; }
            }

            if (modified != null && modified.Count > 0)
            {
                PendingApplyObserverCount++;
                try
                {
                    foreach (var obs in observers)
                    {
                        try { obs(modified, this); }
                        catch { }
                    }
                }
                finally { PendingApplyObserverCount--; }
            }

            Sync(() =>
            {
                ReleasePinnedSnapshotLocked();
                if (globalModified != null)
                    foreach (var s in globalModified)
                        ProcessForUnusedRecordsLocked(s);
                if (modified != null)
                    foreach (var s in modified)
                        ProcessForUnusedRecordsLocked(s);
            });

            return SnapshotApplyResult.Success;
        }

        internal override void RecordModified(IStateObject state)
        {
            if (Modified == null)
                Modified = new HashSet<IStateObject>();
            Modified.Add(state);
        }

        internal override void NotifyObjectsInitialized()
        {
        }

        internal override void CloseLocked()
        {
            OpenSnapshots = OpenSnapshots.Clear(Id);
        }

        private void ValidateNotDisposed()
        {
            if (Disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        internal void RecordPrevious(long id)
        {
            Sync(() =>
            {
                PreviousIds = PreviousIds.Set(id);
            });
        }

        internal void RecordPreviousList(SnapshotIdSet ids)
        {
            Sync(() =>
            {
                PreviousIds = PreviousIds.Or(ids);
            });
        }

        private T Advance<T>(Func<T> value)
        {
            RecordPrevious(Id);
            var result = value();
            if (!Applied && !Disposed)
            {
                var previousId = Id;
                Sync(() =>
                {
                    Id = NextSnapshotId++;
                    OpenSnapshots = OpenSnapshots.Set(Id);
                });
                Invalid = Invalid.AddRange(previousId + 1, Id);
            }
            return result;
        }

        internal void ActivateNestedSnapshot()
        {
            _snapshots++;
        }

        internal void NestedDeactivated(Snapshot snapshot)
        {
            if (--_snapshots == 0 && !Applied)
            {
            }
        }

        public override void Dispose()
        {
            if (!Disposed)
            {
                base.Dispose();
                NestedDeactivated(this);
            }
        }

        internal static void ValidateOpen(Snapshot snapshot)
        {
            if (!OpenSnapshots.Get(snapshot.Id))
                throw new InvalidOperationException("Snapshot is not open");
        }

        internal static SnapshotApplyResult InnerApplyLocked(
            MutableSnapshot snapshot,
            HashSet<IStateObject> modified,
            SnapshotIdSet invalid)
        {
            foreach (var state in modified)
            {
                var record = state.FirstStateRecord;
                StateRecord? appliedRecord = null;

                while (record != null)
                {
                    if (record.SnapshotId == snapshot.Id)
                    {
                        appliedRecord = record;
                        break;
                    }
                    record = record.Next;
                }

                if (appliedRecord == null) continue;

                var globalRecord = ReadableSilent(
                    state.FirstStateRecord, GlobalSnapshot.Id, invalid);

                if (globalRecord != null &&
                    globalRecord.SnapshotId < appliedRecord.SnapshotId)
                {
                    var previous = ReadableSilent(
                        state.FirstStateRecord,
                        snapshot.Id - 1,
                        invalid);

                    if (previous != null &&
                        previous.SnapshotId == globalRecord.SnapshotId)
                    {
                        appliedRecord.SnapshotId = GlobalSnapshot.Id;
                        continue;
                    }

                    var merged = state.MergeRecords(
                        previous ?? globalRecord,
                        globalRecord,
                        appliedRecord);

                    if (merged == null)
                        return SnapshotApplyResult.Failure(
                            "Conflicting writes to " + state.GetType().Name);

                    merged.SnapshotId = GlobalSnapshot.Id;
                    merged.Next = state.FirstStateRecord;
                    state.PrependStateRecord(merged);
                }
                else
                {
                    appliedRecord.SnapshotId = GlobalSnapshot.Id;
                }
            }

            return SnapshotApplyResult.Success;
        }

    }
}
