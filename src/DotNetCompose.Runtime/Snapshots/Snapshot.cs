using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots.Utils;

namespace DotNetCompose.Runtime.Snapshots
{
    public abstract class Snapshot : IDisposable
    {
        private static readonly object _lock = new();
        private static readonly AsyncLocal<Stack<Snapshot?>> _threadSnapshot = new();

        internal static long NextSnapshotId
        {
            get => _nextSnapshotId;
            set => _nextSnapshotId = value;
        }

        private static long _nextSnapshotId = SnapshotId.Initial + 1;

        internal readonly struct LockStruct : IDisposable
        {
            private readonly object _toRelease;
            internal LockStruct(object lockObj) { _toRelease = lockObj; Monitor.Enter(lockObj); }
            public void Dispose() => Monitor.Exit(_toRelease);
        }

        internal static LockStruct Lock() => new LockStruct(_lock);
        internal static MutableSnapshot GlobalSnapshot;
        internal static SnapshotIdSet OpenSnapshots { get; set; } = SnapshotIdSet.Empty;
        internal static SnapshotDoubleIndexHeap PinningTable { get; } = new();
        internal static readonly List<Action<HashSet<IStateObject>, Snapshot>> ApplyObservers = new();
        internal static readonly List<Action<object>> GlobalWriteObservers = new();
        internal static int PendingApplyObserverCount;

        static Snapshot()
        {
            GlobalSnapshot = new MutableSnapshot(
                NextSnapshotId++, SnapshotIdSet.Empty, null, null);
            OpenSnapshots = OpenSnapshots.Set(GlobalSnapshot.Id);
        }

        public long Id { get; set; }
        internal SnapshotIdSet Invalid { get; set; } = SnapshotIdSet.Empty;
        internal SnapshotIdSet PreviousIds { get; set; } = SnapshotIdSet.Empty;
        internal bool Disposed { get; set; }
        internal int _pinningTrackingHandle = -1;
        internal bool IsPinned => _pinningTrackingHandle >= 0;

        public abstract bool ReadOnly { get; }
        public abstract Snapshot Root { get; }

        internal Snapshot(long id, SnapshotIdSet invalid)
        {
            Id = id;
            Invalid = invalid;
            if (id != SnapshotId.Invalid)
            {
                var pinned = invalid.IsEmpty ? id : invalid.Lowest(id);
                using (Lock()) _pinningTrackingHandle = PinningTable.Add(pinned);
            }
        }

        public T Enter<T>(Func<T> block)
        {
            var previous = Push();
            try { return block(); }
            finally { Pop(previous); }
        }

        public void Enter(Action block)
        {
            var previous = Push();
            try { block(); }
            finally { Pop(previous); }
        }

        internal virtual Snapshot? Push()
        {
            var stack = _threadSnapshot.Value;
            if (stack == null)
            {
                stack = new Stack<Snapshot?>();
                _threadSnapshot.Value = stack;
            }
            var previous = stack.Count > 0 ? stack.Peek() : null;
            stack.Push(this);
            return previous;
        }

        internal virtual void Pop(Snapshot? previous)
        {
            var stack = _threadSnapshot.Value;
            if (stack != null && stack.Count > 0)
                stack.Pop();
        }

        public static Snapshot Current
        {
            get
            {
                var stack = _threadSnapshot.Value;
                if (stack != null && stack.Count > 0)
                    return stack.Peek()!;
                return GlobalSnapshot;
            }
        }

        public static bool IsInSnapshot
        {
            get
            {
                var stack = _threadSnapshot.Value;
                return stack != null && stack.Count > 0;
            }
        }

        public abstract Snapshot TakeNestedSnapshot(Action<object>? readObserver = null);
        public virtual Snapshot TakeNestedSnapshot(Action<object>? readObserver, Action<object>? writeObserver)
            => TakeNestedSnapshot(readObserver);
        public abstract bool HasPendingChanges();
        internal abstract void RecordModified(IStateObject state);
        internal abstract void NotifyObjectsInitialized();
        internal abstract Action<object>? ReadObserver { get; set; }
        internal abstract Action<object>? WriteObserver { get; set; }
        internal abstract int WriteCountValue { get; set; }
        internal abstract HashSet<IStateObject>? Modified { get; set; }
        internal virtual bool CanBeReused => false;
        internal abstract void CloseLocked();

        public static Snapshot TakeSnapshot(Action<object>? readObserver = null)
        {
            return Current.TakeNestedSnapshot(readObserver);
        }

