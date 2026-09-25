using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Effects;
using DotNetCompose.Runtime.SlotTable;
using DotNetCompose.Runtime.SlotTable.GapBuffer;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public class CompositionTests
{
    internal sealed class Node
    {
        public int Value;
        public readonly List<Node> Children = new();
    }

    internal sealed class Applier : IApplier<Node>
    {
        public readonly Node Root = new();
        private readonly Stack<Node> _path = new();
        public readonly List<string> Events = new();
        public bool ThrowOnUpdate;
        public Node Current => _path.Count == 0 ? Root : _path.Peek();
        public void OnBeginChanges() => Events.Add("Begin");
        public void OnEndChanges() => Events.Add("End");
        public void Down(Node node) { Events.Add("Down"); _path.Push(node); }
        public void Up() { Events.Add("Up"); _path.Pop(); }
        public void InsertTopDown(int index, Node instance) { Events.Add($"Insert:{index}"); Current.Children.Insert(index, instance); }
        public void InsertBottomUp(int index, Node instance) => Events.Add($"Bottom:{index}");
        public void Remove(int index, int count) { Events.Add($"Remove:{index}:{count}"); Current.Children.RemoveRange(index, count); }
        public void Move(int from, int to, int count)
        {
            Events.Add($"Move:{from}:{to}:{count}");
            var moved = Current.Children.GetRange(from, count);
            Current.Children.RemoveRange(from, count);
            Current.Children.InsertRange(to > from ? to - count : to, moved);
        }
        public void Clear() { _path.Clear(); Root.Children.Clear(); }
        public void Apply(Action<Node, object?> block, object? value)
        {
            if (ThrowOnUpdate) throw new InvalidOperationException("Applier failed");
            Events.Add("Update"); block(Current, value);
        }
    }

    private sealed class Context : SynchronizationContext
    {
        private readonly Queue<Action> _queue = new();
        public override void Post(SendOrPostCallback d, object? state) { lock (_queue) _queue.Enqueue(() => d(state)); }
        public int Count { get { lock (_queue) return _queue.Count; } }
        public void Drain()
        {
            int limit = 100;
            while (true)
            {
                Action next;
                lock (_queue) { if (_queue.Count == 0) return; next = _queue.Dequeue(); }
                Assert.True(limit-- > 0, "The scheduler failed to become idle.");
                next();
            }
        }
    }

    internal static void Emit(IComposerContext c, int value, Action? created = null, Action<IComposerContext>? content = null)
    {
        c.StartNode(10);
        if (c.Inserting) c.CreateNode(() => { created?.Invoke(); return new Node(); }); else c.UseNode();
        c.ApplyNode<Node, int>(value, (node, item) => node.Value = item);
        content?.Invoke(c);
        c.EndNode();
    }

    [Fact]
    public void InitialBatchIsDeferredAndCanOnlyBeAppliedOnce()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        int factories = 0;
        composition.ComposeContent((c, _, _) => Emit(c, 42, () => factories++));
        var batch = composition.PendingChanges!;
        Assert.Contains(batch, op => op.Kind == CompositionOperationKind.CreateNode);
        Assert.Empty(applier.Root.Children);
        Assert.Equal(0, factories);
        Assert.Equal(0, composition.SlotTable.Size);
        Assert.Throws<InvalidOperationException>(() => composition.Recompose());
        composition.ApplyChanges(changes: batch);
        Assert.Equal(42, Assert.Single(applier.Root.Children).Value);
        Assert.Equal(1, factories);
        Assert.True(batch.IsConsumed);
        Assert.Throws<InvalidOperationException>(() => composition.ApplyChanges(batch));
        using var other = new Composition<Node>(new Applier());
        Assert.Throws<InvalidOperationException>(() => other.ApplyChanges(batch));
    }

    [Fact]
    public void EqualContentHasNoOperationsAndUpdatesReuseNodes()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        composition.SetContent((c, _, _) => Emit(c, 1));
        var node = Assert.Single(applier.Root.Children);
        composition.ComposeContent((c, _, _) => Emit(c, 1));
        Assert.Empty(composition.PendingChanges!);
        composition.ApplyChanges();
        composition.ComposeContent((c, _, _) => Emit(c, 2));
        Assert.Equal(1, node.Value);
        composition.ApplyChanges();
        Assert.Same(node, Assert.Single(applier.Root.Children));
        Assert.Equal(2, node.Value);
    }

    [Fact]
    public void PendingOperationsSeparateGroupNodeAndSlotPositions()
    {
        Applier applier = new Applier();
        using Composition<Node> composition = new Composition<Node>(applier);
        int[] keys = { 1, 2 };
        int slotValue = 10;
        ComposableAction content = (composer, _, _) =>
        {
            foreach (int key in keys)
            {
                composer.StartMovableGroup(50, key);
                composer.Changed(slotValue);
                Emit(composer, key);
                composer.EndMovableGroup(50);
            }
        };
        composition.SetContent(content);
        keys = new[] { 2, 1 };
        slotValue = 20;
        composition.ComposeContent(content);
        CompositionChangeSet changes = composition.PendingChanges!;

        CompositionOperation moveGroup = Assert.Single(changes, operation => operation.Kind == CompositionOperationKind.MoveGroup);
        Assert.Equal(1, moveGroup.GroupIndex);
        Assert.NotNull(moveGroup.SourceGroupIndex);
        Assert.Null(moveGroup.NodeIndex);
        Assert.Null(moveGroup.SlotOffset);

        CompositionOperation moveNode = Assert.Single(changes, operation => operation.Kind == CompositionOperationKind.MoveNode);
        Assert.Equal(0, moveNode.NodeIndex);
        Assert.Equal(1, moveNode.SourceNodeIndex);
        Assert.Null(moveNode.GroupIndex);

        CompositionOperation slot = Assert.Single(changes, operation => operation.Kind == CompositionOperationKind.UpdateSlot && operation.GroupIndex == 1);
        Assert.Equal(0, slot.SlotOffset);
        Assert.NotNull(slot.GroupIndex);
        Assert.Null(slot.NodeIndex);
        Assert.DoesNotContain("index=", slot.ToString());
        composition.ApplyChanges();
        Assert.Equal(new[] { 2, 1 }, applier.Root.Children.Select(node => node.Value));
    }

    [Fact]
    public void RandomKeyedEditsKeepNodesRememberAndAnchors()
    {
        var random = new Random(47);
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        var identities = new Dictionary<int, (Node node, object remembered)>();
        var seen = new Dictionary<int, object>();
        var groupAnchors = new Dictionary<int, GroupAnchor>();
        int[] keys = Array.Empty<int>();
        ComposableAction content = (c, _, _) =>
        {
            foreach (int key in keys)
            {
                c.StartMovableGroup(50, key);
                seen[key] = Composables.Builders.Remember("item", () => new object(), c);
                Emit(c, key, content: child => { if (key % 3 == 0) Emit(child, -key); });
                c.EndMovableGroup(50);
            }
        };
        for (int iteration = 0; iteration < 80; iteration++)
        {
            keys = Enumerable.Range(1, 30).Where(_ => random.Next(3) != 0).OrderBy(_ => random.Next()).ToArray();
            seen.Clear();
            composition.SetContent(content);
            Assert.Equal(keys, applier.Root.Children.Select(n => n.Value));
            for (int i = 0; i < keys.Length; i++)
            {
                int key = keys[i];
                Node node = applier.Root.Children[i];
                if (identities.TryGetValue(key, out var previous))
                { Assert.Same(previous.node, node); Assert.Same(previous.remembered, seen[key]); }
                Assert.Equal(key % 3 == 0 ? 1 : 0, node.Children.Count);
            }
            identities = keys.Select((key, i) => (key, i)).ToDictionary(x => x.key, x => (applier.Root.Children[x.i], seen[x.key]));
            using var reader = composition.SlotTable.OpenReader();
            Assert.Equal(reader.Size, reader.GetGroupSize(0));
            Assert.Equal(keys.Length, reader.GetNodeCount(0));
            var nextAnchors = new Dictionary<int, GroupAnchor>();
            for (int location = 1; location < reader.Size; location += reader.GetGroupSize(location))
            {
                int key = (int)reader.GetGroupObjectKey(location)!;
                var anchor = reader.Anchor(location);
                if (groupAnchors.TryGetValue(key, out var prior)) Assert.Equal(prior, anchor);
                nextAnchors.Add(key, anchor);
            }
            foreach (var removed in groupAnchors.Where(item => !nextAnchors.ContainsKey(item.Key)))
                Assert.Equal(0, reader.GetGroupKey(removed.Value));
            groupAnchors = nextAnchors;
        }
    }

    [Fact]
    public void DuplicateKeysUseOldOccurrenceOrderAndGroupsCanHaveMultipleNodes()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        int[] keys = { 1, 2, 1, 3 };
        ComposableAction content = (c, _, _) =>
        {
            foreach (int key in keys)
            {
                c.StartMovableGroup(9, key);
                if (key != 3) { Emit(c, key); Emit(c, -key); }
                c.EndMovableGroup(9);
            }
        };
        composition.SetContent(content);
        var old = applier.Root.Children.ToArray();
        keys = new[] { 1, 1, 3, 2 };
        composition.SetContent(content);
        Assert.Equal(new[] { old[0], old[1], old[4], old[5], old[2], old[3] }, applier.Root.Children);
    }

    [Fact]
    public void ParentSlotsAfterChildrenAreRetainedAndTrimmed()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        bool extra = true;
        object? remembered = null;
        ComposableAction content = (c, _, _) =>
        {
            c.StartGroup(1);
            Emit(c, 1);
            if (extra) remembered = Composables.Builders.Remember("after", () => new object(), c);
            c.EndGroup();
        };
        composition.SetContent(content);
        object? first = remembered;
        composition.SetContent(content);
        Assert.Same(first, remembered);
        extra = false;
        composition.SetContent(content);
        using var reader = composition.SlotTable.OpenReader();
        Assert.Equal(0, reader.GetSlotSize(1));
    }

    private static void Restart(IComposerContext c, int key, Action<IComposerContext> body)
    {
        c.StartRestartableGroup(key);
        if (c.Skipping) c.SkipToGroupEnd(); else body(c);
        c.EndRestartableGroup(key)?.UpdateScope(next => Restart(next, key, body));
    }

    [Fact]
    public void StateRestartsOnlyDependentChildInsideSkippedParent()
    {
        var state = Composables.CreateMutableState(1);
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        int roots = 0, parents = 0, children = 0, siblings = 0;
        composition.SetContent((c, _, _) =>
        {
            roots++;
            Restart(c, 1, parent =>
            {
                parents++;
                Emit(parent, 0, content: nested => Restart(nested, 2, child => { children++; Emit(child, state.Value); }));
            });
            Restart(c, 3, sibling => { siblings++; Emit(sibling, 100); });
        });
        state.Value = 2;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal((1, 1, 2, 1), (roots, parents, children, siblings));
        Assert.Equal(2, applier.Root.Children[0].Children[0].Value);
    }

    [Fact]
    public void StateSubscriptionsFollowTheExecutedBranch()
    {
        var flag = Composables.CreateMutableState(true);
        var a = Composables.CreateMutableState(1);
        var b = Composables.CreateMutableState(2);
        using var composition = new Composition<Node>(new Applier());
        composition.SetContent((c, _, _) => Restart(c, 1, next => Emit(next, flag.Value ? a.Value : b.Value)));
        flag.Value = false;
        Assert.True(composition.Recompose()); composition.ApplyChanges();
        a.Value = 20;
        Assert.False(composition.Recompose());
        b.Value = 30;
        Assert.True(composition.Recompose()); composition.ApplyChanges();
    }

    [Fact]
    public void AutomaticPassesCoalesceAndRetainWritesWhilePending()
    {
        var context = new Context();
        using var recomposer = new Recomposer(context);
        var applier = new Applier();
        using var composition = new Composition<Node>(applier, recomposer);
        var state = Composables.CreateMutableState(0);
        composition.SetContent((c, _, _) => Emit(c, state.Value));
        context.Drain();
        state.Value = 1; state.Value = 2;
        Assert.Equal(1, context.Count);
        context.Drain();
        Assert.Equal(2, applier.Root.Children[0].Value);
        state.Value = 3;
        Assert.True(composition.Recompose());
        state.Value = 4;
        context.Drain();
        composition.ApplyChanges();
        context.Drain();
        Assert.Equal(4, applier.Root.Children[0].Value);
    }

    [Fact]
    public void OnlyAppliedNestedSnapshotsInvalidateComposition()
    {
        var context = new Context();
        using var recomposer = new Recomposer(context);
        var applier = new Applier();
        using var composition = new Composition<Node>(applier, recomposer);
        var state = Composables.CreateMutableState(0);
        composition.SetContent((c, _, _) => Emit(c, state.Value)); context.Drain();
        using (var discarded = Snapshot.TakeMutableSnapshot()) discarded.Enter(() => state.Value = 8);
        context.Drain(); Assert.Equal(0, applier.Root.Children[0].Value);
        using var parent = Snapshot.TakeMutableSnapshot();
        parent.Enter(() =>
        {
            using var child = Snapshot.TakeMutableSnapshot();
            child.Enter(() => state.Value = 9);
            Assert.True(child.Apply().Succeeded);
        });
        context.Drain(); Assert.Equal(0, applier.Root.Children[0].Value);
        Assert.True(parent.Apply().Succeeded);
        context.Drain(); Assert.Equal(9, applier.Root.Children[0].Value);
    }

    private sealed class RememberObserver : IRememberObserver
    {
        public readonly List<string> Events = new();
        public void OnRemembered() => Events.Add("remembered");
        public void OnForgotten() => Events.Add("forgotten");
        public void OnAbandoned() => Events.Add("abandoned");
    }

    [Fact]
    public void RememberLifecycleIsBoundToCommitAndDiscard()
    {
        var first = new RememberObserver();
        var second = new RememberObserver();
        using var composition = new Composition<Node>(new Applier());
        composition.ComposeContent((c, _, _) => Composables.Builders.Remember(1, () => first, c));
        Assert.Empty(first.Events);
        composition.ApplyChanges();
        composition.ComposeContent((c, _, _) => Composables.Builders.Remember(2, () => second, c));
        composition.DiscardChanges();
        Assert.Equal(new[] { "remembered" }, first.Events);
        Assert.Equal(new[] { "abandoned" }, second.Events);
        composition.SetContent((c, _, _) => { });
        Assert.Equal(new[] { "remembered", "forgotten" }, first.Events);
    }

    [Fact]
    public void ExceptionsDoNotChangeCommittedTreeAndDisposeIsIdempotent()
    {
        var applier = new Applier();
        var composition = new Composition<Node>(applier);
        composition.SetContent((c, _, _) => Emit(c, 1));
        Assert.Throws<InvalidOperationException>(() => composition.ComposeContent((c, _, _) =>
        { Emit(c, 2); throw new InvalidOperationException(); }));
        Assert.Equal(1, applier.Root.Children[0].Value);
        Assert.Null(ComposeScope.GetCurrentContext());
        composition.ComposeContent((c, _, _) => Emit(c, 3));
        applier.ThrowOnUpdate = true;
        Assert.Throws<InvalidOperationException>(() => composition.ApplyChanges());
        Assert.True(composition.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => composition.Recompose());
        composition.Dispose(); composition.Dispose();
        Assert.Empty(applier.Root.Children);
        Assert.Throws<ObjectDisposedException>(() => composition.Recompose());
    }

    [Fact]
    public void LaunchedEffectStartsAfterApplyAndCancelsOnRemoval()
    {
        int started = 0, cancelled = 0;
        using var composition = new Composition<Node>(new Applier());
        composition.ComposeContent((c, _, _) => Composables.Builders.LaunchedEffect(1, token =>
        {
            started++;
            token.Register(() => cancelled++);
            return default;
        }, c));
        Assert.Equal(0, started);
        composition.ApplyChanges(); Assert.Equal(1, started);
        composition.SetContent((c, _, _) => { }); Assert.Equal(1, cancelled);
    }

    [Fact]
    public void LaunchedEffectFailureIsReportedOnOwningContext()
    {
        var context = new Context();
        using var recomposer = new Recomposer(context);
        using var composition = new Composition<Node>(new Applier(), recomposer);
        var expected = new InvalidOperationException("effect failed");
        Exception? observed = null;
        int ownerThread = Environment.CurrentManagedThreadId;
        recomposer.Error += (_, args) =>
        {
            Assert.Equal(ownerThread, Environment.CurrentManagedThreadId);
            observed = args.Exception;
        };

        composition.SetContent((c, _, _) => Composables.Builders.LaunchedEffect(1,
            _ => ValueTask.FromException(expected), c));
        context.Drain();

        Assert.Same(expected, observed);
    }

    [Fact]
    public void LaunchedEffectCancelsOnKeyChangeAndDispose()
    {
        var context = new Context();
        using var recomposer = new Recomposer(context);
        var composition = new Composition<Node>(new Applier(), recomposer);
        var key = Composables.CreateMutableState(1);
        int started = 0, cancelled = 0;
        composition.SetContent((c, _, _) => Composables.Builders.LaunchedEffect(key.Value, token =>
        {
            started++;
            token.Register(() => Interlocked.Increment(ref cancelled));
            return default;
        }, c));

        key.Value = 2;
        context.Drain();
        Assert.Equal(2, started);
        Assert.Equal(1, cancelled);

        composition.Dispose();
        Assert.Equal(2, cancelled);
    }

    [Fact]
    public void OneHundredThousandRecompositionsKeepStateRecordChainBounded()
    {
        var state = Composables.CreateMutableState(0);
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        composition.SetContent((c, _, _) => Emit(c, state.Value));

        const int iterations = 100_000;
        for (int value = 1; value <= iterations; value++)
        {
            state.Value = value;
            Assert.True(composition.Recompose());
            composition.ApplyChanges();
        }

        int records = 0;
        for (StateRecord? record = state.FirstStateRecord; record != null; record = record.Next) records++;
        Assert.Equal(iterations, applier.Root.Children[0].Value);
        Assert.InRange(records, 1, 4);
    }

    [Fact]
    public void BackgroundWritesUpdateAllCompositionsOnTheOwningThread()
    {
        var context = new Context();
        using var recomposer = new Recomposer(context);
        var a = new Applier(); var b = new Applier();
        using var first = new Composition<Node>(a, recomposer);
        using var second = new Composition<Node>(b, recomposer);
        var state = Composables.CreateMutableState(1);
        int ownerThread = Environment.CurrentManagedThreadId;
        ComposableAction content = (c, _, _) => { Assert.Equal(ownerThread, Environment.CurrentManagedThreadId); Emit(c, state.Value); };
        first.SetContent(content); second.SetContent(content); context.Drain();
        var thread = new Thread(() => state.Value = 2);
        thread.Start(); thread.Join();
        Assert.Equal(1, a.Root.Children[0].Value);
        context.Drain();
        Assert.Equal(2, a.Root.Children[0].Value);
        Assert.Equal(2, b.Root.Children[0].Value);
    }

    [Fact]
    public void WriteDuringApplicationSchedulesAnotherPass()
    {
        var context = new Context();
        using var recomposer = new Recomposer(context);
        var applier = new Applier();
        using var composition = new Composition<Node>(applier, recomposer);
        var state = Composables.CreateMutableState(0);
        composition.SetContent((c, _, _) =>
        {
            c.StartNode(); if (c.Inserting) c.CreateNode(() => new Node()); else c.UseNode();
            c.ApplyNode<Node, int>(state.Value, (node, value) => { node.Value = value; if (value == 1) state.Value = 2; });
            c.EndNode();
        });
        state.Value = 1; context.Drain();
        Assert.Equal(2, applier.Root.Children[0].Value);
    }

    [Fact]
    public void AutomaticErrorsAreReportedWithoutAnInfiniteRetry()
    {
        var context = new Context();
        using var recomposer = new Recomposer(context);
        using var composition = new Composition<Node>(new Applier(), recomposer);
        var state = Composables.CreateMutableState(0);
        int errors = 0;
        recomposer.Error += (_, e) => { Assert.Same(composition, e.Composition); errors++; };
        composition.SetContent((c, _, _) => { if (state.Value == 1) throw new InvalidOperationException("User code"); Emit(c, 0); });
        state.Value = 1; context.Drain();
        Assert.Equal(1, errors);
        Assert.False(composition.IsFaulted);
        state.Value = 2; context.Drain();
        Assert.False(composition.HasInvalidations);
    }

    [Fact]
    public void SnapshotConflictDiscardsBatchBeforeApplyingAnyNodes()
    {
        var state = Composables.CreateMutableState(0);
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        composition.SetContent((c, _, _) => Emit(c, 0));
        composition.ComposeContent((c, _, _) => { state.Value = 1; Emit(c, 1); });
        state.Value = 2;
        Assert.Throws<InvalidOperationException>(() => composition.ApplyChanges());
        Assert.False(composition.HasPendingChanges);
        Assert.False(composition.IsFaulted);
        Assert.Equal(0, applier.Root.Children[0].Value);
        Assert.Equal(2, state.Value);
    }

    [Fact]
    public void ActiveReaderBlocksApplyWithoutConsumingTheBatch()
    {
        using var composition = new Composition<Node>(new Applier());
        composition.ComposeContent((c, _, _) => Emit(c, 1));
        using (var reader = composition.SlotTable.OpenReader())
            Assert.Throws<InvalidOperationException>(() => composition.ApplyChanges());
        composition.ApplyChanges();
    }

    [Fact]
    public void NullRememberValueAndBalancedGroupValidation()
    {
        using var composition = new Composition<Node>(new Applier());
        int creations = 0;
        ComposableAction content = (c, _, _) => Assert.Null(Composables.Builders.Remember<object?>(1, () => { creations++; return null; }, c));
        composition.SetContent(content); composition.SetContent(content);
        Assert.Equal(1, creations);
        Assert.Throws<InvalidOperationException>(() => composition.ComposeContent((c, _, _) => c.EndGroup()));
        Assert.Throws<InvalidOperationException>(() => composition.ComposeContent((c, _, _) => c.StartGroup(1)));
        Assert.Throws<InvalidOperationException>(() => composition.ComposeContent((c, _, _) => { c.StartNode(); c.EndNode(); }));
        composition.SetContent(content);
        Assert.Equal(1, creations);
    }

    [Fact]
    public void WriterConflictDoesNotLeaveTheCompositionBusy()
    {
        using var composition = new Composition<Node>(new Applier());
        using (var writer = composition.SlotTable.OpenWriter())
            Assert.Throws<InvalidOperationException>(() => composition.ComposeContent((c, _, _) => Emit(c, 1)));
        composition.SetContent((c, _, _) => Emit(c, 2));
    }

    [Fact]
    public void AppliedBatchKeepsItsPublicOperationsAndReleasesExecutionReferences()
    {
        Applier applier = new Applier();
        using Composition<Node> composition = new Composition<Node>(applier);
        composition.ComposeContent((composer, _, _) => Emit(composer, 7));
        CompositionChangeSet changes = composition.PendingChanges!;
        CompositionOperation[] before = changes.ToArray();

        Assert.Contains(before, operation => operation.Kind == CompositionOperationKind.CreateNode);
        Assert.NotNull(changes.ExecutableAt(0).Group);
        Assert.NotNull(changes.InsertTable);
        foreach (CompositionOperation operation in before)
        {
            Assert.Null(operation.Group);
            Assert.Null(operation.Parent);
            Assert.Null(operation.Update);
        }

        composition.ApplyChanges();

        Assert.True(changes.IsConsumed);
        Assert.Throws<InvalidOperationException>(() => { _ = changes.InsertTable; });
        Assert.Equal(before.Length, changes.Count);
        Assert.Equal(7, Assert.Single(applier.Root.Children).Value);
        for (int index = 0; index < before.Length; index++)
        {
            CompositionOperation after = changes[index];
            Assert.Equal(before[index].Kind, after.Kind);
            Assert.Equal(before[index].GroupIndex, after.GroupIndex);
            Assert.Equal(before[index].SourceGroupIndex, after.SourceGroupIndex);
            Assert.Equal(before[index].NodeIndex, after.NodeIndex);
            Assert.Equal(before[index].SourceNodeIndex, after.SourceNodeIndex);
            Assert.Equal(before[index].SlotOffset, after.SlotOffset);
            Assert.Equal(before[index].Count, after.Count);
            Assert.Equal(before[index].Key, after.Key);
            Assert.Same(before[index].Value, after.Value);
            Assert.Equal(before[index].ToString(), after.ToString());
            Assert.Null(changes.ExecutableAt(index).Group);
            Assert.Null(changes.ExecutableAt(index).Parent);
            Assert.Null(changes.ExecutableAt(index).Update);
        }
    }

    [Fact]
    public void DiscardedNullSlotUpdateKeepsItsDescriptionAndCommittedValue()
    {
        using Composition<Node> composition = new Composition<Node>(new Applier());
        object original = new object();
        object? value = original;
        ComposableAction content = (composer, _, _) => composer.Changed(value);
        composition.SetContent(content);

        value = null;
        composition.ComposeContent(content);
        CompositionChangeSet changes = composition.PendingChanges!;
        CompositionOperation update = Assert.Single(changes, operation => operation.Kind == CompositionOperationKind.UpdateSlot);
        Assert.Null(update.Value);
        Assert.NotNull(changes.ExecutableAt(0).Group);
        Assert.NotNull(changes.InsertTable);

        composition.DiscardChanges();

        Assert.True(changes.IsConsumed);
        Assert.Throws<InvalidOperationException>(() => { _ = changes.InsertTable; });
        Assert.Null(Assert.Single(changes, operation => operation.Kind == CompositionOperationKind.UpdateSlot).Value);
        Assert.Null(changes.ExecutableAt(0).Group);
        using ComposerSlotTable.Reader reader = composition.SlotTable.OpenReader();
        Assert.Same(original, reader.GroupGet(0, 0));
    }

    [Fact]
    public void DiscardedInsertionReleasesItsTemporaryTableAndCanBeComputedAgain()
    {
        Applier applier = new Applier();
        using Composition<Node> composition = new Composition<Node>(applier);
        composition.ComposeContent((composer, _, _) => Emit(composer, 7));
        CompositionChangeSet changes = composition.PendingChanges!;
        CompositionOperation[] before = changes.ToArray();
        Assert.True(changes.InsertTable.Size > 0);

        composition.DiscardChanges();

        Assert.True(changes.IsConsumed);
        Assert.Throws<InvalidOperationException>(() => { _ = changes.InsertTable; });
        Assert.Equal(before.Select(operation => operation.ToString()), changes.Select(operation => operation.ToString()));
        Assert.Empty(applier.Root.Children);
        Assert.Equal(0, composition.SlotTable.Size);

        composition.SetContent((composer, _, _) => Emit(composer, 9));
        Assert.Equal(9, Assert.Single(applier.Root.Children).Value);
    }

    [Fact]
    public void AppendedNullSlotAndTrimmedSlotUseTheSamePreparedCommandPath()
    {
        using Composition<Node> composition = new Composition<Node>(new Applier());
        bool includeSlot = false;
        ComposableAction content = (composer, _, _) =>
        {
            composer.StartGroup(5);
            if (includeSlot) composer.Changed<object?>(null);
            composer.EndGroup();
        };
        composition.SetContent(content);

        includeSlot = true;
        composition.ComposeContent(content);
        CompositionChangeSet appended = composition.PendingChanges!;
        CompositionOperation append = Assert.Single(appended, operation => operation.Kind == CompositionOperationKind.AppendSlot);
        Assert.Null(append.Value);
        Assert.Equal(0, append.SlotOffset);
        composition.ApplyChanges();
        using (ComposerSlotTable.Reader reader = composition.SlotTable.OpenReader())
        {
            Assert.Equal(1, reader.GetSlotSize(1));
            Assert.Null(reader.GroupGet(1, 0));
        }

        includeSlot = false;
        composition.ComposeContent(content);
        CompositionChangeSet trimmed = composition.PendingChanges!;
        CompositionOperation trim = Assert.Single(trimmed, operation => operation.Kind == CompositionOperationKind.TrimSlots);
        Assert.Equal(0, trim.SlotOffset);
        Assert.Equal(1, trim.Count);
        composition.ApplyChanges();
        using ComposerSlotTable.Reader finalReader = composition.SlotTable.OpenReader();
        Assert.Equal(0, finalReader.GetSlotSize(1));
    }

    [Fact]
    public void InvalidPreparedOperationsFailValidationBeforeExecution()
    {
        CompositionChangeSet unknown = new CompositionChangeSet(new object(), 0,
            new List<CompositionOperation> { new CompositionOperation((CompositionOperationKind)int.MaxValue) },
            new ComposerSlotTable());
        Assert.Throws<NotSupportedException>(() => unknown.Validate());

        CompositionChangeSet incomplete = new CompositionChangeSet(new object(), 0,
            new List<CompositionOperation> { default }, new ComposerSlotTable());
        Assert.Throws<InvalidOperationException>(() => incomplete.Validate());
    }
}

[CollectionDefinition("Snapshots", DisableParallelization = true)]
public class SnapshotCollection { }
