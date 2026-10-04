using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Effects;
using DotNetCompose.Runtime.SlotTable;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public sealed class CompositionReusingTests
{
    public sealed class Node : IComposeNodeLifecycleCallback
    {
        public readonly List<Node> Children = new();
        public readonly List<string> Events = new();
        public int Value;
        public bool ThrowOnReuse, ThrowOnRelease;
        public void OnReuse()
        {
            Events.Add("Reuse");
            if (ThrowOnReuse) throw new ApplicationException("Reuse failed");
        }
        public void OnDeactivate() => Events.Add("Deactivate");
        public void OnRelease()
        {
            Events.Add("Release");
            if (ThrowOnRelease) throw new ApplicationException("Release failed");
        }
    }

    public class Applier : IApplier<Node>
    {
        public readonly Node Root = new();
        private readonly Stack<Node> _path = new();
        public Node Current => _path.Count == 0 ? Root : _path.Peek();
        public void OnBeginChanges() { }
        public void OnEndChanges() { }
        public void Down(Node node) => _path.Push(node);
        public void Up() => _path.Pop();
        public void InsertTopDown(int index, Node node) => Current.Children.Insert(index, node);
        public void InsertBottomUp(int index, Node node) { }
        public void Remove(int index, int count) => Current.Children.RemoveRange(index, count);
        public void Move(int from, int to, int count)
        {
            var moved = Current.Children.GetRange(from, count);
            Current.Children.RemoveRange(from, count);
            Current.Children.InsertRange(to > from ? to - count : to, moved);
        }
        public void Clear() { _path.Clear(); Root.Children.Clear(); }
        public void Apply(Action<Node, object?> block, object? value) => block(Current, value);
    }

    private sealed class ReuseApplier(bool forwardLifecycle) : Applier, IApplier<Node>
    {
        public readonly List<Node> ReusedNodes = new();

        public void Reuse()
        {
            ReusedNodes.Add(Current);
            Current.Events.Add("ApplierReuse");
            if (forwardLifecycle) Current.OnReuse();
        }
    }

    private sealed class Observer : IRememberObserver
    {
        public int Remembered, Forgotten, Abandoned;
        public void OnRemembered() => Remembered++;
        public void OnForgotten() => Forgotten++;
        public void OnAbandoned() => Abandoned++;
    }

    private static T Remember<T>(IComposerContext c, Func<T> factory)
    {
        object? value = c.RememberedValue();
        if (ReferenceEquals(value, ComposerSlotTable.Empty))
        {
            value = factory();
            c.UpdateRememberedValue(value);
        }
        return (T)value!;
    }

    private static void Emit(IComposerContext c, int key, bool reusable = true,
        Action<IComposerContext>? content = null, int value = 1)
    {
        if (reusable) c.StartReusableNode(key); else c.StartNode(key);
        if (c.Inserting) c.CreateNode(() => new Node()); else c.UseNode();
        c.ApplyNode<Node, int>(value, (node, next) =>
        {
            node.Events.Add("Set");
            node.Value = next;
        });
        content?.Invoke(c);
        c.EndNode();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplierOwnsReuseNotificationForTheCurrentNodeBeforeUpdates(bool forwardLifecycle)
    {
        var applier = new ReuseApplier(forwardLifecycle);
        using var composition = new Composition<Node>(applier);
        int key = 0;
        ComposableAction content = (c, _, _) =>
        {
            c.StartReusableGroup(100, key);
            Emit(c, 10, content: nested => Emit(nested, 20));
            c.EndReusableGroup(100);
        };
        composition.SetContent(content);
        Node parent = Assert.Single(applier.Root.Children);
        Node child = Assert.Single(parent.Children);
        parent.Events.Clear(); child.Events.Clear();
        key++;
        composition.ComposeContent(content);
        Assert.Empty(applier.ReusedNodes);
        composition.ApplyChanges();
        Assert.Equal(new[] { parent, child }, applier.ReusedNodes);
        string[] expected = forwardLifecycle
            ? ["ApplierReuse", "Reuse", "Set"] : ["ApplierReuse", "Set"];
        Assert.Equal(expected, parent.Events);
        Assert.Equal(expected, child.Events);
        Assert.Same(parent, Assert.Single(applier.Root.Children));
        Assert.Same(child, Assert.Single(parent.Children));
    }

    [Fact]
    public void ReusedNodesWithoutLifecycleDoNotRecordReuseOperations()
    {
        var applier = new CompositionTests.Applier();
        using var composition = new Composition<CompositionTests.Node>(applier);
        int key = 0;
        ComposableAction content = (c, _, _) =>
        {
            c.StartReusableGroup(100, key);
            c.StartReusableNode(10);
            if (c.Inserting) c.CreateNode(() => new CompositionTests.Node()); else c.UseNode();
            c.ApplyNode<CompositionTests.Node, int>(key, (node, next) => node.Value = next);
            c.EndNode();
            c.EndReusableGroup(100);
        };
        composition.SetContent(content);
        var node = Assert.Single(applier.Root.Children);
        key++;
        composition.ComposeContent(content);
        Assert.DoesNotContain(composition.PendingChanges!, operation =>
            operation.Kind == CompositionOperationKind.ReuseNode);
        composition.ApplyChanges();
        Assert.Same(node, Assert.Single(applier.Root.Children));
        Assert.Equal(1, node.Value);
    }

    [Fact]
    public void ReuseRetainsNodesButRecreatesRememberedStateAndForcesEqualSetters()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        object? key = 0;
        Observer? current = null;
        ComposableAction content = (c, _, _) =>
        {
            c.StartReusableGroup(100, key);
            current = Remember(c, () => new Observer());
            Emit(c, 10);
            c.EndReusableGroup(100);
        };
        composition.SetContent(content);
        Node node = Assert.Single(applier.Root.Children);
        Observer first = current!;
        node.Events.Clear();
        composition.SetContent(content);
        Assert.Same(first, current);
        Assert.Empty(node.Events);
        key = 1;
        composition.ComposeContent(content);
        Observer second = current!;
        Assert.NotSame(first, second);
        Assert.Equal(0, first.Forgotten);
        Assert.Empty(node.Events);
        composition.ApplyChanges();
        Assert.Same(node, Assert.Single(applier.Root.Children));
        Assert.Equal(new[] { "Reuse", "Set" }, node.Events);
        Assert.Equal(1, first.Forgotten);
        Assert.Equal(1, second.Remembered);
    }

    [Fact]
    public void NonReusableNodesReplaceTheirEntireSubtreeWhileSiblingsSurvive()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        int key = 0;
        ComposableAction content = (c, _, _) =>
        {
            c.StartReusableGroup(100, key);
            Emit(c, 10);
            Emit(c, 20, false, child => Emit(child, 30));
            Emit(c, 40);
            c.EndReusableGroup(100);
        };
        composition.SetContent(content);
        Node[] before = applier.Root.Children.ToArray();
        Node oldChild = Assert.Single(before[1].Children);
        key++;
        composition.SetContent(content);
        Assert.Same(before[0], applier.Root.Children[0]);
        Assert.NotSame(before[1], applier.Root.Children[1]);
        Assert.NotSame(oldChild, Assert.Single(applier.Root.Children[1].Children));
        Assert.Same(before[2], applier.Root.Children[2]);
        Assert.Equal("Release", before[1].Events.Last());
        Assert.Equal("Release", oldChild.Events.Last());
        Assert.DoesNotContain("Release", before[0].Events);
    }

    [Fact]
    public void HostDeactivationForgetsStateStopsObservationAndRetainsNodes()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        bool active = true;
        var state = Composables.CreateMutableState(0);
        Observer? current = null;
        int executions = 0;
        ComposableAction content = (c, _, _) => Composables.Builders.ReusableContentHost(active, (child, _, _) =>
        {
            executions++;
            _ = state.Value;
            current = Remember(child, () => new Observer());
            Emit(child, 10, content: nested => Emit(nested, 20));
        }, c);
        composition.SetContent(content);
        Node parent = Assert.Single(applier.Root.Children);
        Node nested = Assert.Single(parent.Children);
        Observer first = current!;
        parent.Events.Clear(); nested.Events.Clear();
        active = false;
        composition.ComposeContent(content);
        Assert.Empty(parent.Events);
        Assert.Equal(0, first.Forgotten);
        composition.ApplyChanges();
        Assert.Same(parent, Assert.Single(applier.Root.Children));
        Assert.Same(nested, Assert.Single(parent.Children));
        Assert.Equal(new[] { "Deactivate" }, parent.Events);
        Assert.Equal(new[] { "Deactivate" }, nested.Events);
        Assert.Equal(1, first.Forgotten);
        composition.SetContent(content);
        state.Value++;
        Assert.False(composition.Recompose());
        Assert.Equal(1, executions);
        Assert.Equal(new[] { "Deactivate" }, parent.Events);
        active = true;
        composition.SetContent(content);
        Assert.Same(parent, Assert.Single(applier.Root.Children));
        Assert.NotSame(first, current);
        Assert.Equal(new[] { "Deactivate", "Reuse", "Set" }, parent.Events);
        Assert.Equal(new[] { "Deactivate", "Reuse", "Set" }, nested.Events);
        Assert.Equal(1, current!.Remembered);
    }

    [Fact]
    public void DiscardAndComputeFailureDoNotApplyReuseOrDeactivate()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        int key = 0;
        bool active = true, fail = false;
        Observer? current = null;
        ComposableAction content = (c, _, _) =>
        {
            c.StartReusableGroup(100, key);
            if (!active) c.DeactivateToEndGroup();
            else
            {
                current = Remember(c, () => new Observer());
                Emit(c, 10);
            }
            c.EndReusableGroup(100);
            if (fail) throw new ApplicationException();
        };
        composition.SetContent(content);
        Node node = Assert.Single(applier.Root.Children);
        Observer first = current!;
        node.Events.Clear();
        key++;
        composition.ComposeContent(content);
        Observer abandoned = current!;
        composition.DiscardChanges();
        Assert.Equal(1, abandoned.Abandoned);
        Assert.Equal(0, first.Forgotten);
        active = false;
        composition.ComposeContent(content);
        composition.DiscardChanges();
        active = true; fail = true;
        Assert.Throws<ApplicationException>(() => composition.ComposeContent(content));
        Assert.Empty(node.Events);
        Assert.Equal(0, first.Forgotten);
        fail = false;
        composition.SetContent(content);
        Assert.Same(node, Assert.Single(applier.Root.Children));
        Assert.Equal(new[] { "Reuse", "Set" }, node.Events);
        Assert.Equal(1, first.Forgotten);
    }

    [Fact]
    public void StructuralRemovalAndDisposeReleaseEveryNodeExactlyOnce()
    {
        var applier = new Applier();
        var composition = new Composition<Node>(applier);
        bool include = true, active = true;
        ComposableAction content = (c, _, _) => Composables.Builders.ReusableContentHost(active, (child, _, _) =>
        {
            Emit(child, 10, content: nested =>
            {
                if (include) Emit(nested, 20, content: leaf => Emit(leaf, 30));
                Emit(nested, 40);
            });
        }, c);
        composition.SetContent(content);
        Node root = Assert.Single(applier.Root.Children);
        Node removed = root.Children[0];
        Node removedLeaf = Assert.Single(removed.Children);
        Node retained = root.Children[1];
        include = false;
        composition.SetContent(content);
        Assert.Same(retained, Assert.Single(root.Children));
        Assert.Equal(1, removed.Events.Count(x => x == "Release"));
        Assert.Equal(1, removedLeaf.Events.Count(x => x == "Release"));
        active = false;
        composition.SetContent(content);
        composition.Dispose(); composition.Dispose();
        Assert.Equal(1, root.Events.Count(x => x == "Release"));
        Assert.Equal(1, retained.Events.Count(x => x == "Release"));
        Assert.Equal(1, removed.Events.Count(x => x == "Release"));
        Assert.Empty(applier.Root.Children);
    }

    [Fact]
    public void NestedReuseRestoresOuterModeAndProviderValues()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        int outer = 0, inner = 0;
        var local = Composables.CompositionLocalOf(() => -1);
        object? before = null, inside = null, after = null;
        ComposableAction content = (c, _, _) =>
        {
            c.StartReusableGroup(100, outer);
            c.StartProvider(local.Provides(outer));
            before = Remember(c, () => new object());
            c.StartReusableGroup(101, inner);
            inside = Remember(c, () => new object());
            Emit(c, 10, value: c.Consume(local));
            c.EndReusableGroup(101);
            after = Remember(c, () => new object());
            c.EndProvider();
            c.EndReusableGroup(100);
        };
        composition.SetContent(content);
        var initial = (before, inside, after);
        Node node = Assert.Single(applier.Root.Children);
        inner++;
        composition.SetContent(content);
        Assert.Same(initial.before, before);
        Assert.NotSame(initial.inside, inside);
        Assert.Same(initial.after, after);
        var previous = (before, inside, after);
        outer++;
        composition.SetContent(content);
        Assert.NotSame(previous.before, before);
        Assert.NotSame(previous.inside, inside);
        Assert.NotSame(previous.after, after);
        Assert.Same(node, Assert.Single(applier.Root.Children));
        Assert.Equal(1, node.Value);
    }

    [Fact]
    public void ReturningTheSameObserverStillForgetsAndRemembersItDuringReuse()
    {
        using var composition = new Composition<Node>(new Applier());
        var observer = new Observer();
        int key = 0;
        ComposableAction content = (c, _, _) =>
        {
            c.StartReusableGroup(100, key);
            Remember(c, () => observer);
            c.EndReusableGroup(100);
        };
        composition.SetContent(content);
        key++;
        composition.SetContent(content);
        Assert.Equal(2, observer.Remembered);
        Assert.Equal(1, observer.Forgotten);
        Assert.Equal(0, observer.Abandoned);
    }

    [Fact]
    public void LifecycleFailureFaultsApplyAndDisposeStillReleasesAllNodesOnce()
    {
        var applier = new Applier();
        var composition = new Composition<Node>(applier);
        int key = 0;
        ComposableAction content = (c, _, _) =>
        {
            c.StartReusableGroup(100, key);
            Emit(c, 10, content: child => Emit(child, 20));
            c.EndReusableGroup(100);
        };
        composition.SetContent(content);
        Node parent = Assert.Single(applier.Root.Children);
        Node child = Assert.Single(parent.Children);
        parent.ThrowOnReuse = true;
        child.ThrowOnRelease = true;
        key++;
        composition.ComposeContent(content);
        Assert.Throws<ApplicationException>(() => composition.ApplyChanges());
        Assert.True(composition.IsFaulted);
        Assert.Throws<ApplicationException>(() => composition.Dispose());
        composition.Dispose();
        Assert.Equal(1, parent.Events.Count(x => x == "Release"));
        Assert.Equal(1, child.Events.Count(x => x == "Release"));
        Assert.Empty(applier.Root.Children);
        Assert.Equal(0, composition.SlotTable.Size);
    }

    [Fact]
    public void FailedReleaseIsNotRepeatedAndPartiallyInsertedNodesAreReleasedOnDispose()
    {
        var applier = new Applier();
        var composition = new Composition<Node>(applier);
        bool replace = false;
        ComposableAction content = (c, _, _) => Emit(c, replace ? 20 : 10);
        composition.SetContent(content);
        Node old = Assert.Single(applier.Root.Children);
        old.ThrowOnRelease = true;
        replace = true;
        composition.ComposeContent(content);
        Assert.Throws<ApplicationException>(() => composition.ApplyChanges());
        Node inserted = Assert.Single(applier.Root.Children);
        Assert.NotSame(old, inserted);
        composition.Dispose();
        Assert.Equal(1, old.Events.Count(x => x == "Release"));
        Assert.Equal(1, inserted.Events.Count(x => x == "Release"));
    }

    [Fact]
    public void OrdinaryRecompositionDoesNotReuseOrDeactivateNodes()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        composition.SetContent((c, _, _) => Emit(c, 10));
        Node node = Assert.Single(applier.Root.Children);
        node.Events.Clear();
        composition.SetContent((c, _, _) => Emit(c, 10, value: 2));
        Assert.Equal(new[] { "Set" }, node.Events);
        Assert.Same(node, Assert.Single(applier.Root.Children));
    }

    [Fact]
    public void PublicNodeWrappersPreserveChildrenDuringReuse()
    {
        var applier = new Applier();
        using var composition = new Composition<Node>(applier);
        int key = 0, factories = 0;
        ComposableAction content = (c, _, _) => Composables.Builders.ReusableContent(key, (child, _, _) =>
        {
            Composables.Builders.ReusableComposeNode(() => { factories++; return new Node(); }, node => node.Value = key,
                (nested, _, _) => Composables.Builders.ReusableComposeNode(() => { factories++; return new Node(); },
                    node => node.Value = key, nested), child);
        }, c);
        composition.SetContent(content);
        Node root = Assert.Single(applier.Root.Children);
        Node leaf = Assert.Single(root.Children);
        key++;
        composition.SetContent(content);
        Assert.Equal(2, factories);
        Assert.Same(root, Assert.Single(applier.Root.Children));
        Assert.Same(leaf, Assert.Single(root.Children));
        Assert.Equal(1, root.Value);
        Assert.Equal(1, leaf.Value);
    }
}
