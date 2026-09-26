using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public class CompositionLocalTests
{
    private sealed class ModuloPolicy : ISnapshotMutationPolicy<int>
    {
        public bool Equivalent(int a, int b) => a % 10 == b % 10;
        public int Merge(int previous, int current, int applied) => default;
        public bool TryMerge(int previous, int current, int applied, out int result)
        {
            result = default;
            return false;
        }
    }

    private static void Restart(IComposerContext context, int key, Action<IComposerContext> content)
    {
        context.StartRestartableGroup(key);
        if (context.Skipping) context.SkipToGroupEnd();
        else content(context);
        context.EndRestartableGroup(key)?.UpdateScope(next => Restart(next, key, content));
    }

    [Fact]
    public void DefaultsProvidersAndNullFollowTheCompositionHierarchy()
    {
        int defaults = 0;
        ProvidableCompositionLocal<string?> text = Composables.CompositionLocalOf(() =>
        {
            defaults++;
            return "default";
        });
        ProvidableCompositionLocal<int> number = Composables.StaticCompositionLocalOf(() => -1);
        Assert.Throws<NotImplementedException>(() => text.Current());

        List<string?> seen = new();
        List<int> numbers = new();
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        composition.SetContent((context, _, _) =>
        {
            seen.Add(context.Consume(text));
            seen.Add(context.Consume(text));
            Composables.Builders.CompositionLocalProvider(
                new ProvidedValue[] { text.Provides("first"), text.Provides("last"), number.Provides(7) },
                (provided, _, _) =>
                {
                    seen.Add(provided.Consume(text));
                    numbers.Add(provided.Consume(number));
                    Composables.Builders.CompositionLocalProvider(text.ProvidesDefault("ignored"),
                        (nestedDefault, _, _) => seen.Add(nestedDefault.Consume(text)), provided);
                    Composables.Builders.CompositionLocalProvider(text.Provides(null),
                        (nestedNull, _, _) => seen.Add(nestedNull.Consume(text)), provided);
                    seen.Add(provided.Consume(text));
                }, context);
            seen.Add(context.Consume(text));
        });

        Assert.Equal(new string?[] { "default", "default", "last", "last", null, "last", "default" }, seen);
        Assert.Equal(new[] { 7 }, numbers);
        Assert.Equal(1, defaults);
    }

    [Fact]
    public void FailedDefaultFactoryIsRetried()
    {
        int attempts = 0;
        ProvidableCompositionLocal<int> local = Composables.CompositionLocalOf(() =>
        {
            if (++attempts == 1) throw new InvalidOperationException("Not ready");
            return 8;
        });
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());

        Assert.Throws<InvalidOperationException>(() => composition.SetContent((context, _, _) => _ = context.Consume(local)));
        composition.SetContent((context, _, _) => Assert.Equal(8, context.Consume(local)));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void ProvidesDefaultSuppliesAValueOnlyWhenNoAncestorProvidesOne()
    {
        ProvidableCompositionLocal<int> local = Composables.CompositionLocalOf(() => 1);
        List<int> seen = new();
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());

        composition.SetContent((context, _, _) =>
        {
            Composables.Builders.CompositionLocalProvider(local.ProvidesDefault(2),
                (first, _, _) => seen.Add(first.Consume(local)), context);
            Composables.Builders.CompositionLocalProvider(local.Provides(3),
                (outer, _, _) => Composables.Builders.CompositionLocalProvider(local.ProvidesDefault(4),
                    (inner, _, _) => seen.Add(inner.Consume(local)), outer), context);
        });

        Assert.Equal(new[] { 2, 3 }, seen);
    }

    [Fact]
    public void ChangingTheProvidedSetAddsAndRemovesLocals()
    {
        ProvidableCompositionLocal<int> local = Composables.StaticCompositionLocalOf(() => 1);
        IReadOnlyList<ProvidedValue> values = new ProvidedValue[] { local.Provides(2) };
        int seen = 0;
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        ComposableAction content = (context, _, _) =>
            Composables.Builders.CompositionLocalProvider(values,
                (scope, _, _) => seen = scope.Consume(local), context);

        composition.SetContent(content);
        Assert.Equal(2, seen);
        values = Array.Empty<ProvidedValue>();
        composition.SetContent(content);
        Assert.Equal(1, seen);
        values = new ProvidedValue[] { local.Provides(3) };
        composition.SetContent(content);
        Assert.Equal(3, seen);
    }

    [Fact]
    public void DynamicLocalInvalidatesOnlyReadersInTheSamePass()
    {
        SnapshotMutableState<int> source = Composables.CreateMutableState(1);
        ProvidableCompositionLocal<int> local = Composables.CompositionLocalOf(() => -1);
        int providers = 0, readers = 0, nonReaders = 0, seen = 0;
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());

        composition.SetContent((context, _, _) => Restart(context, 1, owner =>
        {
            providers++;
            int value = source.Value;
            Composables.Builders.CompositionLocalProvider(local.Provides(value), (provided, _, _) =>
            {
                Restart(provided, 2, child => { readers++; seen = child.Consume(local); });
                Restart(provided, 3, child => nonReaders++);
            }, owner);
        }));

        source.Value = 2;
        Assert.True(composition.Recompose());
        Assert.Equal(2, seen);
        composition.ApplyChanges();

        Assert.Equal((2, 2, 1, 2), (providers, readers, nonReaders, seen));
        Assert.False(composition.HasInvalidations);
        Assert.False(composition.Recompose());
    }

    [Fact]
    public void DynamicLocalHonorsItsMutationPolicy()
    {
        SnapshotMutableState<int> source = Composables.CreateMutableState(1);
        ProvidableCompositionLocal<int> local = Composables.CompositionLocalOf(() => -1, new ModuloPolicy());
        int readers = 0, seen = 0;
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        ComposableAction content = (context, _, _) => Restart(context, 1, owner =>
        {
            int value = source.Value;
            Composables.Builders.CompositionLocalProvider(local.Provides(value), (provided, _, _) =>
                Restart(provided, 2, child => { readers++; seen = child.Consume(local); }), owner);
        });

        composition.SetContent(content);
        source.Value = 11;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal((1, 1), (readers, seen));

        source.Value = 12;
        Assert.True(composition.Recompose());
        Assert.Equal(12, seen);
        composition.ApplyChanges();
        Assert.Equal(2, readers);
    }

    [Fact]
    public void StaticLocalInvalidatesTheProviderSubtreeButNotOutsideSiblings()
    {
        SnapshotMutableState<int> source = Composables.CreateMutableState(1);
        SnapshotMutableState<int> trigger = Composables.CreateMutableState(0);
        ProvidableCompositionLocal<int> local = Composables.StaticCompositionLocalOf(() => -1);
        int readers = 0, nonReaders = 0, outside = 0, seen = 0;
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());

        composition.SetContent((context, _, _) =>
        {
            Restart(context, 1, owner =>
            {
                _ = trigger.Value;
                int value = source.Value;
                Composables.Builders.CompositionLocalProvider(local.Provides(value), (provided, _, _) =>
                {
                    Restart(provided, 2, child => { readers++; seen = child.Consume(local); });
                    Restart(provided, 3, child => nonReaders++);
                }, owner);
            });
            Restart(context, 4, sibling => outside++);
        });

        trigger.Value = 1;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal((1, 1, 1, 1), (readers, nonReaders, outside, seen));

        source.Value = 2;
        Assert.True(composition.Recompose());
        Assert.Equal(2, seen);
        composition.ApplyChanges();
        Assert.Equal((2, 2, 1, 2), (readers, nonReaders, outside, seen));
    }

    [Fact]
    public void NestedProviderCanMaskAStaticChangeAndRestoreSkipping()
    {
        SnapshotMutableState<int> source = Composables.CreateMutableState(1);
        ProvidableCompositionLocal<int> local = Composables.StaticCompositionLocalOf(() => -1);
        int readers = 0, seen = 0;
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());

        composition.SetContent((context, _, _) => Restart(context, 1, owner =>
        {
            int value = source.Value;
            Composables.Builders.CompositionLocalProvider(local.Provides(value), (outer, _, _) =>
                Composables.Builders.CompositionLocalProvider(local.Provides(10), (inner, _, _) =>
                    Restart(inner, 2, child => { readers++; seen = child.Consume(local); }), outer), owner);
        }));

        source.Value = 2;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal((1, 10), (readers, seen));
    }

    [Fact]
    public void RestartedChildInsideSkippedProviderRetainsItsLocalScope()
    {
        SnapshotMutableState<int> childState = Composables.CreateMutableState(0);
        ProvidableCompositionLocal<int> local = Composables.CompositionLocalOf(() => -1);
        int providers = 0, readers = 0, seen = 0;
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());

        composition.SetContent((context, _, _) =>
            Composables.Builders.CompositionLocalProvider(local.Provides(7), (provided, _, _) =>
            {
                providers++;
                Restart(provided, 1, child =>
                {
                    _ = childState.Value;
                    readers++;
                    seen = child.Consume(local);
                });
            }, context));

        childState.Value = 1;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal((1, 2, 7), (providers, readers, seen));
    }

    [Fact]
    public void DiscardRollsBackDynamicProviderStateAndProviderSlot()
    {
        ProvidableCompositionLocal<int> local = Composables.CompositionLocalOf(() => -1);
        int provided = 1, seen = 0;
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        ComposableAction content = (context, _, _) =>
            Composables.Builders.CompositionLocalProvider(local.Provides(provided),
                (scope, _, _) => seen = scope.Consume(local), context);

        composition.SetContent(content);
        provided = 2;
        composition.ComposeContent(content);
        Assert.Equal(2, seen);
        composition.DiscardChanges();
        Assert.False(composition.HasInvalidations);

        provided = 1;
        composition.ComposeContent(content);
        Assert.Equal(1, seen);
        Assert.Empty(composition.PendingChanges!);
        composition.ApplyChanges();
    }

    [Fact]
    public void KeyedProviderMovesRetainTheCorrectScopes()
    {
        ProvidableCompositionLocal<int> local = Composables.StaticCompositionLocalOf(() => -1);
        int[] keys = { 1, 2, 3 };
        Dictionary<int, int> seen = new();
        using Composition<CompositionTests.Node> composition =
            new Composition<CompositionTests.Node>(new CompositionTests.Applier());
        ComposableAction content = (context, _, _) =>
        {
            seen.Clear();
            foreach (int key in keys)
            {
                context.StartMovableGroup(90, key);
                Composables.Builders.CompositionLocalProvider(local.Provides(key * 10),
                    (scope, _, _) => seen[key] = scope.Consume(local), context);
                context.EndMovableGroup(90);
            }
        };

        composition.SetContent(content);
        keys = new[] { 3, 1, 2 };
        composition.SetContent(content);

        Assert.Equal(30, seen[3]);
        Assert.Equal(10, seen[1]);
        Assert.Equal(20, seen[2]);
    }

    [Fact]
    public async Task ParallelCompositionsKeepIndependentLocalScopes()
    {
        ProvidableCompositionLocal<int> local = Composables.StaticCompositionLocalOf(() => -1);
        using Barrier barrier = new Barrier(2);

        Task<int> Compose(int providedValue) => Task.Run(() =>
        {
            int seen = 0;
            using Composition<CompositionTests.Node> composition =
                new Composition<CompositionTests.Node>(new CompositionTests.Applier());
            composition.SetContent((context, _, _) =>
                Composables.Builders.CompositionLocalProvider(local.Provides(providedValue), (scope, _, _) =>
                {
                    barrier.SignalAndWait();
                    seen = scope.Consume(local);
                }, context));
            return seen;
        });

        int[] results = await Task.WhenAll(Compose(10), Compose(20));
        Assert.Equal(new[] { 10, 20 }, results);
    }
}
