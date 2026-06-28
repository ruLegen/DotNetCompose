using System.Collections.Generic;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests
{
    public class SnapshotMutableStateTests
    {
        [Fact]
        public void Constructor_SetsInitialValue()
        {
            var state = new SnapshotMutableState<int>(42, StructuralPolicy<int>.Default);
            Assert.Equal(42, state.Value);
        }

        [Fact]
        public void SetValue_GetValue_ReturnsUpdated()
        {
            var state = new SnapshotMutableState<string>("hello", StructuralPolicy<string>.Default);
            using var mutable = Snapshot.TakeMutableSnapshot();
            mutable.Enter(() => state.Value = "world");
            mutable.Apply();
            Assert.Equal("world", state.Value);
        }

        [Fact]
        public void SetValue_SameValue_StructuralPolicy_DoesNotCreateNewRecord()
        {
            var state = new SnapshotMutableState<int>(5, StructuralPolicy<int>.Default);
            var recordBefore = state.FirstStateRecord;

            state.Value = 5;

            Assert.Same(recordBefore, state.FirstStateRecord);
        }

        [Fact]
        public void SetValue_NeverEqualPolicy_AlwaysCreatesNewRecord()
        {
            var state = new SnapshotMutableState<int>(0, NeverEqualPolicy<int>.Default);
            var recordBefore = state.FirstStateRecord;

            using var mutable = Snapshot.TakeMutableSnapshot();
            mutable.Enter(() => state.Value = 0);
            mutable.Apply();

            Assert.NotSame(recordBefore, state.FirstStateRecord);
            Assert.Equal(0, state.Value);
        }

        [Fact]
        public void ReadObserver_FiresOnGet()
        {
            var state = new SnapshotMutableState<int>(10, StructuralPolicy<int>.Default);
            var readObjects = new List<object>();

            Snapshot.Observe(
                readObserver: obj => readObjects.Add(obj!),
                writeObserver: null,
                block: () => { var _ = state.Value; }
            );

            Assert.Contains(state, readObjects);
        }

        [Fact]
        public void WriteObserver_FiresOnSet()
        {
            var state = new SnapshotMutableState<int>(1, NeverEqualPolicy<int>.Default);
            var writeObjects = new List<object>();

            Snapshot.Observe(
                readObserver: null,
                writeObserver: obj => writeObjects.Add(obj!),
                block: () => { state.Value = 2; }
            );

            Assert.Contains(state, writeObjects);
        }

        [Fact]
        public void MultipleWrites_CreatesRecordChain()
        {
            var state = new SnapshotMutableState<int>(0, NeverEqualPolicy<int>.Default);

            using (var m1 = Snapshot.TakeMutableSnapshot())
            {
                m1.Enter(() => state.Value = 1);
                m1.Apply();
            }

            using (var m2 = Snapshot.TakeMutableSnapshot())
            {
                m2.Enter(() => state.Value = 2);
                m2.Apply();
            }

            Assert.Equal(2, state.Value);
            // The chain should have multiple records (initial + 2 writes)
            var count = 0;
            var rec = state.FirstStateRecord;
            while (rec != null)
            {
                count++;
                rec = rec.Next;
            }
            Assert.True(count >= 3);
        }
    }
}
