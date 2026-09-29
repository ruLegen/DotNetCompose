using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public sealed class DefaultMaskTests
{
    private sealed class TrackedValue : IEquatable<TrackedValue>
    {
        public TrackedValue(int value) => Value = value;
        public int Value { get; }
        public static int Comparisons;
        public bool Equals(TrackedValue? other)
        {
            Comparisons++;
            return other?.Value == Value;
        }
        public override bool Equals(object? obj) => obj is TrackedValue other && Equals(other);
        public override int GetHashCode() => Value;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(130)]
    public void MaskSnapshotCopiesAndComparesEveryBit(int count)
    {
        byte[] bytes = new byte[count];
        bytes[0] = 1;
        bytes[count - 1] = 1;
        DefaultMaskSnapshot snapshot = DefaultMaskSnapshot.Capture(
            new ComposableArgumentsDefaultState(bytes), count);
        ComposableDefaultsCache cache = new ComposableDefaultsCache(
            new ComposableArgumentsDefaultState(bytes), new object?[count]);

        Assert.True(snapshot.Matches(new ComposableArgumentsDefaultState(bytes), count));
        Assert.True(cache.Matches(new ComposableArgumentsDefaultState(bytes)));
        bytes[count - 1] = 0;
        Assert.False(snapshot.Matches(new ComposableArgumentsDefaultState(bytes), count));
        Assert.False(cache.Matches(new ComposableArgumentsDefaultState(bytes)));
        bytes[count - 1] = 1;
        Assert.True(snapshot.Matches(new ComposableArgumentsDefaultState(bytes), count));
        Assert.False(snapshot.Matches(new ComposableArgumentsDefaultState(bytes), count + 1));
    }

    [Fact]
    public void ContextKeepsOneMaskSlotAndSkipsKnownValueComparisons()
    {
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        byte[] maskBytes = { 1, 0 };
        TrackedValue value = new TrackedValue(1);
        byte inputState = ComposableArgumentsState.Uncertain;
        byte resolved = 0;
        bool maskChanged = false;
        ComposableAction content = (composer, _, _) =>
        {
            composer.StartRestartableGroup(10);
            maskChanged = composer.ChangedDefaultMask(
                new ComposableArgumentsDefaultState(maskBytes), maskBytes.Length);
            resolved = composer.ResolveDefaultParameterState(value, inputState);
            composer.Changed(42);
            composer.EndRestartableGroup(10);
        };

        composition.SetContent(content);
        Assert.True(maskChanged);
        Assert.Equal(ComposableArgumentsState.Different, resolved);
        object? firstMask;
        using (var firstReader = composition.SlotTable.OpenReader())
            firstMask = firstReader.GroupGet(1, 0);

        TrackedValue.Comparisons = 0;
        inputState = ComposableArgumentsState.Same;
        composition.SetContent(content);
        Assert.False(maskChanged);
        Assert.Equal(ComposableArgumentsState.Same, resolved);
        Assert.Equal(0, TrackedValue.Comparisons);
        using (var sameReader = composition.SlotTable.OpenReader())
            Assert.Same(firstMask, sameReader.GroupGet(1, 0));

        inputState = ComposableArgumentsState.Static;
        composition.SetContent(content);
        Assert.False(maskChanged);
        Assert.Equal(ComposableArgumentsState.Static, resolved);
        Assert.Equal(0, TrackedValue.Comparisons);

        maskBytes[1] = 1;
        value = new TrackedValue(2);
        inputState = ComposableArgumentsState.Different;
        composition.SetContent(content);
        Assert.True(maskChanged);
        Assert.Equal(ComposableArgumentsState.Different, resolved);
        using (var changedReader = composition.SlotTable.OpenReader())
            Assert.NotSame(firstMask, changedReader.GroupGet(1, 0));

        inputState = ComposableArgumentsState.Uncertain;
        composition.SetContent(content);
        Assert.False(maskChanged);
        Assert.Equal(ComposableArgumentsState.Same, resolved);
        Assert.True(TrackedValue.Comparisons > 0);

        using var reader = composition.SlotTable.OpenReader();
        Assert.Equal(3, reader.GetSlotSize(1));
    }
}
