using System.Linq;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests
{
    public class SnapshotIdSetTests
    {
        [Fact]
        public void Empty_IsEmpty_ReturnsTrue()
        {
            Assert.True(SnapshotIdSet.Empty.IsEmpty);
        }

        [Fact]
        public void Set_OnEmpty_GetReturnsTrue()
        {
            var set = SnapshotIdSet.Empty.Set(5);
            Assert.True(set.Get(5));
            Assert.False(set.Get(6));
        }

        [Fact]
        public void Clear_RemovesId()
        {
            var set = SnapshotIdSet.Empty.Set(5).Set(10).Clear(5);
            Assert.False(set.Get(5));
            Assert.True(set.Get(10));
        }

        [Fact]
        public void Set_ReturnsNewInstance()
        {
            var a = SnapshotIdSet.Empty;
            var b = a.Set(3);
            Assert.NotSame(a, b);
            Assert.False(a.Get(3));
            Assert.True(b.Get(3));
        }

        [Fact]
        public void AndNot_RemovesOverlap()
        {
            var a = SnapshotIdSet.Empty.Set(1).Set(2).Set(3);
            var b = SnapshotIdSet.Empty.Set(2).Set(4);
            var result = a.AndNot(b);
            Assert.True(result.Get(1));
            Assert.False(result.Get(2));
            Assert.True(result.Get(3));
            Assert.False(result.Get(4));
        }

        [Fact]
        public void And_Intersection()
        {
            var a = SnapshotIdSet.Empty.Set(1).Set(2).Set(3);
            var b = SnapshotIdSet.Empty.Set(2).Set(4);
            var result = a.And(b);
            Assert.False(result.Get(1));
            Assert.True(result.Get(2));
            Assert.False(result.Get(3));
            Assert.False(result.Get(4));
        }

        [Fact]
        public void Or_Union()
        {
            var a = SnapshotIdSet.Empty.Set(1).Set(2);
            var b = SnapshotIdSet.Empty.Set(3).Set(4);
            var result = a.Or(b);
            Assert.True(result.Get(1));
            Assert.True(result.Get(2));
            Assert.True(result.Get(3));
            Assert.True(result.Get(4));
        }

        [Fact]
        public void Lowest_ReturnsMinimum()
        {
            var set = SnapshotIdSet.Empty.Set(10).Set(5).Set(7);
            Assert.Equal(5L, set.Lowest());
        }

        [Fact]
        public void Lowest_Empty_ReturnsDefault()
        {
            Assert.Equal(0L, SnapshotIdSet.Empty.Lowest());
        }

        [Fact]
        public void AddRange_BulkAdd()
        {
            var set = SnapshotIdSet.Empty.AddRange(5, 10);
            Assert.False(set.Get(4));
            for (long i = 5; i < 10; i++)
                Assert.True(set.Get(i));
            Assert.False(set.Get(10));
        }

        [Fact]
        public void Enumeration_YieldsAllIds()
        {
            var set = SnapshotIdSet.Empty.Set(3).Set(7).Set(129);
            var ids = set.ToList();
            Assert.Contains(3L, ids);
            Assert.Contains(7L, ids);
            Assert.Contains(129L, ids);
            Assert.Equal(3, ids.Count);
        }


    }
}
