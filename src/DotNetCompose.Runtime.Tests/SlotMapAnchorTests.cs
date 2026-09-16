using DotNetCompose.Runtime.SlotTable.GapBuffer;

namespace DotNetCompose.Runtime.Tests;

public class SlotMapAnchorTests
{
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
        var first = buffer.InsertStable(0, 10);
        var removed = buffer.InsertStable(1, 20);
        var last = buffer.InsertStable(2, 30);
        if (removeByAddress)
            buffer.Remove(buffer.GetAddressOfIndex(1));
        else
            buffer.Remove(removed);
        Assert.False(buffer.IsValidAnchor(removed));
        Assert.Throws<InvalidOperationException>(() => buffer.GetIndexOfAnchor(removed));
        Assert.Throws<InvalidOperationException>(() => buffer.Get(removed));
        var replacement = buffer.InsertStable(0, 40);
        Assert.Equal(removed.Id, replacement.Id);
        Assert.NotEqual(removed.Generation, replacement.Generation);
        Assert.False(buffer.IsValidAnchor(removed));
        Assert.True(buffer.IsValidAnchor(replacement));
        Assert.Throws<InvalidOperationException>(() => buffer.Set(removed, 999));
        Assert.Throws<InvalidOperationException>(() => buffer.Remove(removed));
        Assert.Equal(10, buffer.Get(first));
        Assert.Equal(30, buffer.Get(last));
        Assert.Equal(40, buffer.Get(replacement));
        Assert.Equal(1, buffer.GetIndexOfAnchor(first));
        Assert.Equal(2, buffer.GetIndexOfAnchor(last));
        Assert.Equal(-1, buffer.GetIndexOfAnchor(GapBufferItemAnchor.Empty));
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
                anchors[value] = buffer.InsertStable(index, value);
            else
                buffer.Insert(index, value);
            expected.Insert(index, value);
        }
        for (int i = 0; i < 150; i++)
        {
            int index = random.Next(expected.Count);
            int value = expected[index];
            buffer.Remove(buffer.GetAddressOfIndex(index));
            if (anchors.Remove(value, out var anchor)) Assert.False(buffer.IsValidAnchor(anchor));
            expected.RemoveAt(index);
        }
        for (int i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], buffer.Get(buffer.GetAddressOfIndex(i)));
        foreach (var (value, anchor) in anchors)
        {
            Assert.Equal(value, buffer.Get(anchor));
            Assert.Equal(expected.IndexOf(value), buffer.GetIndexOfAnchor(anchor));
            Assert.Equal(anchor, buffer.Track(buffer.GetAddressOfIndex(expected.IndexOf(value))));
        }
    }
}
