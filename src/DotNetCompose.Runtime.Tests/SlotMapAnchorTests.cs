using DotNetCompose.Runtime.SlotTable.GapBuffer;

namespace DotNetCompose.Runtime.Tests;

public class SlotMapAnchorTests
{
    [Fact]
    public void IndexedAccessUsesLogicalPositionsAndRejectsMissingElements()
    {
        SlotMapGapBuffer<int> buffer = new SlotMapGapBuffer<int>(2);
        GapBufferItemAnchor tracked = buffer.InsertTrackedAt(0, 10);
        buffer.InsertAt(0, 20);
        buffer.InsertAt(1, 30);
        Assert.Equal(new[] { 20, 30, 10 }, Enumerable.Range(0, buffer.Count).Select(buffer.GetAt));
        Assert.Equal(2, buffer.IndexOf(tracked));

        buffer.SetAt(1, 31);
        buffer.RemoveAt(0);
        Assert.Equal(new[] { 31, 10 }, Enumerable.Range(0, buffer.Count).Select(buffer.GetAt));
        Assert.Equal(1, buffer.IndexOf(tracked));

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetAt(buffer.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetAt(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.RemoveAt(buffer.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.InsertAt(buffer.Count + 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.InsertTrackedAt(-1, 0));
    }

    [Fact]
    public void RepeatedTracking_ReusesTheHandle_AndRejectsNonElementAddresses()
    {
        var gap = new GapBuffer<int>(4);
        var map = new GapBufferSlotMap<int>(gap);
        var anchor = map.Insert(1);
        Assert.Equal(anchor, map.Track(gap.AddressOf(0)));
        Assert.Equal(1, map.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Track(1)); // Inside the gap.
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Track(gap.AddressOf(gap.Count)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Insert(-1, 2));
        Assert.Equal(1, gap.Count);
        Assert.Equal(1, map.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedAnchor_IsInvalidBeforeAndAfterReuse(bool removeByAddress)
    {
        var buffer = new SlotMapGapBuffer<int>(2);
        var first = buffer.InsertTrackedAt(0, 10);
        var removed = buffer.InsertTrackedAt(1, 20);
        var last = buffer.InsertTrackedAt(2, 30);
        if (removeByAddress)
            buffer.RemoveAt(1);
        else
            buffer.Remove(removed);
        Assert.False(buffer.IsValidAnchor(removed));
        Assert.Throws<InvalidOperationException>(() => buffer.IndexOf(removed));
        Assert.Throws<InvalidOperationException>(() => buffer.Get(removed));
        var replacement = buffer.InsertTrackedAt(0, 40);
        Assert.Equal(removed.Id, replacement.Id);
        Assert.NotEqual(removed.Generation, replacement.Generation);
        Assert.False(buffer.IsValidAnchor(removed));
        Assert.True(buffer.IsValidAnchor(replacement));
        Assert.Throws<InvalidOperationException>(() => buffer.Set(removed, 999));
        Assert.Throws<InvalidOperationException>(() => buffer.Remove(removed));
        Assert.Equal(10, buffer.Get(first));
        Assert.Equal(30, buffer.Get(last));
        Assert.Equal(40, buffer.Get(replacement));
        Assert.Equal(1, buffer.IndexOf(first));
        Assert.Equal(2, buffer.IndexOf(last));
        Assert.Equal(-1, buffer.IndexOf(GapBufferItemAnchor.Empty));
        Assert.False(buffer.IsValidAnchor(new GapBufferItemAnchor(-1, 1)));
        Assert.False(buffer.IsValidAnchor(new GapBufferItemAnchor(int.MaxValue, 1)));
    }

    [Fact]
    public void MixedTrackedAndUntrackedElements_SurviveMovesAndSeparateArrayGrowth()
    {
        var buffer = new SlotMapGapBuffer<int>(2);
        var expected = new List<int>();
        var anchors = new Dictionary<int, GapBufferItemAnchor>();
        var random = new Random(139);
        for (int value = 0; value < 300; value++)
        {
            int index = random.Next(expected.Count + 1);
            if (value % 3 == 0)
                anchors[value] = buffer.InsertTrackedAt(index, value);
            else
                buffer.InsertAt(index, value);
            expected.Insert(index, value);
        }
        for (int i = 0; i < 150; i++)
        {
            int index = random.Next(expected.Count);
            int value = expected[index];
            buffer.RemoveAt(index);
            if (anchors.Remove(value, out var anchor)) Assert.False(buffer.IsValidAnchor(anchor));
            expected.RemoveAt(index);
        }
        for (int i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], buffer.GetAt(i));
        foreach (var (value, anchor) in anchors)
        {
            Assert.Equal(value, buffer.Get(anchor));
            Assert.Equal(expected.IndexOf(value), buffer.IndexOf(anchor));
            Assert.Equal(anchor, buffer.TrackAt(expected.IndexOf(value)));
        }
    }
}
