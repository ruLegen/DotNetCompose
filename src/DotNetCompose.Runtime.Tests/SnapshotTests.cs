using System.Collections.Generic;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests
{
    public class SnapshotTests
    {
        [Fact]
        public void Current_OutsideEnter_ReturnsGlobal()
        {
            var current = Snapshot.Current;
            Assert.NotNull(current);
            Assert.False(current.ReadOnly);
        }

        [Fact]
        public void Enter_ChangesCurrent_RestoresAfter()
        {
            var before = Snapshot.Current;
            var inside = (Snapshot?)null;

            Snapshot.GlobalSnapshot.Enter(() =>
            {
                inside = Snapshot.Current;
            });

            Assert.NotNull(inside);
            Assert.Equal(Snapshot.GlobalSnapshot, inside);
            Assert.Equal(before, Snapshot.Current);
        }

        [Fact]
        public void Enter_TakesSnapshot_InsideCurrentIsDifferent()
        {
            var state = new SnapshotMutableState<int>(0,
                StructuralPolicy<int>.Default);

            Snapshot.GlobalSnapshot.Enter(() =>
            {
                using var ms = Snapshot.TakeMutableSnapshot();
                Assert.NotEqual(Snapshot.GlobalSnapshot.Id, ms.Id);
            });
        }

        [Fact]
        public void TakeMutableSnapshot_ModifyAndApply_ChangesVisibleAfterNotification()
        {
            var state = new SnapshotMutableState<int>(10,
                NeverEqualPolicy<int>.Default);
            var readValue = 0;

            using var ms = Snapshot.TakeMutableSnapshot();
            ms.Enter(() => { state.Value = 20; });
            ms.Apply();

            Snapshot.SendApplyNotifications();
            readValue = state.Value;

            Assert.Equal(20, readValue);
        }

        [Fact]
        public void ProvideReadObserver_ReceivesReadCallback()
        {
            var state = new SnapshotMutableState<int>(7,
                StructuralPolicy<int>.Default);
            var readObjects = new List<object>();

            Snapshot.Observe(
                readObserver: obj => readObjects.Add(obj!),
                writeObserver: null,
                block: () => { var _ = state.Value; }
            );

            Assert.Contains(state, readObjects);
        }

        [Fact]
        public void ProvideWriteObserver_ReceivesWriteCallback()
        {
            var state = new SnapshotMutableState<int>(0,
                NeverEqualPolicy<int>.Default);
            var writeObjects = new List<object>();

            Snapshot.Observe(
                readObserver: null,
                writeObserver: obj => writeObjects.Add(obj!),
                block: () => { state.Value = 99; }
            );

            Assert.Contains(state, writeObjects);
        }

        [Fact]
        public void WithoutReadObservation_SuppressesReadObserver()
        {
            var state = new SnapshotMutableState<int>(3,
                StructuralPolicy<int>.Default);
            var readObserved = false;

            Snapshot.Observe(
                readObserver: _ => readObserved = true,
                writeObserver: null,
                block: () =>
                {
                    Snapshot.WithoutReadObservation(() =>
                    {
                        var _ = state.Value;
                    });
                }
            );

            Assert.False(readObserved);
        }

        [Fact]
        public void RegisterApplyObserver_FiresOnGlobalAdvance()
        {
            var state = new SnapshotMutableState<int>(1,
                NeverEqualPolicy<int>.Default);
            var observedStates = new List<HashSet<IStateObject>>();

            using var handle = Snapshot.RegisterApplyObserver(
                (states, _) => observedStates.Add(states));

            state.Value = 2;
            Snapshot.SendApplyNotifications();

            Assert.NotEmpty(observedStates);
            Assert.Contains(state, observedStates[0]);
        }

        [Fact]
        public void RegisterApplyObserver_Dispose_Unregisters()
        {
            var state = new SnapshotMutableState<int>(5,
                NeverEqualPolicy<int>.Default);
            var callCount = 0;

            var handle = Snapshot.RegisterApplyObserver(
                (_, _) => callCount++);

            handle.Dispose();

            state.Value = 6;
            Snapshot.SendApplyNotifications();
            var countAfterDispose = callCount;
            Snapshot.SendApplyNotifications();

            Assert.Equal(0, countAfterDispose);
        }

        [Fact]
        public void NestedEnter_PreservesOuterContext()
        {
            var outerSnapshot = (Snapshot?)null;
            var innerSnapshot = (Snapshot?)null;

            Snapshot.GlobalSnapshot.Enter(() =>
            {
                outerSnapshot = Snapshot.Current;
                using var ms = Snapshot.TakeMutableSnapshot();
                ms.Enter(() =>
                {
                    innerSnapshot = Snapshot.Current;
                });
            });

            Assert.NotNull(outerSnapshot);
            Assert.NotNull(innerSnapshot);
            Assert.NotEqual(outerSnapshot!.Id, innerSnapshot!.Id);
        }
    }
}
