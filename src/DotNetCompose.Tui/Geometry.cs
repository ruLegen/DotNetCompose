namespace DotNetCompose.Tui;

public readonly record struct TuiSize(int Width, int Height)
{
    public static readonly TuiSize Empty = new(0, 0);
}

public readonly record struct TuiRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public TuiRect Intersect(TuiRect other)
    {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top
            ? new TuiRect(left, top, 0, 0)
            : new TuiRect(left, top, right - left, bottom - top);
    }
}

public readonly record struct TuiConstraints(int MinWidth, int MaxWidth, int MinHeight, int MaxHeight)
{
    public TuiSize Constrain(TuiSize size) => new(
        Math.Clamp(size.Width, MinWidth, MaxWidth),
        Math.Clamp(size.Height, MinHeight, MaxHeight));

    public static TuiConstraints Tight(TuiSize size) =>
        new(size.Width, size.Width, size.Height, size.Height);
}

public enum TuiLengthKind { Auto, Cells, Fill }

public readonly record struct TuiLength(TuiLengthKind Kind, int Value)
{
    public static TuiLength Auto => new(TuiLengthKind.Auto, 0);
    public static TuiLength Cells(int cells) => new(TuiLengthKind.Cells, Math.Max(0, cells));
    public static TuiLength Fill(int weight = 1) => new(TuiLengthKind.Fill, Math.Max(1, weight));
}

public readonly record struct TuiLayout(TuiLength Width, TuiLength Height)
{
    public static readonly TuiLayout Auto = new(TuiLength.Auto, TuiLength.Auto);
    public static TuiLayout Fill => new(TuiLength.Fill(), TuiLength.Fill());
}
