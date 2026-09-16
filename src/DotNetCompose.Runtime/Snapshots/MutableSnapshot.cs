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
        private readonly long _initialId;
        private readonly SnapshotIdSet _initialInvalid;
        public bool Applied { get; internal set; }

        public override Snapshot Root => this;
        public override bool ReadOnly => false;

        internal MutableSnapshot(long id, SnapshotIdSet invalid,
            Action<object>? readObserver, Action<object>? writeObserver)
            : base(id, invalid)
        {
            ReadObserver = readObserver;
            WriteObserver = writeObserver;
            _initialId = id;
            _initialInvalid = invalid;
        }

        public override bool HasPendingChanges() => Modified?.Count > 0;

        public virtual MutableSnapshot TakeNestedMutableSnapshot(
            Action<object>? readObserver = null,
            Action<object>? writeObserver = null)
        {
            ValidateNotDisposed();
            return Advance(() =>
            {
                using (Snapshot.Lock())
                {
                    long newId = NextSnapshotId++;
                    OpenSnapshots = OpenSnapshots.Set(newId);
                    SnapshotIdSet currentInvalid = Invalid;
                    Invalid = currentInvalid.Set(newId);
                    return new NestedMutableSnapshot(
                        newId,
                        currentInvalid,
                        MergeReadObserver(readObserver, ReadObserver),
                        MergeWriteObserver(writeObserver, WriteObserver),
                        this
                    );
                }
            });
        }

        public override Snapshot TakeNestedSnapshot(Action<object>? readObserver = null)
        {
            return TakeNestedMutableSnapshot(readObserver, null);
        }

        public virtual SnapshotApplyResult Apply()
        {
            HashSet<IStateObject>? modified = Modified;

            List<Action<HashSet<IStateObject>, Snapshot>> observers = new List<Action<HashSet<IStateObject>, Snapshot>>();
            HashSet<IStateObject>? globalModified = null;
            SnapshotApplyResult? failureResult = null;

            using (Snapshot.Lock())
            {
                ValidateOpen(this);
                MutableSnapshot previousGlobal = GlobalSnapshot;

                if (modified == null || modified.Count == 0)
                {
                    CloseLocked();
                    HashSet<IStateObject>? prevMod = previousGlobal.Modified;
                    AdvanceGlobalSnapshot();
                    if (prevMod != null && prevMod.Count > 0)
                    {
                        observers.AddRange(ApplyObservers);
                        globalModified = prevMod;
                    }
                }
                else
                {
                    SnapshotApplyResult result = InnerApplyLocked(this, modified, GlobalSnapshot);
                    if (!result.Succeeded)
                    {
                        failureResult = result;
                    }
                    else
                    {
                        CloseLocked();
                        HashSet<IStateObject>? prevMod = previousGlobal.Modified;
                        AdvanceGlobalSnapshot();
                        Modified = null;
                        previousGlobal.Modified = null;
                        observers.AddRange(ApplyObservers);
                        globalModified = prevMod;
                    }
                }
            }

            if (failureResult.HasValue) 
                return failureResult.Value;

            Applied = true;

            if (globalModified != null && globalModified.Count > 0)
            {
                PendingApplyObserverCount++;
                try
                {
                    foreach (Action<HashSet<IStateObject>, Snapshot> obs in observers)
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
                    foreach (Action<HashSet<IStateObject>, Snapshot> obs in observers)
                    {
                        try { obs(modified, this); }
                        catch { }
                    }
                }
                finally { PendingApplyObserverCount--; }
            }

            using (Snapshot.Lock())
            {
                ReleasePinnedSnapshotLocked();
                if (globalModified != null)
                    foreach (IStateObject s in globalModified)
                        ProcessForUnusedRecordsLocked(s);
                if (modified != null)
                    foreach (IStateObject s in modified)
                        ProcessForUnusedRecordsLocked(s);
            }

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
            OpenSnapshots = OpenSnapshots.Clear(Id).AndNot(PreviousIds);
        }

        private void ValidateNotDisposed()
        {
            if (Disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        internal void RecordPrevious(long id)
        {
            using (Snapshot.Lock())
            {
                PreviousIds = PreviousIds.Set(id);
            }
        }

        internal void RecordPreviousList(SnapshotIdSet ids)
        {
            using (Snapshot.Lock())
            {
                PreviousIds = PreviousIds.Or(ids);
            }
        }

        private T Advance<T>(Func<T> value)
        {
            RecordPrevious(Id);
            T result = value();
            if (!Applied && !Disposed)
            {
                long previousId = Id;
                using (Snapshot.Lock())
                {
                    Id = NextSnapshotId++;
                    OpenSnapshots = OpenSnapshots.Set(Id);
                }
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
                using (Lock())
                {
                    if (Modified != null)
                        foreach (IStateObject state in Modified)
                            for (StateRecord? record = state.FirstStateRecord; record != null; record = record.Next)
                                if (record.SnapshotId == Id || PreviousIds.Get(record.SnapshotId)) record.SnapshotId = SnapshotId.Invalid;
                    CloseLocked();
                }
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
            MutableSnapshot target)
        {
            ValidateOpen(snapshot);
            if (target.Disposed || target.Applied) return SnapshotApplyResult.Failure("The parent snapshot is closed.");
            List<(IStateObject State, StateRecord Record)> writes = new List<(IStateObject, StateRecord)>();
            foreach (IStateObject state in modified)
            {
                StateRecord? appliedRecord = ReadableSilent(state.FirstStateRecord, snapshot.Id, snapshot.Invalid);
                if (appliedRecord == null) continue;
                StateRecord? current = ReadableSilent(state.FirstStateRecord, target.Id, target.Invalid.Set(snapshot.Id).Or(snapshot.PreviousIds));
                StateRecord? previous = ReadableSilent(state.FirstStateRecord, snapshot._initialId - 1, snapshot._initialInvalid);
                StateRecord selected = appliedRecord;
                if (current != null && previous != null && current.SnapshotId != previous.SnapshotId)
                {
                    StateRecord? merged = state.MergeRecords(previous, current, appliedRecord);
                    if (merged == null)
                        return SnapshotApplyResult.Failure("Conflicting writes to " + state.GetType().Name);
                    selected = merged;
                }
                StateRecord copy = selected.Create();
                copy.Assign(selected);
                copy.SnapshotId = target.Id;
                writes.Add((state, copy));
            }
            // Validate every merge before publishing any of the writes.
            foreach ((IStateObject state, StateRecord record) in writes)
            {
                record.Next = state.FirstStateRecord;
                state.PrependStateRecord(record);
            }
            return SnapshotApplyResult.Success;
        }

    }
}