        public static MutableSnapshot TakeMutableSnapshot(
            Action<object>? readObserver = null,
            Action<object>? writeObserver = null)
        {
            if (Current is MutableSnapshot ms)
                return ms.TakeNestedMutableSnapshot(readObserver, writeObserver);
            throw new InvalidOperationException("Cannot create a mutable snapshot from a read-only snapshot");
        }

        public static void SendApplyNotifications()
        {
            bool hasPending;
            lock (_lock) { hasPending = GlobalSnapshot.HasPendingChanges(); }
            if (hasPending) AdvanceGlobalSnapshot();
        }

        internal static void AdvanceGlobalSnapshot()
        {
            var previousGlobal = GlobalSnapshot;
            HashSet<IStateObject>? modified = null;

            using (Lock())
            {
                previousGlobal = GlobalSnapshot;
                modified = previousGlobal.Modified;

                var globalId = NextSnapshotId++;
                OpenSnapshots = OpenSnapshots.Clear(previousGlobal.Id);
                GlobalSnapshot = new MutableSnapshot(globalId, OpenSnapshots, null, null);
                previousGlobal.Dispose();
                OpenSnapshots = OpenSnapshots.Set(globalId);
            }

            if (modified != null && modified.Count > 0)
            {
                PendingApplyObserverCount++;
                try
                {
                    var observers = ApplyObservers.ToArray();
                    foreach (var obs in observers)
                    {
                        try { obs(modified, previousGlobal); }
                        catch { }
                    }
                }
                finally
                {
                    PendingApplyObserverCount--;
                }
            }

            using (Lock())
            {
                CheckAndOverwriteUnusedRecordsLocked();
                if (modified != null)
                    foreach (var state in modified)
                        ProcessForUnusedRecordsLocked(state);
            }
        }

        public static ObserverHandle RegisterApplyObserver(
            Action<HashSet<IStateObject>, Snapshot> observer)
        {
            AdvanceGlobalSnapshot();
            lock (_lock) { ApplyObservers.Add(observer); }
            return new ObserverHandle(() =>
            {
                lock (_lock) { ApplyObservers.Remove(observer); }
            });
        }

        internal static ObserverHandle RegisterApplyObserver(
            Func<HashSet<IStateObject>, Snapshot, bool> predicate,
            Action<HashSet<IStateObject>, Snapshot> handler)
        {
            var observer = new Action<HashSet<IStateObject>, Snapshot>((states, snapshot) =>
            {
                if (predicate(states, snapshot))
                    handler(states, snapshot);
            });

            AdvanceGlobalSnapshot();
            lock (_lock) { ApplyObservers.Add(observer); }
            return new ObserverHandle(() =>
            {
                lock (_lock) { ApplyObservers.Remove(observer); }
            });
        }

        internal static void RegisterStateToScopeMapping(
            IStateObject state,
            RecomposeScopeImpl scope)
        {
            if (_stateToScopes == null)
                _stateToScopes = new Dictionary<IStateObject, List<WeakReference<RecomposeScopeImpl>>>();

            if (!_stateToScopes.ContainsKey(state))
                _stateToScopes[state] = new List<WeakReference<RecomposeScopeImpl>>();

            _stateToScopes[state].Add(new WeakReference<RecomposeScopeImpl>(scope));
        }

        internal static void InvalidateScopesForState(IStateObject state)
        {
            lock (_lock)
            {
                if (_stateToScopes != null && _stateToScopes.TryGetValue(state, out var scopes))
                {
                    foreach (var weakRef in scopes)
                        if (weakRef.TryGetTarget(out var scope))
                            scope.Invalidate();
                }
            }
        }

        internal static Action<object>? SnapshotReadObserver
        {
            get => _snapshotReadObserver;
            set => _snapshotReadObserver = value;
        }

        private static Action<object>? _snapshotReadObserver;

        private static Dictionary<IStateObject, List<WeakReference<RecomposeScopeImpl>>>? _stateToScopes;

