using DotNetCompose.Runtime.Composer;
using Microsoft.Maui.Controls;

namespace DotNetCompose.Maui;

/// <summary>Applies composition changes to a MAUI layout tree.</summary>
public sealed class MauiApplier : IApplier<MauiNode>
{
    private readonly Stack<MauiNode> _stack = new();

    public MauiApplier(Grid root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = new MauiNode(root, acceptsChildren: true, root: true);
        _stack.Push(Root);
    }

    public MauiNode Root { get; }
    public MauiNode Current
    {
        get
        {
            return _stack.Peek();
        }
    }

    public void OnBeginChanges()
    {
    }

    public void OnEndChanges()
    {
    }

    public void Down(MauiNode node)
    {
        _stack.Push(node);
    }

    public void Up()
    {
        if (_stack.Count == 1)
        {
            throw new InvalidOperationException("Cannot move above the MAUI root.");
        }
        _stack.Pop();
    }

    public void InsertTopDown(int index, MauiNode instance)
    {
    }

    public void InsertBottomUp(int index, MauiNode instance)
    {
        Current.Insert(index, instance);
    }

    public void Remove(int index, int count)
    {
        Current.Remove(index, count);
    }

    public void Move(int from, int to, int count)
    {
        Current.Move(from, to, count);
    }

    public void Clear()
    {
        Root.ClearChildren();
    }

    public void Apply(Action<MauiNode, object?> block, object? value)
    {
        block(Current, value);
    }
}
