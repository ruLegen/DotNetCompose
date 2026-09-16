using System;
using System.Collections.Generic;
using System.Linq;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.SlotTable;
using Xunit;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public class NodeInsertionOrderTests
{
    private sealed class Node
    {
        public Node(int value) => Value = value;
        public int Value;
        public readonly List<Node> Children = new List<Node>();
    }

    private sealed class RecordingApplier : IApplier<Node>
    {
        private readonly bool _buildBottomUp;
        private readonly Stack<Node> _parents = new Stack<Node>();

        public RecordingApplier(bool buildBottomUp) => _buildBottomUp = buildBottomUp;

        public Node Root { get; } = new Node(0);
        public List<string> Events { get; } = new List<string>();
        public Node Current => _parents.Count == 0 ? Root : _parents.Peek();

        public void OnBeginChanges() => Events.Add("begin");
        public void OnEndChanges() => Events.Add("end");

        public void Down(Node node)
        {
            Events.Add($"down:{node.Value}");
            _parents.Push(node);
        }

        public void Up()
        {
            Events.Add($"up:{Current.Value}");
            _parents.Pop();
        }

        public void InsertTopDown(int index, Node instance)
        {
            Events.Add($"top:{instance.Value}@{index}");
            if (!_buildBottomUp) Current.Children.Insert(index, instance);
        }

        public void InsertBottomUp(int index, Node instance)
        {
            Events.Add($"bottom:{instance.Value}@{index}");
            if (_buildBottomUp) Current.Children.Insert(index, instance);
        }

        public void Remove(int index, int count)
        {
            Events.Add($"remove:{index}x{count}");
            Current.Children.RemoveRange(index, count);
        }

        public void Move(int from, int to, int count)
        {
            Events.Add($"move:{from}->{to}x{count}");
            List<Node> moved = Current.Children.GetRange(from, count);
            Current.Children.RemoveRange(from, count);
            Current.Children.InsertRange(to > from ? to - count : to, moved);
        }

        public void Clear()
        {
            _parents.Clear();
            Root.Children.Clear();
        }

        public void Apply(Action<Node, object?> block, object? value)
        {
            Events.Add($"update:{Current.Value}");
            block(Current, value);
        }
    }

    private static void EmitNode(IComposerContext composer, int key, int value,
        List<string> events, Action<IComposerContext>? children = null)
    {
        composer.StartNode(key);
        if (composer.Inserting)
            composer.CreateNode(() =>
            {
                events.Add($"factory:{value}");
                return new Node(value);
            });
        else composer.UseNode();
        composer.ApplyNode<Node, int>(value, (node, updated) => node.Value = updated);
        children?.Invoke(composer);
        composer.EndNode();
    }

    private static string Tree(Node node) =>
        $"{node.Value}[{string.Join(",", node.Children.Select(Tree))}]";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedInsertionCreatesNodesInParentChildOrder(bool buildBottomUp)
    {
        RecordingApplier applier = new RecordingApplier(buildBottomUp);
        using Composition<Node> composition = new Composition<Node>(applier);

        composition.ComposeContent((composer, _, _) =>
            EmitNode(composer, 1, 10, applier.Events, parent =>
                EmitNode(parent, 2, 20, applier.Events)));

        Assert.Empty(applier.Events);
        Assert.Empty(applier.Root.Children);
        Assert.Equal(0, composition.SlotTable.Size);
        CompositionChangeSet changes = composition.PendingChanges!;
        using (ComposerSlotTable.Reader pendingReader = changes.InsertTable.OpenReader())
        {
            Assert.Null(pendingReader.GetNode(1));
            Assert.Null(pendingReader.GetNode(2));
        }
        Assert.Equal(new[]
        {
            CompositionOperationKind.InsertGroup,
            CompositionOperationKind.CreateNode,
            CompositionOperationKind.InsertTopDown,
            CompositionOperationKind.Down,
            CompositionOperationKind.UpdateNode,
            CompositionOperationKind.CreateNode,
            CompositionOperationKind.InsertTopDown,
            CompositionOperationKind.Down,
            CompositionOperationKind.UpdateNode,
            CompositionOperationKind.Up,
            CompositionOperationKind.InsertBottomUp,
            CompositionOperationKind.Up,
            CompositionOperationKind.InsertBottomUp
        }, changes.Select(operation => operation.Kind));

        composition.ApplyChanges();

        Assert.Equal("0[10[20[]]]", Tree(applier.Root));
        Assert.Same(applier.Root, applier.Current);
        Assert.Equal(new[]
        {
            "begin", "factory:10", "top:10@0", "down:10", "update:10",
            "factory:20", "top:20@0", "down:20", "update:20",
            "up:20", "bottom:20@0", "up:10", "bottom:10@0", "end"
        }, applier.Events);

        Node parentNode = Assert.Single(applier.Root.Children);
        Node childNode = Assert.Single(parentNode.Children);
        using ComposerSlotTable.Reader reader = composition.SlotTable.OpenReader();
        Assert.Same(parentNode, reader.GetNode(1));
        Assert.Same(childNode, reader.GetNode(2));
    }

    [Fact]
    public void TopDownAndBottomUpKeepTheSameTreeAcrossNestedEdits()
    {
        string[] topDown = RunNestedEdits(buildBottomUp: false);
        string[] bottomUp = RunNestedEdits(buildBottomUp: true);

        Assert.Equal(new[]
        {
            "0[100[101[]],201[],202[]]",
            "0[100[101[],102[]],201[],202[]]",
            "0[110[101[],102[]],202[],201[]]",
            "0[110[101[]],202[]]"
        }, topDown);
        Assert.Equal(topDown, bottomUp);
    }

    private static string[] RunNestedEdits(bool buildBottomUp)
    {
        RecordingApplier applier = new RecordingApplier(buildBottomUp);
        using Composition<Node> composition = new Composition<Node>(applier);
        int parentKey = 1;
        int parentValue = 100;
        bool secondChild = false;
        int[] siblingKeys = { 1, 2 };
        ComposableAction content = (composer, _, _) =>
        {
            EmitNode(composer, parentKey, parentValue, applier.Events, parent =>
            {
                EmitNode(parent, 11, 101, applier.Events);
                if (secondChild) EmitNode(parent, 12, 102, applier.Events);
            });
            foreach (int siblingKey in siblingKeys)
            {
                composer.StartMovableGroup(50, siblingKey);
                EmitNode(composer, 20, 200 + siblingKey, applier.Events);
                composer.EndMovableGroup(50);
            }
        };

        composition.SetContent(content);
        Node firstParent = applier.Root.Children[0];
        Node firstChild = firstParent.Children[0];
        Node firstSibling = applier.Root.Children[1];
        Node secondSibling = applier.Root.Children[2];
        string initial = Tree(applier.Root);

        secondChild = true;
        int eventCount = applier.Events.Count;
        composition.ComposeContent(content);
        Assert.Equal(initial, Tree(applier.Root));
        Assert.Equal(eventCount, applier.Events.Count);
        Assert.Contains(composition.PendingChanges!, operation => operation.Kind == CompositionOperationKind.CreateNode);
        composition.ApplyChanges();
        Assert.Same(firstParent, applier.Root.Children[0]);
        Assert.Same(firstChild, firstParent.Children[0]);
        string insertedChild = Tree(applier.Root);

        parentKey = 2;
        parentValue = 110;
        siblingKeys = new[] { 2, 1 };
        composition.ComposeContent(content);
        Assert.Equal(insertedChild, Tree(applier.Root));
        Assert.Contains(composition.PendingChanges!, operation => operation.Kind == CompositionOperationKind.InsertGroup);
        Assert.Contains(composition.PendingChanges!, operation => operation.Kind == CompositionOperationKind.RemoveGroup);
        Assert.Contains(composition.PendingChanges!, operation => operation.Kind == CompositionOperationKind.MoveNode);
        composition.ApplyChanges();
        Assert.NotSame(firstParent, applier.Root.Children[0]);
        Assert.Same(secondSibling, applier.Root.Children[1]);
        Assert.Same(firstSibling, applier.Root.Children[2]);
        Node replacementParent = applier.Root.Children[0];
        Node replacementChild = replacementParent.Children[0];
        string replacedParent = Tree(applier.Root);

        secondChild = false;
        siblingKeys = new[] { 2 };
        composition.SetContent(content);
        Assert.Same(replacementParent, applier.Root.Children[0]);
        Assert.Same(replacementChild, replacementParent.Children[0]);
        Assert.Same(secondSibling, applier.Root.Children[1]);
        Assert.Same(applier.Root, applier.Current);
        return new[] { initial, insertedChild, replacedParent, Tree(applier.Root) };
    }
}
