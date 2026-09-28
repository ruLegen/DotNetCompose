using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui;

/// <summary>An immutable, ordered chain of MAUI layout and drawing effects.</summary>
public sealed class Modifier
{
    private readonly ModifierElement[] _elements;

    private Modifier(ModifierElement[] elements) => _elements = elements;

    public static Modifier Empty { get; } = new([]);

    internal IReadOnlyList<ModifierElement> Elements => _elements;

    private Modifier Then(ModifierElement element)
    {
        ModifierElement[] next = new ModifierElement[_elements.Length + 1];
        Array.Copy(_elements, next, _elements.Length);
        next[^1] = element;
        return new Modifier(next);
    }

    public Modifier Size(double width, double height)
    {
        CheckDimension(width, nameof(width));
        CheckDimension(height, nameof(height));
        return Then(new SizeElement(width, height));
    }

    public Modifier Fill(bool width = true, bool height = true) => Then(new FillElement(width, height));

    public Modifier Padding(double all) => Padding(all, all, all, all);

    public Modifier Padding(double left, double top, double right, double bottom)
    {
        CheckDimension(left, nameof(left));
        CheckDimension(top, nameof(top));
        CheckDimension(right, nameof(right));
        CheckDimension(bottom, nameof(bottom));
        return Then(new PaddingElement(left, top, right, bottom));
    }

    public Modifier Background(Color color) => Then(new BackgroundElement(color ?? throw new ArgumentNullException(nameof(color))));

    public Modifier Border(Color color, double thickness = 1)
    {
        ArgumentNullException.ThrowIfNull(color);
        CheckDimension(thickness, nameof(thickness));
        return Then(new BorderElement(color, thickness));
    }

    public Modifier DrawBehind(Action<MauiDrawScope> draw) =>
        Then(new DrawBehindElement(draw ?? throw new ArgumentNullException(nameof(draw))));

    public Modifier DrawForeground(Action<MauiDrawScope> draw) =>
        Then(new DrawForegroundElement(draw ?? throw new ArgumentNullException(nameof(draw))));

    /// <summary>Supported by <see cref="MauiUi.Canvas"/>. Native MAUI views cannot draw into its canvas.</summary>
    public Modifier DrawWithContent(Action<MauiDrawScope> draw) =>
        Then(new DrawWithContentElement(draw ?? throw new ArgumentNullException(nameof(draw))));

    private static void CheckDimension(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, "Dimensions must be finite and nonnegative.");
    }
}

internal abstract record ModifierElement;
internal sealed record SizeElement(double Width, double Height) : ModifierElement;
internal sealed record FillElement(bool Width, bool Height) : ModifierElement;
internal sealed record PaddingElement(double Left, double Top, double Right, double Bottom) : ModifierElement;
internal sealed record BackgroundElement(Color Color) : ModifierElement;
internal sealed record BorderElement(Color Color, double Thickness) : ModifierElement;
internal sealed record DrawBehindElement(Action<MauiDrawScope> Draw) : ModifierElement;
internal sealed record DrawForegroundElement(Action<MauiDrawScope> Draw) : ModifierElement;
internal sealed record DrawWithContentElement(Action<MauiDrawScope> Draw) : ModifierElement;
