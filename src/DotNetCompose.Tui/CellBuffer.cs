using System.Globalization;
using System.Text;

namespace DotNetCompose.Tui;

public readonly record struct TuiCell(string Grapheme, int Width, TuiStyle Style, bool Continuation)
{
    public static readonly TuiCell Empty = new(" ", 1, default, false);
}

public sealed class CellBuffer
{
    private readonly TuiCell[] _cells;

    public CellBuffer(int width, int height)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        _cells = new TuiCell[width * height];
        Clear();
    }

    public int Width { get; }
    public int Height { get; }
    public TuiCell this[int x, int y] => _cells[y * Width + x];

    public void Clear(TuiStyle style = default)
    {
        Array.Fill(_cells, new TuiCell(" ", 1, style, false));
    }

    public void Set(int x, int y, string grapheme, int width, TuiStyle style)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || width <= 0) return;
        _cells[y * Width + x] = new TuiCell(grapheme, width, style, false);
        for (int offset = 1; offset < width && x + offset < Width; offset++)
            _cells[y * Width + x + offset] = new TuiCell(string.Empty, 0, style, true);
    }

    public void Write(int x, int y, string text, TuiStyle style, TuiRect clip)
    {
        int cursor = x;
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text ?? string.Empty);
        while (elements.MoveNext())
        {
            string grapheme = elements.GetTextElement();
            int width = UnicodeWidth.GetWidth(grapheme);
            if (width == 0)
            {
                if (cursor > x && cursor - 1 >= clip.X && cursor - 1 < clip.Right)
                {
                    TuiCell previous = this[cursor - 1, y];
                    Set(cursor - 1, y, previous.Grapheme + grapheme, previous.Width, previous.Style);
                }
                continue;
            }
            if (cursor >= clip.Right) break;
            if (cursor >= clip.X && y >= clip.Y && y < clip.Bottom && cursor + width <= clip.Right)
                Set(cursor, y, grapheme, width, style);
            cursor += width;
        }
    }

    public string ToPlainText()
    {
        StringBuilder result = new();
        for (int y = 0; y < Height; y++)
        {
            int last = Width - 1;
            while (last >= 0 && (this[last, y].Continuation || this[last, y].Grapheme == " ")) last--;
            for (int x = 0; x <= last; x++)
                if (!this[x, y].Continuation) result.Append(this[x, y].Grapheme);
            if (y + 1 < Height) result.AppendLine();
        }
        return result.ToString();
    }
}

internal static class UnicodeWidth
{
    internal static int Measure(string text)
    {
        int width = 0;
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text ?? string.Empty);
        while (elements.MoveNext()) width += GetWidth(elements.GetTextElement());
        return width;
    }

    internal static int GetWidth(string grapheme)
    {
        if (string.IsNullOrEmpty(grapheme)) return 0;
        Rune rune = Rune.GetRuneAt(grapheme, 0);
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            return 0;
        int value = rune.Value;
        return IsWide(value) ? 2 : 1;
    }

    private static bool IsWide(int value) =>
        value is >= 0x1100 and <= 0x115F or
        >= 0x2329 and <= 0x232A or
        >= 0x2E80 and <= 0xA4CF or
        >= 0xAC00 and <= 0xD7A3 or
        >= 0xF900 and <= 0xFAFF or
        >= 0xFE10 and <= 0xFE19 or
        >= 0xFE30 and <= 0xFE6F or
        >= 0xFF00 and <= 0xFF60 or
        >= 0xFFE0 and <= 0xFFE6 or
        >= 0x1F300 and <= 0x1FAFF or
        >= 0x20000 and <= 0x3FFFD;
}
