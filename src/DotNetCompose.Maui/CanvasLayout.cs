using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace DotNetCompose.Maui;

internal sealed class CanvasLayout : Layout, IDisposable
{
    private readonly ObservedDrawable _drawable;
    private Modifier _modifier = Modifier.Empty;
    private Action<MauiDrawScope> _onDraw = _ => { };

    internal CanvasLayout()
    {
        CanvasView = new GraphicsView { InputTransparent = true };
        Add(CanvasView);
        _drawable = new ObservedDrawable(CanvasView, Draw);
    }

    internal GraphicsView CanvasView { get; }
    internal Modifier Modifier => _modifier;
    internal int InvalidationCount => _drawable.InvalidationCount;

    internal void Update(Modifier modifier, Action<MauiDrawScope> onDraw)
    {
        _modifier = modifier;
        _onDraw = onDraw;
        InvalidateMeasure();
        _drawable.Update(Draw);
    }

    protected override ILayoutManager CreateLayoutManager() => new CanvasLayoutManager(this);

    private void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float width = (float)Math.Max(0, CanvasView.Width);
        float height = (float)Math.Max(0, CanvasView.Height);
        if (width == 0 && height == 0)
        {
            width = dirtyRect.Width;
            height = dirtyRect.Height;
        }
        DrawElement(canvas, 0, new RectF(0, 0, width, height));
    }

    private void DrawElement(ICanvas canvas, int index, RectF bounds)
    {
        if (index == _modifier.Elements.Count)
        {
            _onDraw(new MauiDrawScope(canvas, bounds));
            return;
        }

        ModifierElement element = _modifier.Elements[index];
        canvas.SaveState();
        try
        {
            switch (element)
            {
                case PaddingElement padding:
                    canvas.Translate((float)padding.Left, (float)padding.Top);
                    DrawElement(canvas, index + 1, new RectF(0, 0,
                        Math.Max(0, bounds.Width - (float)(padding.Left + padding.Right)),
                        Math.Max(0, bounds.Height - (float)(padding.Top + padding.Bottom))));
                    break;
                case SizeElement size:
                    DrawElement(canvas, index + 1, new RectF(0, 0,
                        Math.Min(bounds.Width, (float)size.Width),
                        Math.Min(bounds.Height, (float)size.Height)));
                    break;
                case FillElement:
                    DrawElement(canvas, index + 1, bounds);
                    break;
                case BackgroundElement background:
                    canvas.FillColor = background.Color;
                    canvas.FillRectangle(bounds);
                    DrawElement(canvas, index + 1, bounds);
                    break;
                case BorderElement border:
                    DrawElement(canvas, index + 1, bounds);
                    canvas.StrokeColor = border.Color;
                    canvas.StrokeSize = (float)border.Thickness;
                    float inset = (float)border.Thickness / 2;
                    canvas.DrawRectangle(inset, inset,
                        Math.Max(0, bounds.Width - 2 * inset),
                        Math.Max(0, bounds.Height - 2 * inset));
                    break;
                case DrawBehindElement behind:
                    behind.Draw(new MauiDrawScope(canvas, bounds));
                    DrawElement(canvas, index + 1, bounds);
                    break;
                case DrawForegroundElement foreground:
                    DrawElement(canvas, index + 1, bounds);
                    foreground.Draw(new MauiDrawScope(canvas, bounds));
                    break;
                case DrawWithContentElement withContent:
                    withContent.Draw(new MauiDrawScope(canvas, bounds,
                        () => DrawElement(canvas, index + 1, bounds)));
                    break;
                default:
                    throw new NotSupportedException($"Unknown modifier {element.GetType().Name}.");
            }
        }
        finally
        {
            canvas.RestoreState();
        }
    }

    public void Dispose() => _drawable.Dispose();
}

internal sealed class CanvasLayoutManager(CanvasLayout layout) : LayoutManager(layout)
{
    public override Size Measure(double widthConstraint, double heightConstraint)
    {
        double width = 0;
        double height = 0;
        IReadOnlyList<ModifierElement> elements = layout.Modifier.Elements;
        for (int index = elements.Count - 1; index >= 0; index--)
        {
            switch (elements[index])
            {
                case SizeElement size:
                    width = size.Width;
                    height = size.Height;
                    break;
                case FillElement fill:
                    if (fill.Width && double.IsFinite(widthConstraint)) width = widthConstraint;
                    if (fill.Height && double.IsFinite(heightConstraint)) height = heightConstraint;
                    break;
                case PaddingElement padding:
                    width += padding.Left + padding.Right;
                    height += padding.Top + padding.Bottom;
                    break;
            }
        }
        return new Size(Math.Min(width, widthConstraint), Math.Min(height, heightConstraint));
    }

    public override Size ArrangeChildren(Rect bounds)
    {
        Size desired = Measure(bounds.Width, bounds.Height);
        layout.CanvasView.Arrange(new Rect(bounds.X, bounds.Y,
            Math.Min(bounds.Width, desired.Width), Math.Min(bounds.Height, desired.Height)));
        return bounds.Size;
    }
}
