using DotNetCompose.Runtime.SlotTable;
using DotNetCompose.Runtime.SlotTable.GapBuffer;

namespace DotNetCompose.Runtime.Tests;

public class SlotEditingTests
{
    [Fact]
    public void MovesKeepTrackedAndUntrackedElementsAcrossGapAndGrowth()
    {
        var map = new SlotMapGapBuffer<int>(2);
        var expected = new List<int>();
        var anchors = new Dictionary<int, GapBufferItemAnchor>();
        for (int i = 0; i < 100; i++)
        {
            if (i % 2 == 0) anchors.Add(i, map.InsertTrackedAt(0, i)); else map.InsertAt(0, i);
            expected.Insert(0, i);
        }
        var random = new Random(25);
        for (int iteration = 0; iteration < 150; iteration++)
        {
            int from = random.Next(expected.Count);
            int count = random.Next(1, expected.Count - from + 1);
            int to = random.Next(expected.Count + 1);
            if (to > from && to < from + count) continue;
            var moved = expected.GetRange(from, count);
            expected.RemoveRange(from, count);
            expected.InsertRange(to > from ? to - count : to, moved);
            map.MoveRange(from, to, count);
            for (int i = 0; i < expected.Count; i++) Assert.Equal(expected[i], map.GetAt(i));
            foreach (var (value, anchor) in anchors)
            { Assert.Equal(value, map.Get(anchor)); Assert.Equal(expected.IndexOf(value), map.IndexOf(anchor)); }
        }
    }

    [Fact]
    public void ImportMoveDeleteAndTrimPreserveMetadataAndRejectDeletedAnchor()
    {
        var table = new ComposerSlotTable();
        using (var writer = table.OpenWriter())
        {
            writer.StartGroup(1);
            writer.StartGroup(2, "key", "aux"); writer.AppendSlot("value"); writer.EndGroup();
            writer.StartGroup(3); writer.EndGroup();
            writer.StartNode(4, "node"); writer.StartNode(5, "nested"); writer.EndGroup(); writer.EndGroup();
            writer.EndGroup();
        }
        GroupAnchor root, data, empty, node;
        using (var reader = table.OpenReader())
        { root = reader.Anchor(0); data = reader.Anchor(1); empty = reader.Anchor(2); node = reader.Anchor(3); }
        using (var writer = table.OpenWriter())
        {
            writer.MoveGroup(node, 1);
            writer.UpdateAux(data, "updated");
            writer.TrimSlots(data, 0);
            writer.AppendSlot(empty, null);
            writer.TrimSlots(empty, 0);
            writer.AppendSlot(empty, "first");
            writer.AppendSlot(root, "after children");
            writer.MoveGroup(node, 5);
        }
        using (var reader = table.OpenReader())
        {
            Assert.Equal(data, reader.Anchor(1));
            Assert.Equal("updated", reader.GetGroupAux(1));
            Assert.Equal("key", reader.GetGroupObjectKey(1));
            Assert.Equal(0, reader.GetSlotSize(1));
            Assert.Equal("first", reader.GroupGet(2, 0));
            Assert.Equal("after children", reader.GroupGet(0, 0));
            Assert.Equal(1, reader.GetNodeCount(0));
            Assert.Equal(1, reader.GetNodeCount(3));
        }
        var source = new ComposerSlotTable();
        using (var writer = source.OpenWriter())
        { writer.StartGroup(6); writer.StartNode(7, "imported"); writer.EndGroup(); writer.EndGroup(); }
        GroupAnchor imported;
        using (var reader = source.OpenReader()) imported = reader.Anchor(0);
        using (var writer = table.OpenWriter())
        {
            writer.RemoveGroup(data);
            var mapping = writer.ImportGroup(source, imported, 1, root);
            Assert.NotEqual(data, mapping[imported]);
            Assert.Equal(table, mapping[imported].Owner);
            Assert.Equal(source, imported.Owner);
            Assert.Throws<InvalidOperationException>(() => writer.RemoveGroup(data));
            Assert.Throws<ArgumentException>(() => writer.RemoveGroup(imported));
        }
        using (var reader = table.OpenReader())
        {
            Assert.Equal(0, reader.GetGroupKey(data));
            Assert.Equal(2, reader.GetNodeCount(0));
            Assert.Equal(6, reader.GetGroupSize(0));
            Assert.Equal("imported", reader.GetNode(2));
        }
    }

    [Fact]
    public void GroupAnchorsRejectAnotherTableEvenWhenStorageIdsCoincide()
    {
        ComposerSlotTable first = new ComposerSlotTable();
        ComposerSlotTable second = new ComposerSlotTable();
        using (ComposerSlotTable.Writer writer = first.OpenWriter())
        { writer.StartGroup(11); writer.EndGroup(); }
        using (ComposerSlotTable.Writer writer = second.OpenWriter())
        { writer.StartGroup(22); writer.EndGroup(); }

        GroupAnchor firstAnchor;
        GroupAnchor secondAnchor;
        using (ComposerSlotTable.Reader reader = first.OpenReader()) firstAnchor = reader.Anchor(0);
        using (ComposerSlotTable.Reader reader = second.OpenReader()) secondAnchor = reader.Anchor(0);
        Assert.Equal(firstAnchor.Item, secondAnchor.Item);
        Assert.NotEqual(firstAnchor, secondAnchor);

        using (ComposerSlotTable.Reader reader = first.OpenReader())
            Assert.Throws<ArgumentException>(() => reader.GetGroupKey(secondAnchor));
        using (ComposerSlotTable.Writer writer = first.OpenWriter())
        {
            Assert.Throws<ArgumentException>(() => writer.MoveGroup(secondAnchor, 0));
            Assert.Throws<ArgumentException>(() => writer.ImportGroup(second, firstAnchor, 0, GroupAnchor.Empty));
            Assert.Throws<ArgumentException>(() => writer.ImportGroup(second, secondAnchor, 0, secondAnchor));
        }
    }
}