        internal static void ObserveReads(
            Action<object>? readObserver,
            Action<object>? writeObserver,
            Action block)
        {
            if (readObserver == null && writeObserver == null)
            {
                block();
                return;
            }

            var previous = _threadSnapshot.Value?.Count > 0 ? _threadSnapshot.Value.Peek() : null;

            if (previous is TransparentObserverMutableSnapshot toms && toms.CanBeReused)
            {
                var prevRead = toms.ReadObserver;
                var prevWrite = toms.WriteObserver;
                try
                {
                    toms.ReadObserver = MergeReadObserver(readObserver, prevRead);
                    toms.WriteObserver = MergeWriteObserver(writeObserver, prevWrite);
                    block();
                }
                finally
                {
                    toms.ReadObserver = prevRead;
                    toms.WriteObserver = prevWrite;
                }
            }
            else if (previous is TransparentObserverSnapshot tos && tos.CanBeReused && readObserver != null)
            {
                var prevRead = tos.ReadObserver;
                try
                {
                    tos.ReadObserver = MergeReadObserver(readObserver, prevRead);
                    block();
                }
                finally
                {
                    tos.ReadObserver = prevRead;
                }
            }
            else
            {
                MutableSnapshot? snapshot;
                if (previous == null || previous is MutableSnapshot)
                {
                    snapshot = new TransparentObserverMutableSnapshot(
                        previous as MutableSnapshot,
                        readObserver, writeObserver,
                        mergeParentObservers: true,
                        ownsParentSnapshot: false);
                }
                else if (readObserver == null)
                {
                    return;
                }
                else
                {
                    previous.TakeNestedSnapshot(readObserver, writeObserver).Enter(block);
                    return;
                }

                try { snapshot!.Enter(block); }
                finally { snapshot!.Dispose(); }
            }
        }

        public static void Observe(
            Action<object>? readObserver,
            Action<object>? writeObserver,
            Action block)
        {
            Observe(() => { block(); return 0; }, readObserver, writeObserver);
        }

        public static T Observe<T>(
            Func<T> block,
            Action<object>? readObserver,
            Action<object>? writeObserver)
        {
            if (readObserver == null && writeObserver == null)
                return block();

            var previous = _threadSnapshot.Value?.Count > 0 ? _threadSnapshot.Value.Peek() : null;

            if (previous is TransparentObserverMutableSnapshot toms && toms.CanBeReused)
            {
                var prevRead = toms.ReadObserver;
                var prevWrite = toms.WriteObserver;
                try
                {
                    toms.ReadObserver = MergeReadObserver(readObserver, prevRead);
                    toms.WriteObserver = MergeWriteObserver(writeObserver, prevWrite);
                    return block();
                }
                finally
                {
                    toms.ReadObserver = prevRead;
                    toms.WriteObserver = prevWrite;
                }
            }
            else if (previous is TransparentObserverSnapshot tos && tos.CanBeReused && readObserver != null)
            {
                var prevRead = tos.ReadObserver;
                try
                {
                    tos.ReadObserver = MergeReadObserver(readObserver, prevRead);
                    return block();
                }
                finally
                {
                    tos.ReadObserver = prevRead;
                }
            }
            else
            {
                MutableSnapshot? snapshot;
                if (previous == null || previous is MutableSnapshot)
                {
                    snapshot = new TransparentObserverMutableSnapshot(
                        previous as MutableSnapshot,
                        readObserver, writeObserver,
                        mergeParentObservers: true,
                        ownsParentSnapshot: false);
                }
                else if (readObserver == null)
                {
                    return block();
                }
                else
                {
                    return previous.TakeNestedSnapshot(readObserver, writeObserver).Enter(block);
                }

                try { return snapshot!.Enter(block); }
                finally { snapshot!.Dispose(); }
            }
        }

        public static void NotifyAllObjectsInitialized()
        {
            Current.NotifyObjectsInitialized();
        }

        internal static T TakeNewGlobalSnapshot<T>(
            MutableSnapshot previousGlobal,
            Func<SnapshotIdSet, T> block)
        {
            var result = block(OpenSnapshots.Clear(previousGlobal.Id));
            using (Lock())
            {
                var globalId = NextSnapshotId++;
                OpenSnapshots = OpenSnapshots.Clear(previousGlobal.Id);
                GlobalSnapshot = new MutableSnapshot(globalId, OpenSnapshots, null, null);
                previousGlobal.Dispose();
                OpenSnapshots = OpenSnapshots.Set(globalId);
            }
            return result;
        }

        internal static void CheckAndOverwriteUnusedRecordsLocked()
        {
        }

        internal static void ProcessForUnusedRecordsLocked(IStateObject state)
        {
        }

        internal static T? ReadableSilent<T>(T record, long snapshotId, SnapshotIdSet invalid)
            where T : StateRecord
        {
            return (T?)ReadableSilent((StateRecord)record, snapshotId, invalid);
        }

