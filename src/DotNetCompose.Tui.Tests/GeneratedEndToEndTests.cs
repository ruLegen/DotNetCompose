using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Tui.Tests;

public sealed partial class GeneratedEndToEndTests
{
    private static readonly SnapshotMutableState<string> Text = Composables.CreateMutableState(string.Empty);

    [Composable]
    internal static void Content()
    {
        Tui.Column(gap: 1, layout: TuiLayout.Fill, content: () =>
        {
            Tui.Text("Editor");
            Tui.TextField(Text.Value, value => Text.Value = value, "type here");
        });
    }

    [Fact]
    public void GeneratedContentRunsThroughFakeTerminalAndRestoresIt()
    {
        Text.Value = string.Empty;
        FakeTerminalDriver terminal = new(30, 5);
        terminal.Enqueue(new TuiKeyEvent(ConsoleKey.X, 'x', 0));
        terminal.Enqueue(new TuiKeyEvent(ConsoleKey.Escape, '\u001b', 0));

        TuiApplication.Run(Builders.Content, terminal);

        Assert.Equal("x", Text.Value);
        Assert.True(terminal.Exited);
        Assert.Contains("Editor", terminal.Output);
    }
}
