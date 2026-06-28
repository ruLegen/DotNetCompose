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
                WithCurrent(record =>
                {
                    if (!Policy.Equivalent(record.Value, value))
                    {
                        Overwritable(record, rec =>
                        {
                            rec.Value = value;
                            return rec;
                        });
                    }
                    return record;
                });
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
            return WithCurrent(rec => $"MutableState(value={rec.Value})#{GetHashCode()}");
        }

        private StateStateRecord Readable()
        {
            var snapshot = Snapshot.Current;
            snapshot.ReadObserver?.Invoke(this);
            var result = Snapshot.ReadableSilent(_next, snapshot.Id, snapshot.Invalid);
            if (result != null)
            {
                var readableObserver = snapshot.ReadObserver;
                return result;
            }

            return Snapshot.Sync(() =>
            {
                var syncSnapshot = Snapshot.Current;
                var result = Snapshot.ReadableSilent<StateStateRecord>(
                    _next, syncSnapshot.Id, syncSnapshot.Invalid);
                if (result != null) return result;
                Snapshot.ReadError<StateStateRecord>();
                return null!;
            });
        }

        private R WithCurrent<R>(Func<StateStateRecord, R> block)
        {
            var snapshot = Snapshot.Current;
            var record = Snapshot.ReadCurrent(_next, snapshot);
            return block(record);
        }

        private R Overwritable<R>(StateStateRecord candidate, Func<StateStateRecord, R> block)
        {
            var snapshot = Snapshot.Current;
            return Snapshot.Sync(() =>
            {
                snapshot = Snapshot.Current;
                var rec = OverwritableRecord(snapshot, candidate);
                var result = block(rec);
                return result;
            }).Also(val =>
            {
                Snapshot.NotifyWrite(snapshot, this);
            });
        }

        private StateStateRecord OverwritableRecord(Snapshot snapshot, StateStateRecord candidate)
        {
            if (snapshot.ReadOnly)
                snapshot.RecordModified(this);

            var id = snapshot.Id;
            if (candidate.SnapshotId == id)
                return candidate;

            var newData = Snapshot.Sync(() => NewOverwritableRecordLocked());
            newData.SnapshotId = id;
            snapshot.RecordModified(this);
            return newData;
        }

        private StateStateRecord NewOverwritableRecordLocked()
        {
            var used = Snapshot.UsedLocked(this);
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