        internal static StateRecord? ReadableSilent(StateRecord record, long snapshotId, SnapshotIdSet invalid)
        {
            var current = record;
            while (current != null)
            {
                if (!invalid.Get(current.SnapshotId) && current.SnapshotId <= snapshotId)
                {
                    var isInvalid = current.SnapshotId != SnapshotId.Invalid && current.SnapshotId <= snapshotId && invalid.Get(current.SnapshotId);
                    if (!isInvalid)
                        return current;
                }
                current = current.Next;
            }
            return null;
        }



        internal static T ReadCurrent<T>(T record, Snapshot snapshot)
            where T : StateRecord
        {
            return (T)ReadCurrent((StateRecord)record, snapshot);
        }

        internal static T ReadCurrent<T>(T record)
            where T : StateRecord
        {
            return (T)ReadCurrent((StateRecord)record, Current);
        }

        internal static StateRecord ReadCurrent(StateRecord record, Snapshot snapshot)
        {
            var result = ReadableSilent(record, snapshot.Id, snapshot.Invalid);
            return result ?? record;
        }

        internal static StateRecord? TryFindReusableRecord(IStateObject state)
        {
            var current = state.FirstStateRecord;
            while (current != null)
            {
                if (current.SnapshotId == long.MaxValue)
                    return current;
                current = current.Next;
            }
            return null;
        }

        internal static void NotifyWrite(Snapshot snapshot, IStateObject state)
        {
            snapshot.WriteObserver?.Invoke(state);
        }

        internal static void MakeCurrentNonObservable(
            Snapshot previous,
            out Snapshot? nonObservable)
        {
            var observer = previous.ReadObserver;
            if (previous is TransparentObserverMutableSnapshot toms && toms.CanBeReused)
            {
                toms.ReadObserver = null;
                nonObservable = toms;
            }
            else if (previous is TransparentObserverSnapshot tos && tos.CanBeReused)
            {
                tos.ReadObserver = null;
                nonObservable = tos;
            }
            else
            {
                nonObservable = previous.TakeNestedSnapshot(null);
                nonObservable.Push();
            }
        }

        internal static void RestoreNonObservable(
            Snapshot previous,
            Snapshot nonObservable,
            Action<object>? observer)
        {
            if (ReferenceEquals(previous, nonObservable) && nonObservable is TransparentObserverMutableSnapshot toms)
            {
                toms.ReadObserver = observer;
            }
            else if (ReferenceEquals(previous, nonObservable) && nonObservable is TransparentObserverSnapshot tos)
            {
                tos.ReadObserver = observer;
            }
            else
            {
                nonObservable.Pop(previous);
                nonObservable.Dispose();
            }
        }

        public static void WithoutReadObservation(Action block)
        {
            var previous = CurrentThreadSnapshot;
            var observer = previous.ReadObserver;
            MakeCurrentNonObservable(previous, out var nonObservable);
            try { block(); }
            finally { RestoreNonObservable(previous, nonObservable, observer); }
        }

        public static Snapshot CurrentThreadSnapshot
        {
            get
            {
                var stack = _threadSnapshot.Value;
                return stack?.Count > 0 ? stack.Peek()! : Current;
            }
        }

        internal static Action<object>? MergeReadObserver(
            Action<object>? readObserver,
            Action<object>? parentReadObserver,
            bool merge = true)
        {
            var parent = merge ? parentReadObserver : null;
            if (readObserver != null && parent != null && readObserver != parent)
                return state => { readObserver(state); parent(state); };
            return readObserver ?? parent;
        }

        internal static Action<object>? MergeWriteObserver(
            Action<object>? writeObserver,
            Action<object>? parentWriteObserver)
        {
            if (writeObserver != null && parentWriteObserver != null && writeObserver != parentWriteObserver)
                return state => { writeObserver(state); parentWriteObserver(state); };
            return writeObserver ?? parentWriteObserver;
        }

        public virtual void Dispose()
        {
            if (!Disposed)
            {
                Disposed = true;
                using (Lock())
                {
                    ReleasePinnedSnapshotLocked();
                }
            }
        }

        internal void ReleasePinnedSnapshotLocked()
        {
            if (_pinningTrackingHandle >= 0)
            {
                PinningTable.Remove(_pinningTrackingHandle);
                _pinningTrackingHandle = -1;
            }
        }
    }
}