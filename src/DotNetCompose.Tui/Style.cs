namespace DotNetCompose.Tui;

public enum TuiColor
{
    Default = -1,
    Black = 0,
    Red = 1,
    Green = 2,
    Yellow = 3,
    Blue = 4,
    Magenta = 5,
    Cyan = 6,
    White = 7,
    BrightBlack = 8,
    BrightRed = 9,
    BrightGreen = 10,
    BrightYellow = 11,
    BrightBlue = 12,
    BrightMagenta = 13,
    BrightCyan = 14,
    BrightWhite = 15
}

[Flags]
public enum TuiAttributes { None = 0, Bold = 1, Underline = 2, Reverse = 4 }

public readonly record struct TuiStyle(
    TuiColor Foreground = TuiColor.Default,
    TuiColor Background = TuiColor.Default,
    TuiAttributes Attributes = TuiAttributes.None);

public sealed record TuiTheme(
    TuiStyle Text,
    TuiStyle Border,
    TuiStyle Focused,
    TuiStyle Disabled,
    TuiStyle Selection)
{
    public static TuiTheme Default { get; } = new(
        new TuiStyle(TuiColor.White),
        new TuiStyle(TuiColor.BrightBlack),
        new TuiStyle(TuiColor.Black, TuiColor.Cyan, TuiAttributes.Bold),
        new TuiStyle(TuiColor.BrightBlack),
        new TuiStyle(TuiColor.Black, TuiColor.BrightWhite));
}
