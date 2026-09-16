using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public class SnapshotCompositionTests
{
    [Fact]
    public void ConflictDoesNotPublishAnyOfTheSnapshotWrites()
    {
        var a = Composables.CreateMutableState(0);
        var b = Composables.CreateMutableState(0);
        using var pending = Snapshot.TakeMutableSnapshot();
        pending.Enter(() => { a.Value = 1; b.Value = 1; });
        b.Value = 2;
        Assert.False(pending.Apply().Succeeded);
        Assert.Equal(0, a.Value);
        Assert.Equal(2, b.Value);
    }

    [Fact]
    public void NestedApplyIsPrivateUntilParentAppliesAndParentCanDiscardIt()
    {
        var state = Composables.CreateMutableState(0);
        using (var parent = Snapshot.TakeMutableSnapshot())
        {
            parent.Enter(() =>
            {
                using var child = Snapshot.TakeMutableSnapshot();
                child.Enter(() => state.Value = 3);
                Assert.True(child.Apply().Succeeded);
                Assert.Equal(3, state.Value);
            });
            Assert.Equal(0, state.Value);
        }
        for (int i = 0; i < 100; i++)
        { using var unrelated = Snapshot.TakeMutableSnapshot(); Assert.True(unrelated.Apply().Succeeded); }
        Assert.Equal(0, state.Value);
    }

    [Fact]
    public void FirstAndRepeatedGlobalWritesAreObservedButPrivateWritesAreNot()
    {
        var writes = new List<object>();
        using var observer = Snapshot.RegisterGlobalWriteObserver(writes.Add);
        var state = Composables.CreateMutableState(0);
        state.Value = 1; state.Value = 2;
        Assert.Equal(2, writes.Count);
        using var snapshot = Snapshot.TakeMutableSnapshot();
        snapshot.Enter(() => state.Value = 3);
        Assert.Equal(2, writes.Count);
        Assert.All(writes, value => Assert.Same(state, value));
    }

    [Fact]
    public void SnapshotCreatedObjectsRemainReadableAfterApply()
    {
        SnapshotMutableState<int>? created = null;
        using var snapshot = Snapshot.TakeMutableSnapshot();
        snapshot.Enter(() => created = Composables.CreateMutableState(17));
        Assert.True(snapshot.Apply().Succeeded);
        Snapshot.SendApplyNotifications();
        Assert.Equal(17, created!.Value);
    }

    [Fact]
    public void IdSetKeepsOldIdsAcrossSignedBitsAndSeveralWindows()
    {
        var expected = new SortedSet<long>();
        SnapshotIdSet set = SnapshotIdSet.Empty;
        foreach (long id in new long[] { 1, 63, 64, 127, 128, 255, 300, 1000, 4000 })
        {
            set = set.Set(id); expected.Add(id);
            Assert.Equal(expected, set);
        }
        Assert.True(set.AndNot(set).IsEmpty);
        var random = new Random(741);
        for (int i = 0; i < 500; i++)
        {
            long id = random.Next(5000);
            if (random.Next(2) == 0) { expected.Add(id); set = set.Set(id); }
            else { expected.Remove(id); set = set.Clear(id); }
            Assert.Equal(expected, set);
        }
    }
}
