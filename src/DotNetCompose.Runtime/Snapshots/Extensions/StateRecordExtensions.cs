using System;

namespace DotNetCompose.Runtime.Snapshots
{
    internal static class StateRecordExtensions
    {
        public static R WithCurrent<T, R>(this T record, Func<T, R> block)
            where T : StateRecord
        {
            return block(Snapshot.ReadCurrent(record));
        }

        public static T Readable<T>(this T record, IStateObject state)
            where T : StateRecord
        {
            var snapshot = Snapshot.Current;
            snapshot.ReadObserver?.Invoke(state);
            return Snapshot.ReadableSilent(record, snapshot.Id, snapshot.Invalid)
                ?? Snapshot.Sync(() =>
                {
                    var syncSnapshot = Snapshot.Current;
                    return Snapshot.ReadableSilent<T>(
                        (T)state.FirstStateRecord,
                        syncSnapshot.Id,
                        syncSnapshot.Invalid)
                    ?? Snapshot.ReadError<T>();
                });
        }

        public static T Readable<T>(this T record, IStateObject state, Snapshot snapshot)
            where T : StateRecord
        {
            snapshot.ReadObserver?.Invoke(state);
            return Snapshot.ReadableSilent(record, snapshot.Id, snapshot.Invalid)
                ?? Snapshot.ReadError<T>();
        }

        internal static StateRecord FindYoungestOr(this StateRecord record,
            Func<StateRecord, bool> predicate)
        {
            var current = record;
            StateRecord youngest = record;

            while (current != null)
            {
                if (predicate(current))
                    return current;

                if (youngest.SnapshotId < current.SnapshotId)
                    youngest = current;

                current = current.Next;
            }

            return youngest;
        }
    }
}
