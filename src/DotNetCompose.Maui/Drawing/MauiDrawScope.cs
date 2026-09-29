using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui;

/// <summary>The canvas and local bounds for one drawing stage.</summary>
public sealed class MauiDrawScope
{
    private readonly Action? _drawContent;

    internal MauiDrawScope(ICanvas canvas, RectF bounds, Action? drawContent = null)
    {
        Canvas = canvas;
        Bounds = bounds;
        _drawContent = drawContent;
    }

    public ICanvas Canvas { get; }
    public RectF Bounds { get; }

    /// <summary>Draws the wrapped content. A DrawWithContent callback controls when and how often this runs.</summary>
    public void DrawContent()
    {
        (_drawContent ?? throw new InvalidOperationException(
            "DrawContent is available only inside DrawWithContent."))();
    }
}
