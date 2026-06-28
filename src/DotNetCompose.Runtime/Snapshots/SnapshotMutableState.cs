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
            set
            {
                var snapshot = Snapshot.Current;
                var record = Snapshot.ReadCurrent(_next, snapshot);
                if (!Policy.Equivalent(record.Value, value))
                {
                    using (Snapshot.Lock())
                    {
                        snapshot = Snapshot.Current;
                        record = OverwritableRecord(snapshot, record);
                        record.Value = value;
                    }
                    Snapshot.NotifyWrite(snapshot, this);
                }
            }
        }

        public ISnapshotMutationPolicy<T> Policy { get; }

        T IState<T>.Value => Value;

        private StateStateRecord _next;

        public SnapshotMutableState(T value, ISnapshotMutationPolicy<T> policy)
        {
            Policy = policy;
            _next = new StateStateRecord(Snapshot.Current.Id, value);
        }
        public void PrependStateRecord(StateRecord value)
        {
            _next = (StateStateRecord)value;
        }

        public StateRecord? MergeRecords(StateRecord previous, StateRecord current, StateRecord applied)
        {
            var prevR = (StateStateRecord)previous;
            var curR = (StateStateRecord)current;
            var appR = (StateStateRecord)applied;

            if (Policy.Equivalent(curR.Value, appR.Value))
                return current;

            var merged = Policy.Merge(prevR.Value, curR.Value, appR.Value);
            if (merged != null)
            {
                var result = (StateStateRecord)appR.Create();
                result.Value = merged;
                return result;
            }
            return null;
        }

        public override string ToString()
        {
            var snapshot = Snapshot.Current;
            var record = Snapshot.ReadCurrent(_next, snapshot);
            return $"MutableState(value={record.Value})#{GetHashCode()}";
        }

        private StateStateRecord Readable()
        {
            var snapshot = Snapshot.Current;
            snapshot.ReadObserver?.Invoke(this);
            var result = Snapshot.ReadableSilent(_next, snapshot.Id, snapshot.Invalid);
            if (result != null)
                return result;

            using (Snapshot.Lock())
            {
                var syncSnapshot = Snapshot.Current;
                var lockedResult = Snapshot.ReadableSilent<StateStateRecord>(
                    _next, syncSnapshot.Id, syncSnapshot.Invalid);
                if (lockedResult != null) return lockedResult;
                throw new InvalidOperationException("Readable snapshot record not found");
            }
        }

        private StateStateRecord OverwritableRecord(Snapshot snapshot, StateStateRecord candidate)
        {
            if (snapshot.ReadOnly)
                snapshot.RecordModified(this);

            var id = snapshot.Id;
            if (candidate.SnapshotId == id)
                return candidate;

            StateStateRecord newData;
            using (Snapshot.Lock()) newData = NewOverwritableRecordLocked();
            newData.SnapshotId = id;
            snapshot.RecordModified(this);
            return newData;
        }

        private StateStateRecord NewOverwritableRecordLocked()
        {
            var used = Snapshot.TryFindReusableRecord(this);
            if (used != null)
            {
                _next.SnapshotId = long.MaxValue;
                return (StateStateRecord)used;
            }

            var rec = new StateStateRecord(long.MaxValue, default!)
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
                SnapshotId = value.SnapshotId;
            }

            public override StateRecord Create()
            {
                return new StateStateRecord(Snapshot.Current.Id, Value);
            }
        }
    }
}
