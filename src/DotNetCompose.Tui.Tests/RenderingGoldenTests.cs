namespace DotNetCompose.Tui.Tests;

public sealed class RenderingGoldenTests
{
    [Fact]
    public void BorderRowStylesAndResizeProduceDeterministicBuffers()
    {
        TuiRootNode root = new();
        BorderNode border = new() { Title = "Demo", Layout = TuiLayout.Fill };
        StackNode row = new(StackOrientation.Horizontal) { Gap = 1, Layout = TuiLayout.Fill };
        TextNode left = new() { Text = "界", Style = new TuiStyle(TuiColor.Yellow) };
        TextNode right = new() { Text = "value", Layout = new TuiLayout(TuiLength.Fill(), TuiLength.Auto) };
        row.Insert(0, left); row.Insert(1, right); border.Insert(0, row); root.Insert(0, border);
        FakeTerminalDriver terminal = new(14, 4);
        TuiRenderer renderer = new(terminal);
        FocusManager focus = new();

        renderer.Render(root, terminal.Size, focus, TuiTheme.Default);

        Assert.Equal("┌─ Demo ─────┐\r\n│界 value    │\r\n│            │\r\n└────────────┘",
            renderer.LastBuffer!.ToPlainText());
        Assert.Equal(TuiColor.Yellow, renderer.LastBuffer[1, 1].Style.Foreground);
        Assert.True(renderer.LastBuffer[2, 1].Continuation);

        terminal.Size = new TuiSize(8, 3);
        renderer.Render(root, terminal.Size, focus, TuiTheme.Default);
        Assert.Equal("┌─ Demo\r\n│界 val│\r\n└──────┘", renderer.LastBuffer!.ToPlainText());
        Assert.DoesNotContain("\u001b[2J", terminal.Output);
    }
}
