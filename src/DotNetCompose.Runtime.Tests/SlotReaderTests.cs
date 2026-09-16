using System.Reflection;
using DotNetCompose.Runtime.SlotTable;
using DotNetCompose.Runtime.SlotTable.GapBuffer;

namespace DotNetCompose.Runtime.Tests;

public class SlotReaderTests
{
    private static ComposerSlotTable Build(Action<ComposerSlotTable.Writer> write)
    {
        var table = new ComposerSlotTable();
        using var writer = table.OpenWriter();
        write(writer);
        return table;
    }

    [Fact]
    public void EmptyTable_HasNoGroupsOrSlots()
    {
        var table = Build(_ => { });
        Assert.Equal(0, Buffer<object?>(table, "_slots").Count);
        using var reader = table.OpenReader();
        Assert.Equal(0, reader.Size);
        Assert.Equal(-1, reader.Parent);
        Assert.Equal(0, reader.ParentNodes);
        Assert.Equal(0, reader.Slot);
        Assert.True(reader.IsGroupEnd);
        Assert.Equal(0, reader.GroupKey);
        Assert.Null(reader.GroupObjectKey);
        Assert.Null(reader.GroupNode);
        Assert.Same(Composables.Empty, reader.Next());
        Assert.Same(ComposerSlotTable.Empty, reader.Get(0));
        Assert.False(reader.HadNext);
        Assert.Empty(reader.ExtractKeys());
        reader.Reposition(reader.Size);
        Assert.Throws<InvalidOperationException>(reader.StartGroup);
        Assert.Throws<InvalidOperationException>(() => reader.SkipGroup());
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Anchor());
    }

    [Fact]
    public void Traversal_RestoresParentSlots_AndSupportsMultipleRoots()
    {
        var table = Build(w =>
        {
            w.StartGroup(10, "root");
            w.AppendSlot("before");
            w.StartGroup(20);
            w.AppendSlot("child");
            w.StartGroup(30);
            w.EndGroup();
            w.EndGroup();
            w.StartNode(40, "node");
            w.AppendSlot("node-slot");
            w.EndGroup();
            w.AppendSlot("after");
            w.EndGroup();
            w.StartGroup(50);
            w.EndGroup();
        });

        using var r = table.OpenReader();
        Assert.Equal(5, r.Size);
        Assert.Equal(4, r.GroupSize);
        Assert.Equal(2, r.GroupSlotCount);
        Assert.Equal("root", r.GroupObjectKey);
        Assert.Equal(2, r.GetSlotSize(0));
        Assert.Equal(3, r.GetGroupEnd(1));
        Assert.Equal(0, r.ParentOf(1));
        Assert.Equal(1, r.GetParent(2));
        r.StartGroup();
        Assert.Equal(0, r.Parent);
        Assert.Equal(4, r.CurrentEnd);
        Assert.Equal(1, r.ParentNodes);
        Assert.Equal("before", r.Next());
        r.StartGroup();
        Assert.Equal("child", r.Next());
        r.StartGroup();
        Assert.True(r.IsGroupEnd);
        Assert.Same(ComposerSlotTable.Empty, r.Next());
        r.EndGroup();
        Assert.Equal(1, r.Slot);
        Assert.Equal(0, r.RemainingSlots);
        r.EndGroup();
        Assert.Equal(1, r.GroupSlotIndex);
        Assert.Equal("after", r.Get(0));
        Assert.Equal("node", r.GroupNode);
        r.StartNode();
        Assert.Equal("node-slot", r.Next());
        r.EndGroup();
        Assert.Equal("after", r.Next());
        Assert.True(r.IsGroupEnd);
        r.EndGroup();
        Assert.Equal(-1, r.Parent);
        Assert.Equal(50, r.GroupKey);
        r.StartGroup();
        r.EndGroup();
        Assert.Equal(r.Size, r.CurrentGroup);
        Assert.Equal(0, r.RemainingSlots);
    }

    [Fact]
    public void Metadata_IsSeparateFromUserSlots_AndNullIsAValue()
    {
        var table = Build(w =>
        {
            w.StartGroup(1, null, null);
            w.AppendSlot(null);
            w.AppendSlot("last");
            w.EndGroup();
            w.StartNode(2, null);
            w.EndGroup();
            w.StartGroup(3, ComposerSlotTable.Empty, "aux-only");
            w.EndGroup();
            w.StartGroup(4);
            w.EndGroup();
        });
        using var r = table.OpenReader();
        Assert.True(r.HasObjectKey);
        Assert.True(r.GetHasObjectKey(0));
        Assert.Null(r.GroupObjectKey);
        Assert.Null(r.GroupAux);
        Assert.Null(r.GetGroupAux(0));
        Assert.Null(r.GetNode(0));
        Assert.Same(ComposerSlotTable.Empty, r.GroupNode);
        Assert.Equal(2, r.GroupSlotCount);
        Assert.Null(r.GroupGet(0));
        Assert.Equal("last", r.GroupGet(0, 1));
        Assert.Same(ComposerSlotTable.Empty, r.GroupGet(0, int.MaxValue));
        r.StartGroup();
        Assert.Null(r.Next());
        Assert.True(r.HadNext);
        Assert.Equal(1, r.Slot);
        Assert.Equal("last", r.Get(0));
        Assert.Same(ComposerSlotTable.Empty, r.Get(int.MaxValue));
        Assert.Equal(1, r.RemainingSlots);
        Assert.Equal("last", r.Next());
        Assert.Same(ComposerSlotTable.Empty, r.Next());
        Assert.False(r.HadNext);
        Assert.Equal(2, r.Slot);
        r.EndGroup();
        Assert.True(r.IsNode);
        Assert.True(r.GetIsNode(1));
        Assert.Null(r.GroupNode);
        Assert.Null(r.GetNode(1));
        Assert.Same(ComposerSlotTable.Empty, r.GroupAux);
        Assert.Equal(0, r.GroupSlotCount);
        Assert.Equal("aux-only", r.GetGroupAux(2));
        Assert.False(r.GetHasObjectKey(2));
        Assert.Same(ComposerSlotTable.Empty, r.GetGroupAux(3));
        // Two metadata slots, two user slots, one node and one aux; no sentinel or duplicated node.
        Assert.Equal(6, Buffer<object?>(table, "_slots").Count);
    }

    [Fact]
    public void SkipAndExtractKeys_CountNodeGroupsAsOne_WithoutChangingSlots()
    {
        var table = Build(w =>
        {
            w.StartGroup(0);
            w.AppendSlot("parent");
            w.StartNode(1, "outer");
            w.StartNode(2, "inner-a"); w.EndGroup();
            w.StartNode(3, "inner-b"); w.EndGroup();
            w.EndGroup();
            w.StartGroup(4, "key");
            w.StartNode(5, "sibling-a"); w.EndGroup();
            w.StartNode(6, "sibling-b"); w.EndGroup();
            w.EndGroup();
            w.EndGroup();
        });
        using var r = table.OpenReader();
        Assert.Equal(3, r.NodeCount);
        Assert.Equal(2, r.GetNodeCount(1));
        Assert.Equal(0, r.GetNodeCount(2));
        r.StartGroup();
        var keys = r.ExtractKeys();
        Assert.Equal(new[] { 1, 4 }, keys.Select(k => k.Key));
        Assert.Equal(new[] { 1, 4 }, keys.Select(k => k.Location));
        Assert.Equal(new[] { 1, 2 }, keys.Select(k => k.Nodes));
        Assert.Equal(new[] { 0, 1 }, keys.Select(k => k.Index));
        Assert.Equal("key", keys[1].ObjectKey);
        Assert.Equal(1, r.CurrentGroup);
        Assert.Equal(0, r.Slot);
        Assert.Equal(1, r.SkipGroup());
        Assert.Single(r.ExtractKeys());
        Assert.Equal(2, r.SkipGroup());
        Assert.Equal("parent", r.Next());
        r.EndGroup();
    }

    [Fact]
    public void EmptyMode_IsNested_AndOnlyNextPretendsSlotsAreAbsent()
    {
        var table = Build(w => { w.StartGroup(1); w.AppendSlot("value"); w.EndGroup(); });
        using var r = table.OpenReader();
        r.StartGroup();
        r.BeginEmpty();
        r.BeginEmpty();
        Assert.True(r.InEmpty);
        Assert.True(r.IsGroupEnd);
        r.StartGroup();
        r.StartNode();
        r.EndGroup();
        Assert.Same(ComposerSlotTable.Empty, r.Next());
        Assert.False(r.HadNext);
        // This is the behavior of the local Kotlin implementation, despite its get() comment.
        Assert.Equal("value", r.Get(0));
        Assert.Equal("value", r.GroupGet(0, 0));
        Assert.Equal(1, r.RemainingSlots);
        Assert.Empty(r.ExtractKeys());
        Assert.Throws<InvalidOperationException>(() => r.SkipGroup());
        Assert.Throws<InvalidOperationException>(r.SkipToGroupEnd);
        Assert.Throws<InvalidOperationException>(() => r.Reposition(0));
        r.EndEmpty();
        Assert.Same(ComposerSlotTable.Empty, r.Next());
        r.EndEmpty();
        Assert.Equal("value", r.Next());
        Assert.True(r.HadNext);
        r.EndGroup();
        Assert.Throws<InvalidOperationException>(r.EndEmpty);
    }

    [Fact]
    public void RepositionAndRestoreParent_PreserveExplicitGroupStack()
    {
        var table = Build(w =>
        {
            w.StartGroup(0); w.AppendSlot("root");
            w.StartGroup(1); w.AppendSlot("child");
            w.StartGroup(2); w.AppendSlot("leaf"); w.EndGroup();
            w.EndGroup();
            w.StartGroup(3); w.EndGroup();
            w.EndGroup();
        });
        using var r = table.OpenReader();
        r.StartGroup();
        r.Reposition(3); // Same parent: keep the parent's slots.
        Assert.Equal("root", r.Get(0));
        r.Reposition(2); // Different parent: discard its slot cursor.
        Assert.Equal(1, r.Parent);
        Assert.Equal(3, r.GroupEnd);
        Assert.Equal(0, r.RemainingSlots);
        r.StartGroup();
        Assert.Equal("leaf", r.Next());
        r.EndGroup();
        r.RestoreParent(0);
        Assert.Equal(0, r.Parent);
        Assert.Equal(4, r.CurrentEnd);
        r.StartGroup();
        r.EndGroup();
        r.EndGroup();
        r.Reposition(r.Size);
        Assert.Equal(-1, r.Parent);
        Assert.True(r.IsGroupEnd);
        r.Reposition(0);
        r.StartGroup();
        r.SkipToGroupEnd();
        Assert.Equal(0, r.RemainingSlots);
        Assert.Same(ComposerSlotTable.Empty, r.Next());
        r.EndGroup();
    }

    [Fact]
    public void InvalidNavigation_ThrowsWithoutConsumingTheCurrentGroup()
    {
        var table = Build(w =>
        {
            w.StartGroup(0); w.StartGroup(1); w.EndGroup(); w.EndGroup();
            w.StartGroup(2); w.EndGroup();
        });
        using var r = table.OpenReader();
        Assert.Throws<InvalidOperationException>(r.EndGroup);
        Assert.Throws<InvalidOperationException>(r.StartNode);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Reposition(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Reposition(r.Size + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.GetGroupKey(r.Size));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.GetParent(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Get(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.GroupGet(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Anchor(r.Size));
        r.StartGroup();
        Assert.Throws<InvalidOperationException>(r.EndGroup);
        Assert.Throws<InvalidOperationException>(() => r.RestoreParent(2));
        Assert.Equal(1, r.CurrentGroup);
        r.StartGroup();
        r.EndGroup();
        r.EndGroup();
    }

    [Fact]
    public void Readers_AreIndependent_AndCloseExactlyOnce()
    {
        var table = Build(w => { w.StartGroup(1); w.AppendSlot("value"); w.EndGroup(); });
        var first = table.OpenReader();
        var second = table.OpenReader();
        first.StartGroup();
        Assert.Equal(0, second.CurrentGroup);
        Assert.Throws<InvalidOperationException>(() => table.OpenWriter());
        first.Close();
        first.Close();
        first.Dispose();
        Assert.True(first.Closed);
        Assert.Throws<InvalidOperationException>(() => table.OpenWriter());
        Assert.Throws<ObjectDisposedException>(() => first.Next());
        Assert.Throws<ObjectDisposedException>(() => first.Get(0));
        Assert.Throws<ObjectDisposedException>(() => first.Size);
        Assert.Throws<ObjectDisposedException>(() => first.GetGroupKey(GroupAnchor.Empty));
        Assert.Throws<ObjectDisposedException>(first.BeginEmpty);
        Assert.Throws<ObjectDisposedException>(() => first.Reposition(0));
        second.StartGroup();
        Assert.Equal("value", second.Next());
        second.Dispose();
        using var writer = table.OpenWriter();
        Assert.Throws<InvalidOperationException>(() => table.OpenReader());
        Assert.Throws<InvalidOperationException>(() => table.OpenWriter());
    }

    [Fact]
    public void AnchorsAndMarks_WorkWithGroupGapInTheMiddle_WithoutMutatingTable()
    {
        var table = Build(w =>
        {
            for (int i = 0; i < 40; i++) { w.StartGroup(i + 10); w.EndGroup(); }
        });
        var groups = Buffer<GroupRecord>(table, "_groups");
        var originalAnchors = Enumerable.Range(0, groups.Count).Select(i => table.Wrap(groups.AnchorAt(i))).ToArray();
        groups.GetRef(originalAnchors[0].Item).Flags |= GroupFlags.Mark | GroupFlags.ContainsMark;
        // Simulate the gap position that later editing operations will leave, preserving the tree.
        groups.InsertAt(1, default);
        groups.RemoveAt(1);
        var before = Enumerable.Range(0, groups.Count).Select(groups.GetAt).ToArray();
        using (var r = table.OpenReader())
        {
            Assert.True(r.HasMark(0));
            Assert.True(r.ContainsMark(0));
            Assert.False(r.HasMark(1));
            Assert.False(r.ContainsMark(1));
            for (int i = 0; i < r.Size; i++)
            {
                Assert.Equal(originalAnchors[i], r.Anchor());
                Assert.Equal(r.Anchor(), r.Anchor(i));
                Assert.Equal(i + 10, r.GetGroupKey(originalAnchors[i]));
                Assert.Equal(i + 10, r.GroupKey);
                r.SkipGroup();
            }
            Assert.Equal(0, r.GetGroupKey(GroupAnchor.Empty));
            var anchor = originalAnchors[0];
            Assert.Equal(0, r.GetGroupKey(new GroupAnchor(table, new GapBufferItemAnchor(anchor.Item.Id, anchor.Item.Generation + 1))));
        }
        Assert.Equal(before, Enumerable.Range(0, groups.Count).Select(groups.GetAt));
        using (var w = table.OpenWriter())
        {
            for (int i = 0; i < 80; i++) { w.StartGroup(100 + i); w.EndGroup(); }
        }
        using var again = table.OpenReader();
        Assert.Equal(120, again.Size);
        for (int i = 0; i < originalAnchors.Length; i++)
            Assert.Equal(i + 10, again.GetGroupKey(originalAnchors[i]));
    }

    [Fact]
    public void EmptyGroupsAndLateParentSlots_HaveIndependentDataBoundaries()
    {
        var table = Build(w =>
        {
            w.StartGroup(0);
            w.StartGroup(1); w.EndGroup();
            w.StartGroup(2);
            w.StartGroup(3); w.EndGroup();
            w.StartGroup(4); w.AppendSlot("child"); w.EndGroup();
            w.StartGroup(5); w.EndGroup();
            w.AppendSlot("middle");
            w.EndGroup();
            w.StartGroup(6); w.EndGroup();
            w.AppendSlot("root-old"); w.UpdateSlot("root");
            w.EndGroup();
        });
        using var r = table.OpenReader();
        Assert.Equal("root", r.GroupGet(0, 0));
        Assert.Equal("middle", r.GroupGet(2, 0));
        Assert.Equal("child", r.GroupGet(4, 0));
        foreach (int i in new[] { 1, 3, 5, 6 })
        {
            Assert.Equal(0, r.GetSlotSize(i));
            Assert.Same(ComposerSlotTable.Empty, r.GroupGet(i, 0));
        }
        Assert.Equal(3, Buffer<object?>(table, "_slots").Count);
    }

    [Fact]
    public void Writer_LifecycleAndSlotUpdate_ProtectPublishedData()
    {
        var table = new ComposerSlotTable();
        var w = table.OpenWriter();
        Assert.Throws<InvalidOperationException>(() => w.AppendSlot(1));
        Assert.Throws<InvalidOperationException>(w.EndGroup);
        w.StartNode(1, "node");
        Assert.Throws<InvalidOperationException>(() => w.UpdateSlot("wrong"));
        Assert.Throws<InvalidOperationException>(w.Close);
        Assert.Throws<InvalidOperationException>(() => table.OpenReader());
        w.EndGroup();
        w.Close();
        w.Dispose();
        Assert.Throws<ObjectDisposedException>(() => w.StartGroup());
        using var r = table.OpenReader();
        Assert.Equal("node", r.GroupNode);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(42)]
    [InlineData(139)]
    public void GeneratedTrees_RoundTripSlotsAndNodes_AcrossBufferGrowth(int seed)
    {
        var random = new Random(seed);
        int nextKey = 1;
        ModelGroup Create(int depth)
        {
            var group = new ModelGroup { Key = nextKey++, IsNode = random.Next(3) == 0 };
            int childCount = depth == 0 ? 0 : random.Next(1, 4);
            for (int i = 0; i < childCount; i++) group.Children.Add(Create(depth - 1));
            int slotCount = random.Next(4);
            for (int i = 0; i < slotCount; i++)
                group.Slots.Add(i == 1 ? null : $"slot:{group.Key}:{i}");
            return group;
        }
        var roots = Enumerable.Range(0, 4).Select(_ => Create(4)).ToList();
        void Write(ComposerSlotTable.Writer w, ModelGroup group)
        {
            if (group.IsNode) w.StartNode(group.Key, group);
            else w.StartGroup(group.Key, group);
            int beforeChildren = random.Next(group.Slots.Count + 1);
            foreach (var slot in group.Slots.Take(beforeChildren)) w.AppendSlot(slot);
            foreach (var child in group.Children) Write(w, child);
            foreach (var slot in group.Slots.Skip(beforeChildren)) w.AppendSlot(slot);
            w.EndGroup();
        }
        var table = Build(w => { foreach (var root in roots) Write(w, root); });
        using var r = table.OpenReader();
        Assert.Equal(roots.Sum(root => root.Size), r.Size);
        Assert.True(Buffer<GroupRecord>(table, "_groups").Capacity > 16);
        void Read(ModelGroup expected, int parent)
        {
            int index = r.CurrentGroup;
            Assert.Equal(expected.Key, r.GroupKey);
            Assert.Equal(parent, r.GetParent(index));
            Assert.Equal(expected.IsNode, r.IsNode);
            Assert.Equal(expected.Size, r.GroupSize);
            Assert.Equal(expected.Nodes, r.NodeCount);
            Assert.Equal(expected.Slots.Count, r.GroupSlotCount);
            Assert.Same(expected, expected.IsNode ? r.GroupNode : r.GroupObjectKey);
            var anchor = r.Anchor();
            for (int i = 0; i < expected.Slots.Count; i++)
                Assert.Equal(expected.Slots[i], r.GroupGet(i));
            r.StartGroup();
            int beforeChildren = expected.Slots.Count / 2;
            for (int i = 0; i < beforeChildren; i++) Assert.Equal(expected.Slots[i], r.Next());
            foreach (var child in expected.Children) Read(child, index);
            Assert.Equal(beforeChildren, r.Slot);
            for (int i = beforeChildren; i < expected.Slots.Count; i++) Assert.Equal(expected.Slots[i], r.Next());
            Assert.Same(ComposerSlotTable.Empty, r.Next());
            Assert.Equal(expected.Slots.Count, r.Slot);
            Assert.True(r.IsGroupEnd);
            r.EndGroup();
            Assert.Equal(expected.Key, r.GetGroupKey(anchor));
        }
        foreach (var root in roots) Read(root, -1);
        Assert.True(r.IsGroupEnd);
    }

    private sealed class ModelGroup
    {
        public int Key;
        public bool IsNode;
        public List<object?> Slots { get; } = new();
        public List<ModelGroup> Children { get; } = new();
        public int Size => 1 + Children.Sum(child => child.Size);
        public int Nodes => Children.Sum(child => child.IsNode ? 1 : child.Nodes);
    }

    // Test-only access permits storage invariants and flags to be exercised before a full editor exists.
    private static SlotMapGapBuffer<T> Buffer<T>(ComposerSlotTable table, string field) =>
        (SlotMapGapBuffer<T>)typeof(ComposerSlotTable).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(table)!;
}
