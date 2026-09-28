using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace DotNetCompose.Maui;

internal interface IModifierView : IDisposable
{
    View View { get; }
    void Update(ModifierElement element);
    void DetachChild();
}

internal sealed class MeasureModifierView : Layout, IModifierView
{
    private ModifierElement _element;

    internal MeasureModifierView(View child, ModifierElement element)
    {
        _element = element;
        Add(child);
    }

    View IModifierView.View => this;
    internal View Child => (View)Children[0];
    internal ModifierElement Element => _element;

    public void Update(ModifierElement element)
    {
        _element = element;
        InvalidateMeasure();
    }

    protected override ILayoutManager CreateLayoutManager() => new MeasureModifierLayoutManager(this);

    public void DetachChild() => RemoveAt(0);
    public void Dispose() { }
}

internal sealed class MeasureModifierLayoutManager(MeasureModifierView layout) : LayoutManager(layout)
{
    public override Size Measure(double widthConstraint, double heightConstraint)
    {
        if (layout.Children.Count == 0)
            return Size.Zero;

        ModifierElement element = layout.Element;
        double childWidth = widthConstraint;
        double childHeight = heightConstraint;
        if (element is PaddingElement padding)
        {
            childWidth = Math.Max(0, widthConstraint - padding.Left - padding.Right);
            childHeight = Math.Max(0, heightConstraint - padding.Top - padding.Bottom);
        }
        else if (element is SizeElement size)
        {
            childWidth = Math.Min(widthConstraint, size.Width);
            childHeight = Math.Min(heightConstraint, size.Height);
        }

        Size desired = layout.Child.Measure(childWidth, childHeight);
        double width = desired.Width;
        double height = desired.Height;
        switch (element)
        {
            case PaddingElement outerPadding:
                width += outerPadding.Left + outerPadding.Right;
                height += outerPadding.Top + outerPadding.Bottom;
                break;
            case SizeElement size:
                width = size.Width;
                height = size.Height;
                break;
            case FillElement fill:
                if (fill.Width && double.IsFinite(widthConstraint)) width = widthConstraint;
                if (fill.Height && double.IsFinite(heightConstraint)) height = heightConstraint;
                break;
        }
        return new Size(Math.Min(width, widthConstraint), Math.Min(height, heightConstraint));
    }

    public override Size ArrangeChildren(Rect bounds)
    {
        if (layout.Children.Count == 0)
            return bounds.Size;

        Rect childBounds = bounds;
        if (layout.Element is PaddingElement padding)
            childBounds = new Rect(bounds.X + padding.Left, bounds.Y + padding.Top,
                Math.Max(0, bounds.Width - padding.Left - padding.Right),
                Math.Max(0, bounds.Height - padding.Top - padding.Bottom));
        else if (layout.Element is SizeElement size)
            childBounds = new Rect(bounds.X, bounds.Y,
                Math.Min(bounds.Width, size.Width), Math.Min(bounds.Height, size.Height));
        layout.Child.Arrange(childBounds);
        return bounds.Size;
    }
}

internal sealed class DrawModifierView : Layout, IModifierView
{
    private readonly GraphicsView _graphics;
    private readonly ObservedDrawable _drawable;
    private ModifierElement _element;

    internal DrawModifierView(View child, ModifierElement element)
    {
        _element = element;
        _graphics = new GraphicsView { InputTransparent = true };
        bool foreground = IsForeground(element);
        if (foreground)
        {
            Add(child);
            Add(_graphics);
        }
        else
        {
            Add(_graphics);
            Add(child);
        }
        _drawable = new ObservedDrawable(_graphics, Draw);
    }

    View IModifierView.View => this;
    internal View Child => (View)Children[IsForeground(_element) ? 0 : 1];
    internal ModifierElement Element => _element;

    public void Update(ModifierElement element)
    {
        _element = element;
        _drawable.Update(Draw);
    }

    private void Draw(ICanvas canvas, RectF dirtyRect)
    {
        RectF bounds = new(0, 0, (float)Math.Max(0, Width), (float)Math.Max(0, Height));
        if (bounds.Width == 0 && bounds.Height == 0)
            bounds = dirtyRect;
        canvas.SaveState();
        try
        {
            switch (_element)
            {
                case BackgroundElement background:
                    canvas.FillColor = background.Color;
                    canvas.FillRectangle(bounds);
                    break;
                case BorderElement border:
                    canvas.StrokeColor = border.Color;
                    canvas.StrokeSize = (float)border.Thickness;
                    float inset = (float)border.Thickness / 2;
                    canvas.DrawRectangle(inset, inset,
                        Math.Max(0, bounds.Width - 2 * inset),
                        Math.Max(0, bounds.Height - 2 * inset));
                    break;
                case DrawBehindElement behind:
                    behind.Draw(new MauiDrawScope(canvas, bounds));
                    break;
                case DrawForegroundElement foreground:
                    foreground.Draw(new MauiDrawScope(canvas, bounds));
                    break;
            }
        }
        finally
        {
            canvas.RestoreState();
        }
    }

    private static bool IsForeground(ModifierElement element) =>
        element is BorderElement or DrawForegroundElement;

    protected override ILayoutManager CreateLayoutManager() => new DrawModifierLayoutManager(this);

    public void DetachChild() => Remove(Child);
    public void Dispose() => _drawable.Dispose();
}

internal sealed class DrawModifierLayoutManager(DrawModifierView layout) : LayoutManager(layout)
{
    public override Size Measure(double widthConstraint, double heightConstraint) =>
        layout.Children.Count < 2 ? Size.Zero : layout.Child.Measure(widthConstraint, heightConstraint);

    public override Size ArrangeChildren(Rect bounds)
    {
        if (layout.Children.Count == 2)
        {
            foreach (IView child in layout.Children)
                child.Arrange(bounds);
        }
        return bounds.Size;
    }
}
