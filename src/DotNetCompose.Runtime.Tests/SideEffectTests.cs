using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Effects;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public sealed class SideEffectTests
{
    private sealed class Observer(List<string> events) : IRememberObserver
    {
        public void OnRemembered() => events.Add("Remember");
        public void OnForgotten() => events.Add("Forget");
        public void OnAbandoned() => events.Add("Abandon");
    }

    private sealed class ThrowingObserver : IRememberObserver
    {
        public void OnRemembered() => throw new ApplicationException("Remember failed");
        public void OnForgotten() { }
        public void OnAbandoned() { }
    }

    [Theory]
    [InlineData("Apply")]
    [InlineData("Discard")]
    [InlineData("ComputeFailure")]
    [InlineData("ApplyFailure")]
    [InlineData("RememberFailure")]
    [InlineData("EffectFailure")]
    [InlineData("Dispose")]
    public void CompletedQueuesReleaseCapturedObjects(string outcome)
    {
        var applier = new CompositionTests.Applier { ThrowOnUpdate = outcome == "ApplyFailure" };
        using var composition = new Composition<CompositionTests.Node>(applier);
        WeakReference captured = ComposeCapturedEffect(composition, outcome);
        switch (outcome)
        {
            case "ComputeFailure":
                break;
            case "Discard":
                composition.DiscardChanges();
                break;
            case "Dispose":
                composition.Dispose();
                break;
            case "ApplyFailure":
                Assert.Throws<InvalidOperationException>(() => composition.ApplyChanges());
                break;
            case "RememberFailure":
            case "EffectFailure":
                Assert.Throws<ApplicationException>(() => composition.ApplyChanges());
                break;
            default:
                composition.ApplyChanges();
                break;
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(captured.IsAlive);
        GC.KeepAlive(composition);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ComposeCapturedEffect(Composition<CompositionTests.Node> composition, string outcome)
    {
        WeakReference? captured = null;
        ComposableAction content = (c, _, _) =>
        {
            object value = new();
            captured = new WeakReference(value);
            c.RecordSideEffect(() =>
            {
                GC.KeepAlive(value);
                if (outcome == "EffectFailure") throw new ApplicationException("Effect failed");
            });
            if (outcome == "ApplyFailure") CompositionTests.Emit(c, 1);
            if (outcome == "RememberFailure")
            {
                c.RememberedValue();
                c.UpdateRememberedValue(new ThrowingObserver());
            }
            if (outcome == "ComputeFailure") throw new ApplicationException("Compute failed");
        };
        if (outcome == "ComputeFailure")
            Assert.Throws<ApplicationException>(() => composition.ComposeContent(content));
        else
            composition.ComposeContent(content);
        return captured!;
    }

    [Fact]
    public void EmptyAndDiscardedPassesDoNotReplayEffectsFromEarlierPasses()
    {
        using var composition = new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        var calls = new List<int>();
        ComposableAction empty = static (c, _, _) => { };
        for (int pass = 0; pass < 3; pass++)
        {
            int first = pass * 16;
            ComposableAction effects = (c, _, _) =>
            {
                for (int index = 0; index < 16; index++)
                {
                    int value = first + index;
                    c.RecordSideEffect(() => calls.Add(value));
                }
            };
            composition.SetContent(effects);
            composition.SetContent(empty);
            composition.ComposeContent(effects);
            composition.DiscardChanges();
            composition.SetContent(empty);
        }
        Assert.Equal(Enumerable.Range(0, 48), calls);
    }

    [Fact]
    public void EffectsRunAfterNodesAndObserversInRegistrationOrder()
    {
        var applier = new CompositionTests.Applier();
        using var composition = new Composition<CompositionTests.Node>(applier);
        composition.ComposeContent((c, _, _) =>
        {
            CompositionTests.Emit(c, 7);
            c.RememberedValue();
            c.UpdateRememberedValue(new Observer(applier.Events));
            Composables.Builders.SideEffect(() =>
            {
                Assert.Equal(7, Assert.Single(applier.Root.Children).Value);
                applier.Events.Add("First");
            }, c);
            Composables.Builders.SideEffect(() => applier.Events.Add("Second"), c);
        });
        Assert.Empty(applier.Events);
        composition.ApplyChanges();
        Assert.Equal(new[] { "End", "Remember", "First", "Second" }, applier.Events.TakeLast(4));
    }

    [Fact]
    public void EffectsRunWithNoTreeOperationsAndAreNotReplayed()
    {
        using var composition = new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        int calls = 0;
        ComposableAction content = (c, _, _) => Composables.Builders.SideEffect(() => calls++, c);
        composition.SetContent(content);
        composition.ComposeContent(content);
        Assert.Empty(composition.PendingChanges!);
        Assert.Equal(1, calls);
        composition.ApplyChanges();
        Assert.Equal(2, calls);
        Assert.Throws<InvalidOperationException>(() => composition.ApplyChanges());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DiscardFailureAndDisposeDropUncommittedEffects()
    {
        int calls = 0;
        var composition = new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        ComposableAction content = (c, _, _) => Composables.Builders.SideEffect(() => calls++, c);
        composition.ComposeContent(content);
        composition.DiscardChanges();
        Assert.Throws<InvalidOperationException>(() => composition.ComposeContent((c, _, _) =>
        {
            content(c, default, default);
            throw new InvalidOperationException();
        }));
        composition.SetContent(content);
        Assert.Equal(1, calls);
        composition.ComposeContent(content);
        composition.Dispose();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FailedApplyDoesNotDispatchEffects()
    {
        var applier = new CompositionTests.Applier { ThrowOnUpdate = true };
        using var composition = new Composition<CompositionTests.Node>(applier);
        int calls = 0;
        composition.ComposeContent((c, _, _) =>
        {
            CompositionTests.Emit(c, 1);
            c.RecordSideEffect(() => calls++);
        });
        Assert.Throws<InvalidOperationException>(() => composition.ApplyChanges());
        Assert.Equal(0, calls);
        Assert.True(composition.IsFaulted);
    }

    [Fact]
    public void ThrowingEffectStopsTheQueueAndFaultsTheCommittedComposition()
    {
        using var composition = new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        int calls = 0;
        composition.ComposeContent((c, _, _) =>
        {
            c.RecordSideEffect(() => calls++);
            c.RecordSideEffect(() => throw new ApplicationException("Effect failed"));
            c.RecordSideEffect(() => calls++);
        });
        var batch = composition.PendingChanges!;
        Assert.Throws<ApplicationException>(() => composition.ApplyChanges());
        Assert.Equal(1, calls);
        Assert.True(batch.IsConsumed);
        Assert.True(composition.IsFaulted);
        Assert.False(composition.HasPendingChanges);
    }

    [Fact]
    public void KeyCountsAndValuesKeepNeighbourSlotsIndependent()
    {
        using var composition = new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        object?[] keys = [1, 2];
        int keyedCalls = 0, neighbourCalls = 0, emptyCalls = 0;
        object? neighbour = null;
        ComposableAction content = (c, _, _) =>
        {
            Composables.Builders.SideEffect((ReadOnlySpan<object?>)keys, () => keyedCalls++, c);
            Composables.Builders.SideEffect(neighbour, () => neighbourCalls++, c);
            Composables.Builders.SideEffect(ReadOnlySpan<object?>.Empty, () => emptyCalls++, c);
        };
        composition.SetContent(content);
        composition.SetContent(content);
        Assert.Equal((1, 1, 1), (keyedCalls, neighbourCalls, emptyCalls));
        keys = [3, 4];
        composition.ComposeContent(content);
        composition.DiscardChanges();
        composition.SetContent(content);
        composition.SetContent(content);
        Assert.Equal(2, keyedCalls);
        foreach (object?[] next in new object?[][] { [3], [], [3, 4, 5] })
        {
            keys = next;
            composition.SetContent(content);
            composition.SetContent(content);
        }
        Assert.Equal((5, 1, 1), (keyedCalls, neighbourCalls, emptyCalls));
        neighbour = "changed";
        composition.SetContent(content);
        Assert.Equal((5, 2, 1), (keyedCalls, neighbourCalls, emptyCalls));
    }
}
