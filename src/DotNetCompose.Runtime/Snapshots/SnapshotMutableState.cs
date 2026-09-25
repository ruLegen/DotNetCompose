using System;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.Snapshots
{
    public class SnapshotMutableState<T> : IStateObject, ISnapshotMutableState<T>
    {
        public StateRecord FirstStateRecord => _next;

        public T Value
        {
            get => Readable().Value;
            set => SetValueAndReportChange(value);
        }

        public ISnapshotMutationPolicy<T> Policy { get; }

        T IState<T>.Value => Value;

        private StateStateRecord _next;

        public SnapshotMutableState(T value, ISnapshotMutationPolicy<T> policy)
        {
            Policy = policy;
            _next = new StateStateRecord(Snapshot.Current.Id, value);
            if (!ReferenceEquals(Snapshot.Current.Root, Snapshot.GlobalSnapshot)) Snapshot.Current.RecordModified(this);
        }

        internal bool SetValueAndReportChange(T value)
        {
            Snapshot snapshot = Snapshot.Current;
            StateStateRecord record = Snapshot.ReadCurrent(_next, snapshot);
            if (Policy.Equivalent(record.Value, value)) return false;
            using (Snapshot.Lock())
            {
                snapshot = Snapshot.Current;
                record = OverwritableRecord(snapshot, record);
                record.Value = value;
            }
            Snapshot.NotifyWrite(snapshot, this);
            return true;
        }
        public void PrependStateRecord(StateRecord value)
        {
            _next = (StateStateRecord)value;
        }

        public StateRecord? MergeRecords(StateRecord previous, StateRecord current, StateRecord applied)
        {
            StateStateRecord prevR = (StateStateRecord)previous;
            StateStateRecord curR = (StateStateRecord)current;
            StateStateRecord appR = (StateStateRecord)applied;

            if (Policy.Equivalent(curR.Value, appR.Value))
                return current;

            if (Policy.TryMerge(prevR.Value, curR.Value, appR.Value, out T merged))
            {
                StateStateRecord result = (StateStateRecord)appR.Create();
                result.Value = merged;
                return result;
            }
            return null;
        }

        public override string ToString()
        {
            Snapshot snapshot = Snapshot.Current;
            StateStateRecord record = Snapshot.ReadCurrent(_next, snapshot);
            return $"MutableState(value={record.Value})#{GetHashCode()}";
        }

        private StateStateRecord Readable()
        {
            Snapshot snapshot = Snapshot.Current;
            snapshot.ReadObserver?.Invoke(this);
            StateStateRecord? result = Snapshot.ReadableSilent(_next, snapshot.Id, snapshot.Invalid);
            if (result != null)
                return result;

            using (Snapshot.Lock())
            {
                Snapshot syncSnapshot = Snapshot.Current;
                StateStateRecord? lockedResult = Snapshot.ReadableSilent<StateStateRecord>(
                    _next, syncSnapshot.Id, syncSnapshot.Invalid);
                if (lockedResult != null) return lockedResult;
                throw new InvalidOperationException("Readable snapshot record not found");
            }
        }

        private StateStateRecord OverwritableRecord(Snapshot snapshot, StateStateRecord candidate)
        {
            snapshot.RecordModified(this);

            long id = snapshot.Id;
            if (candidate.SnapshotId == id)
                return candidate;

            StateStateRecord newData;
            using (Snapshot.Lock()) newData = NewOverwritableRecordLocked();
            newData.SnapshotId = id;
            return newData;
        }

        private StateStateRecord NewOverwritableRecordLocked()
        {
            StateRecord? used = Snapshot.TryFindReusableRecord(this);
            if (used != null)
            {
                used.SnapshotId = long.MaxValue;
                return (StateStateRecord)used;
            }

            StateStateRecord rec = new StateStateRecord(long.MaxValue, default!)
            {
                Next = _next
            };
            _next = rec;
            return rec;
        }

            private class StateStateRecord : StateRecord
            {
                public T Value = default!;

                public StateStateRecord() : base() { }

            public StateStateRecord(long snapshotId, T value) : base(snapshotId)
            {
                Value = value;
            }

            public override void Assign(StateRecord value)
            {
                Value = ((StateStateRecord)value).Value;
            }

            public override StateRecord Create()
            {
                return new StateStateRecord(Snapshot.Current.Id, Value);
            }
        }
    }
}
