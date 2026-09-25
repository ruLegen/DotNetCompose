using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Tui;

public sealed class TuiApplier : IApplier<TuiNode>
{
    private readonly Stack<TuiNode> _stack = new();
    private readonly Action? _onEndChanges;

    public TuiApplier(Action? onEndChanges = null)
    {
        Root = new TuiRootNode();
        _stack.Push(Root);
        _onEndChanges = onEndChanges;
    }

    public TuiNode Root { get; }
    public TuiNode Current => _stack.Peek();

    public void OnBeginChanges() { }
    public void OnEndChanges() => _onEndChanges?.Invoke();
    public void Down(TuiNode node) => _stack.Push(node);

    public void Up()
    {
        if (_stack.Count == 1) throw new InvalidOperationException("Cannot move above the TUI root.");
        _stack.Pop();
    }

    public void InsertTopDown(int index, TuiNode instance) => Current.Insert(index, instance);
    public void InsertBottomUp(int index, TuiNode instance) { }
    public void Remove(int index, int count) => Current.Remove(index, count);
    public void Move(int from, int to, int count) => Current.Move(from, to, count);
    public void Clear() => Current.ClearChildren();
    public void Apply(Action<TuiNode, object?> block, object? value) => block(Current, value);
}
