using System.Threading;
using DotNetCompose.Maui.Modifiers;
using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui;

/// <summary>An immutable, ordered chain of MAUI layout and drawing effects.</summary>
public readonly struct Modifier
{
    private readonly ModifierLink? _tail;

    private Modifier(ModifierLink tail)
    {
        _tail = tail;
    }

    public static Modifier Empty
    {
        get
        {
            return default;
        }
    }

    internal IReadOnlyList<ModifierElement> Elements
    {
        get
        {
            return _tail?.Elements ?? Array.Empty<ModifierElement>();
        }
    }

    private Modifier Then(ModifierElement element)
    {
        return new Modifier(new ModifierLink(_tail, element));
    }

    public Modifier Size(double width, double height)
    {
        CheckDimension(width, nameof(width));
        CheckDimension(height, nameof(height));
        return Then(new SizeElement(width, height));
    }

    public Modifier Fill(bool width = true, bool height = true)
    {
        return Then(new FillElement(width, height));
    }

    public Modifier Padding(double all)
    {
        return Padding(all, all, all, all);
    }

    public Modifier Padding(double left, double top, double right, double bottom)
    {
        CheckDimension(left, nameof(left));
        CheckDimension(top, nameof(top));
        CheckDimension(right, nameof(right));
        CheckDimension(bottom, nameof(bottom));
        return Then(new PaddingElement(left, top, right, bottom));
    }

    public Modifier Background(Color color)
    {
        return Then(new BackgroundElement(color ?? throw new ArgumentNullException(nameof(color))));
    }

    public Modifier Border(Color color, double thickness = 1)
    {
        ArgumentNullException.ThrowIfNull(color);
        CheckDimension(thickness, nameof(thickness));
        return Then(new BorderElement(color, thickness));
    }

    public Modifier DrawBehind(Action<MauiDrawScope> draw)
    {
        return Then(new DrawBehindElement(draw ?? throw new ArgumentNullException(nameof(draw))));
    }

    public Modifier DrawForeground(Action<MauiDrawScope> draw)
    {
        return Then(new DrawForegroundElement(draw ?? throw new ArgumentNullException(nameof(draw))));
    }

    /// <summary>Supported by <see cref="MauiUi.Canvas"/>. Native MAUI views cannot draw into its canvas.</summary>
    public Modifier DrawWithContent(Action<MauiDrawScope> draw)
    {
        return Then(new DrawWithContentElement(draw ?? throw new ArgumentNullException(nameof(draw))));
    }

    /// <summary>Makes this point in the modifier chain respond to activation.</summary>
    public Modifier Clickable(Action onClick, bool enabled = true, string? onClickLabel = null,
        ClickableRole? role = null)
    {
        return Then(new ClickableElement(onClick ?? throw new ArgumentNullException(nameof(onClick)),
            enabled, onClickLabel, role));
    }

    private static void CheckDimension(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(name, "Dimensions must be finite and nonnegative.");
        }
    }

    private sealed class ModifierLink
    {
        private ModifierElement[]? _elements;

        internal ModifierLink(ModifierLink? previous, ModifierElement element)
        {
            Previous = previous;
            Element = element;
            Count = (previous?.Count ?? 0) + 1;
        }

        internal ModifierLink? Previous { get; }
        internal ModifierElement Element { get; }
        internal int Count { get; }

        internal IReadOnlyList<ModifierElement> Elements
        {
            get
            {
                ModifierElement[]? cached = Volatile.Read(ref _elements);
                if (cached is not null)
                {
                    return cached;
                }

                ModifierElement[] ordered = new ModifierElement[Count];
                ModifierLink? cursor = this;
                for (int index = Count - 1; index >= 0; index--)
                {
                    ordered[index] = cursor!.Element;
                    cursor = cursor.Previous;
                }

                return Interlocked.CompareExchange(ref _elements, ordered, null) ?? ordered;
            }
        }
    }
}

public enum ClickableRole
{
    Button
}
