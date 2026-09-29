using DotNetCompose.Maui.Drawing;
using DotNetCompose.Maui.Modifiers;
using Microsoft.Maui.Controls;

namespace DotNetCompose.Maui;

/// <summary>A stable composition node whose visual child is a MAUI view.</summary>
public sealed class MauiNode
{
    private readonly List<MauiNode> _children = new();
    private readonly List<IModifierView> _wrappers = new();
    private Action<View>? _onRelease;
    private bool _released;

    internal MauiNode(View core, bool acceptsChildren = false, bool root = false)
    {
        Core = core;
        AcceptsChildren = acceptsChildren;
        View = root ? core : new ContentView { Content = core };
    }

    public View Core { get; }
    public View View { get; }
    public IReadOnlyList<MauiNode> Children
    {
        get
        {
            return _children;
        }
    }
    internal bool AcceptsChildren { get; }

    internal void UpdateNative<TView>(Action<TView>? update, Modifier modifier, Action<TView>? onRelease)
        where TView : View
    {
        if (Core is not TView view)
        {
            throw new InvalidOperationException("The native view type changed at an existing composition node.");
        }
        ApplyModifier(modifier);
        _onRelease = onRelease == null ? null : item => onRelease((TView)item);
        update?.Invoke(view);
    }

    internal void UpdateCanvas(Modifier modifier, Action<MauiDrawScope> onDraw)
    {
        if (Core is not CanvasLayout canvas)
        {
            throw new InvalidOperationException("The canvas node type changed at an existing composition node.");
        }
        ApplyLayoutOptions(modifier);
        canvas.Update(modifier, onDraw);
    }

    internal void ApplyModifier(Modifier modifier)
    {
        if (Core is CanvasLayout)
        {
            throw new InvalidOperationException("Canvas modifiers are applied by the canvas renderer.");
        }
        if (modifier.Elements.Any(element => element is DrawWithContentElement))
        {
            throw new NotSupportedException(
                "DrawWithContent requires MauiUi.Canvas. Native MAUI views cannot draw into an ICanvas.");
        }

        ApplyLayoutOptions(modifier);

        if (_wrappers.Count == modifier.Elements.Count &&
            _wrappers.Select((wrapper, index) => WrapperMatches(wrapper, modifier.Elements[index])).All(match => match))
        {
            for (int index = 0; index < _wrappers.Count; index++)
            {
                _wrappers[index].Update(modifier.Elements[index]);
            }
            return;
        }

        ContentView host = (ContentView)View;
        host.Content = null;
        foreach (IModifierView wrapper in _wrappers)
        {
            wrapper.DetachChild();
            wrapper.Dispose();
        }
        _wrappers.Clear();

        View current = Core;
        for (int index = modifier.Elements.Count - 1; index >= 0; index--)
        {
            ModifierElement element = modifier.Elements[index];
            IModifierView wrapper = element switch
            {
                SizeElement or FillElement or PaddingElement => new MeasureModifierView(current, element),
                BackgroundElement or BorderElement or DrawBehindElement or DrawForegroundElement =>
                    new DrawModifierView(current, element),
                ClickableElement clickable => new ClickableModifierView(current, clickable),
                _ => throw new NotSupportedException($"Unknown modifier {element.GetType().Name}.")
            };
            _wrappers.Insert(0, wrapper);
            current = wrapper.View;
        }
        host.Content = current;
    }

    private static bool WrapperMatches(IModifierView wrapper, ModifierElement element)
    {
        return (wrapper is MeasureModifierView && element is SizeElement or FillElement or PaddingElement) ||
            (wrapper is DrawModifierView draw && draw.Element.GetType() == element.GetType()) ||
            (wrapper is ClickableModifierView && element is ClickableElement);
    }

    private void ApplyLayoutOptions(Modifier modifier)
    {
        if (View is not ContentView host)
        {
            return;
        }

        bool? fillWidth = null;
        bool? fillHeight = null;
        foreach (ModifierElement element in modifier.Elements)
        {
            switch (element)
            {
                case SizeElement:
                    fillWidth ??= false;
                    fillHeight ??= false;
                    break;
                case FillElement fill:
                    if (fill.Width)
                    {
                        fillWidth ??= true;
                    }
                    if (fill.Height)
                    {
                        fillHeight ??= true;
                    }
                    break;
            }
        }
        host.HorizontalOptions = fillWidth == false ? LayoutOptions.Start : LayoutOptions.Fill;
        host.VerticalOptions = fillHeight == false ? LayoutOptions.Start : LayoutOptions.Fill;
    }

    internal void Insert(int index, MauiNode child)
    {
        if (Core is ScrollView scroll)
        {
            if (index != 0 || _children.Count != 0)
            {
                throw new InvalidOperationException("Scroll accepts exactly one composable child.");
            }
            _children.Add(child);
            scroll.Content = child.View;
            return;
        }
        Microsoft.Maui.Controls.Layout layout = ChildLayout();
        _children.Insert(index, child);
        layout.Children.Insert(index, child.View);
    }

    internal void Remove(int index, int count)
    {
        if (Core is ScrollView scroll)
        {
            if (index != 0 || count != 1 || _children.Count != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            MauiNode removed = _children[0];
            _children.Clear();
            scroll.Content = null;
            removed.Release();
            return;
        }
        Microsoft.Maui.Controls.Layout layout = ChildLayout();
        for (int offset = 0; offset < count; offset++)
        {
            MauiNode child = _children[index];
            _children.RemoveAt(index);
            layout.Children.RemoveAt(index);
            child.Release();
        }
    }

    internal void Move(int from, int to, int count)
    {
        if (Core is ScrollView)
        {
            if (from == 0 && count == 1 && (to == 0 || to == 1))
            {
                return;
            }
            throw new InvalidOperationException("Scroll contains one child and cannot reorder it.");
        }
        Microsoft.Maui.Controls.Layout layout = ChildLayout();
        List<MauiNode> moved = _children.GetRange(from, count);
        _children.RemoveRange(from, count);
        for (int index = 0; index < count; index++)
        {
            layout.Children.RemoveAt(from);
        }
        int destination = to > from ? to - count : to;
        _children.InsertRange(destination, moved);
        for (int index = 0; index < count; index++)
        {
            layout.Children.Insert(destination + index, moved[index].View);
        }
    }

    internal void ClearChildren()
    {
        if (_children.Count > 0)
        {
            Remove(0, _children.Count);
        }
    }

    private Microsoft.Maui.Controls.Layout ChildLayout()
    {
        if (!AcceptsChildren || Core is not Microsoft.Maui.Controls.Layout layout)
        {
            throw new InvalidOperationException("This MAUI node does not accept composable children.");
        }
        return layout;
    }

    internal void Release()
    {
        if (_released)
        {
            return;
        }
        _released = true;
        ClearChildren();
        _onRelease?.Invoke(Core);
        foreach (IModifierView wrapper in _wrappers)
        {
            wrapper.Dispose();
        }
        _wrappers.Clear();
        if (Core is CanvasLayout canvas)
        {
            canvas.Dispose();
        }
        if (Core is ScrollView scroll)
        {
            MauiUi.ReleaseScroll(scroll);
        }
    }
}
