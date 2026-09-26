using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Tui.Tests;

[Collection(TuiCompositionCollection.Name)]
public sealed partial class GeneratedEndToEndTests
{
    private static readonly SnapshotMutableState<string> Text = Composables.CreateMutableState(string.Empty);
    private static readonly SnapshotMutableState<string> Label = Composables.CreateMutableState("first");
    private static readonly SnapshotMutableState<int> CallbackValue = Composables.CreateMutableState(1);
    private static readonly SnapshotMutableState<TuiTheme> ThemeValue =
        Composables.CreateMutableState(TuiTheme.Default);
    private static int _clickedValue;

    [Composable]
    internal static void Content()
    {
        Tui.Column(gap: 1, layout: TuiLayout.Fill, content: () =>
        {
            Tui.Text("Editor");
            Tui.TextField(Text.Value, value => Text.Value = value, "type here");
        });
    }

    [Composable]
    internal static void RecomposeContent()
    {
        int callbackValue = CallbackValue.Value;
        Tui.Theme(
            ThemeValue.Value,
            () => Tui.Button(Label.Value, () => _clickedValue = callbackValue));
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

    [Fact]
    public void GeneratedThemeContentAndCallbackUpdateDuringRecomposition()
    {
        Label.Value = "first";
        CallbackValue.Value = 1;
        ThemeValue.Value = TuiTheme.Default;
        _clickedValue = 0;
        TuiApplier applier = new();
        using Composition<TuiNode> composition = new(applier);
        composition.SetContent(Builders.RecomposeContent);

        ButtonNode first = Assert.IsType<ButtonNode>(Assert.Single(applier.Root.Children));
        Assert.Equal("first", first.Label);
        Assert.Same(TuiTheme.Default, first.Theme);

        TuiTheme updatedTheme = TuiTheme.Default with
        {
            Text = new TuiStyle(TuiColor.BrightGreen),
        };
        Label.Value = "second";
        CallbackValue.Value = 2;
        ThemeValue.Value = updatedTheme;

        Assert.True(composition.Recompose());
        composition.ApplyChanges();

        ButtonNode second = Assert.IsType<ButtonNode>(Assert.Single(applier.Root.Children));
        Assert.Same(first, second);
        Assert.Equal("second", second.Label);
        Assert.Same(updatedTheme, second.Theme);
        second.OnClick!();
        Assert.Equal(2, _clickedValue);
    }
}
