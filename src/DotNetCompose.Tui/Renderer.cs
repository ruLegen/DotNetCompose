using System.Text;

namespace DotNetCompose.Tui;

internal static class CellBufferDiff
{
    internal static string Build(CellBuffer? previous, CellBuffer current)
    {
        StringBuilder output = new();
        TuiStyle? activeStyle = null;
        for (int y = 0; y < current.Height; y++)
        {
            int x = 0;
            while (x < current.Width)
            {
                if (!Changed(previous, current, x, y)) { x++; continue; }
                output.Append("\u001b[").Append(y + 1).Append(';').Append(x + 1).Append('H');

                while (x < current.Width && Changed(previous, current, x, y))
                {
                    TuiCell cell = current[x, y];
                    if (cell.Continuation) { x++; continue; }
                    if (activeStyle != cell.Style)
                    {
                        AppendStyle(output, cell.Style);
                        activeStyle = cell.Style;
                    }
                    output.Append(cell.Grapheme);
                    x += Math.Max(1, cell.Width);
                }
            }
        }
        if (activeStyle != null) output.Append("\u001b[0m");
        return output.ToString();
    }

    private static bool Changed(CellBuffer? previous, CellBuffer current, int x, int y)
    {
        if (previous == null || previous.Width != current.Width || previous.Height != current.Height) return true;
        return previous[x, y] != current[x, y];
    }

    private static void AppendStyle(StringBuilder output, TuiStyle style)
    {
        output.Append("\u001b[0");
        if ((style.Attributes & TuiAttributes.Bold) != 0) output.Append(";1");
        if ((style.Attributes & TuiAttributes.Underline) != 0) output.Append(";4");
        if ((style.Attributes & TuiAttributes.Reverse) != 0) output.Append(";7");
        AppendColor(output, style.Foreground, foreground: true);
        AppendColor(output, style.Background, foreground: false);
        output.Append('m');
    }

    private static void AppendColor(StringBuilder output, TuiColor color, bool foreground)
    {
        int value = (int)color;
        if (value < 0) { output.Append(foreground ? ";39" : ";49"); return; }
        int code = value < 8
            ? (foreground ? 30 : 40) + value
            : (foreground ? 90 : 100) + value - 8;
        output.Append(';').Append(code);
    }
}

internal sealed class TuiRenderer
{
    private readonly ITerminalDriver _terminal;
    private CellBuffer? _previous;

    internal TuiRenderer(ITerminalDriver terminal) => _terminal = terminal;
    internal CellBuffer? LastBuffer => _previous;

    internal void Render(TuiNode root, TuiSize size, FocusManager focus, TuiTheme theme)
    {
        CellBuffer next = new(size.Width, size.Height);
        TuiRect viewport = new(0, 0, size.Width, size.Height);
        root.Measure(TuiConstraints.Tight(size));
        root.Arrange(viewport);
        focus.Rebuild(root);
        root.Render(next, viewport, focus, theme);
        string update = CellBufferDiff.Build(_previous, next);
        if (update.Length != 0) _terminal.Write(update);
        _previous = next;
    }

    internal void Reset() => _previous = null;
}
