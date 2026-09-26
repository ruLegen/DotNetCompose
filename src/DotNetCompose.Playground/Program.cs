using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Playground;

internal static class Program
{
    private static void Main()
    {
        // COmposition Locals => Context retrieving
        // Lambda handling GetLambda (start group and so on)


        // A UI application supplies its own SynchronizationContext.
        DemoContext context = new DemoContext();
        using Recomposer recomposer = new Recomposer(context);
        TreeApplier applier = new TreeApplier();
        using Composition<TextNode> composition = new Composition<TextNode>(applier, recomposer);
        SnapshotMutableState<int> count = Composables.CreateMutableState(0);
        composition.ComposeContent((composer, changed, defaults) =>
        {
            composer.StartNode(10);
            if (composer.Inserting) composer.CreateNode(() => new TextNode());
            else composer.UseNode();
            composer.ApplyNode<TextNode, string>($"Count: {count.Value}", (node, text) => node.Text = text);
            composer.EndNode();
        });
        PrintChanges(composition);
        Console.WriteLine($"Nodes before ApplyChanges: {applier.Root.Children.Count}");
        composition.ApplyChanges();
        TextNode originalNode = applier.Root.Children[0];
        Console.WriteLine(originalNode.Text);

        count.Value = 1;
        if (composition.Recompose())
        {
            PrintChanges(composition);
            Console.WriteLine($"Before ApplyChanges: {originalNode.Text}");
            composition.ApplyChanges();
        }
        count.Value = 2;
        count.Value = 3;
        context.Drain();
        Console.WriteLine($"After automatic recomposition: {originalNode.Text}");
        Console.WriteLine($"Same node: {ReferenceEquals(originalNode, applier.Root.Children[0])}");
    }

    private static void PrintChanges(IControlledComposition composition)
    {
        Console.WriteLine("Pending operations:");
        foreach (CompositionOperation operation in composition.PendingChanges!) Console.WriteLine($"  {operation}");
    }

    private sealed class TextNode
    {
        public string Text { get; set; } = string.Empty;
        public List<TextNode> Children { get; } = new List<TextNode>();
    }

    private sealed class TreeApplier : IApplier<TextNode>
    {
        private readonly Stack<TextNode> _path = new Stack<TextNode>();
        public TextNode Root { get; } = new TextNode();
        public TextNode Current => _path.Count == 0 ? Root : _path.Peek();
        public void OnBeginChanges() { }
        public void OnEndChanges() { }
        public void Down(TextNode node) => _path.Push(node);
        public void Up() => _path.Pop();
        public void InsertTopDown(int index, TextNode instance) => Current.Children.Insert(index, instance);
        // This backend inserts top-down. A bottom-up backend would insert here instead.
        public void InsertBottomUp(int index, TextNode instance) { }
        public void Remove(int index, int count) => Current.Children.RemoveRange(index, count);
        public void Move(int from, int to, int count)
        {
            List<TextNode> moved = Current.Children.GetRange(from, count);
            Current.Children.RemoveRange(from, count);
            Current.Children.InsertRange(to > from ? to - count : to, moved);
        }
        public void Apply(Action<TextNode, object?> block, object? value) => block(Current, value);
        public void Clear() { _path.Clear(); Root.Children.Clear(); }
    }

    private sealed class DemoContext : SynchronizationContext
    {
        private readonly Queue<Action> _queue = new Queue<Action>();
        public override void Post(SendOrPostCallback callback, object? state)
        { lock (_queue) _queue.Enqueue(() => callback(state)); }
        public void Drain()
        {
            while (true)
            {
                Action action;
                lock (_queue) { if (_queue.Count == 0) return; action = _queue.Dequeue(); }
                action();
            }
        }
    }
}
