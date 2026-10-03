using DotNetCompose.Maui.Sample.Screens.App;
using DotNetCompose.Maui.Sample.Screens.EditorGraph;
using DotNetCompose.Maui.Sample.Screens.EditorGraph.Editor;
using DotNetCompose.Maui.Sample.Screens.EditorGraph.Preview;
using DotNetCompose.Maui.Sample.Screens.Home;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using Microsoft.Maui.Graphics;
using System.Diagnostics;
using Ui = DotNetCompose.Maui.MauiUi;

namespace DotNetCompose.Maui.Sample.Screens;

internal static partial class AppScreen
{
    [Composable(ComposableMode.NonSkippable)]
    public static void Content(SampleAppState state)
    {
        MauiTheme.Provide(mode: state.Theme, content: () =>
        {
            Ui.Surface(
                modifier: Modifier.Empty.Fill(),
                content: () => Ui.NavHost(state.Navigator));
        });
    }
}

internal static partial class HomeScreen
{
    [Composable]
    public static void Content(HomeViewModel viewModel)
    {
        SnapshotMutableState<bool> isAsynInProgress = Composables.RememberState(false);
        SnapshotMutableState<int> seconds = Composables.RememberState(0);

        Composables.LaunchedEffect(viewModel, async (t) =>
        {
            await viewModel.Init(t);
            while (!t.IsCancellationRequested)
            {
                await Task.Delay(1000,t);
                seconds.Value++;
            }
        });

        Composables.DisposableEffect(viewModel, () =>
        {
            Debug.WriteLine($"========== Disposed");
        });


        bool isInited = viewModel.IsInited;
        Ui.Column(
            modifier: Modifier.Empty.Padding(24),
            spacing: 12,
            content: () =>
            {
                Ui.Text("tSasd ls j;laskjde", fontSize: 24);
                Ui.Text(string.Format("Ellapsed: {0} sec", seconds.Value), fontSize: 24);
                Ui.NativeView(
                    factory: () => new Label(),
                    update: label => label.Text = "Native MAUI Label");

                if (!isInited)
                {
                    Ui.Text("Please wait, Initializing...", fontSize: 24);
                }
                Ui.Button(viewModel.OpenEditor, enabled: isInited, content: () => Ui.Text(viewModel.ButtonText));
                Ui.Button(viewModel.ChangeButtonText, enabled: isInited, content: () => Ui.Text("Change"));

                Ui.Button(async () => 
                {
                    isAsynInProgress.Value = true;
                    await Task.Delay(1000);
                    isAsynInProgress.Value = false;

                }, enabled: !isAsynInProgress, content: () => Ui.Text("StartAsync"));

                if (isAsynInProgress)
                {
                   Ui.Text("Operation in progress");
                }
            });
    }
}

internal static partial class EditorScreen
{
    [Composable]
    public static void Content(EditorViewModel viewModel)
    {
        EditorGraphViewModel graphVm = viewModel.Graph;
        string name = graphVm.Name.Value;
        bool decoration = graphVm.DrawDecoration.Value;
        Ui.Column(modifier: Modifier.Empty.Padding(24),
            spacing: 12,
            content: () =>
            {
                Ui.Row(spacing: 4, content: () =>
                {
                    Ui.Text(graphVm.Greeting, fontSize: 24);
                    Ui.Text(name, fontSize: 24);
                });

                Ui.TextField(name,
                    onValueChange: value => graphVm.Name.Value = value,
                    placeholder: "Your name");

                Ui.Row(spacing: 8, content: () =>
                {
                    Ui.Text("Draw decoration");
                    Ui.Switch(decoration, value => graphVm.DrawDecoration.Value = value);
                });
                if (decoration)
                {
                    ResetButton(() => graphVm.DrawDecoration.Value = false);
                }

                int counter = graphVm.Counter.Value;
                CounterButton(counter,
                    onAdd: () => graphVm.AddToCounter(1),
                    onDecrease: () => graphVm.AddToCounter(-1));

                if (counter > 0)
                {
                    for (int i = 0; i < Math.Min(counter, 3); i++)
                    {
                        Ui.Text("* Counter Element " + i.ToString());
                    }

                    if (counter > 3)
                        Ui.Text("Counter > 3");
                }

                Ui.TextButton(viewModel.ShowPreview, content: () => Ui.Text("Preview"));
                Ui.TextButton(viewModel.Back, content: () => Ui.Text("Back"));
            });
    }

    [Composable]
    public static void ResetButton(Action? onClick = default)
    {
        Ui.Box(modifier: Modifier.Empty
            .Clickable(onClick ?? EmptyHandle)
            .Background(Colors.GreenYellow)
            .Padding(12),
            content: () =>
            {
                Ui.Text("Draw decoration Enabled; CLick here to disable");
            });
    }


    [Composable]
    public static void CounterButton(int counter, Action? onAdd = default, Action? onDecrease = default)
    {
        Ui.Box(modifier: Modifier.Empty
            .Padding(12),
            content: () =>
            {
                Ui.Row(content: () =>
                {
                    int fontSizeConst = 24;
                    Ui.TextButton(onAdd ?? EmptyHandle, content: () => Ui.Text("+", fontSize: fontSizeConst));
                    Ui.Text(counter.ToString());
                    Ui.TextButton(onDecrease ?? EmptyHandle, content: () => Ui.Text("-", fontSize: fontSizeConst));
                });
            });
    }
    public static void EmptyHandle()
    {
    }
}

internal static partial class PreviewScreen
{
    [Composable]
    public static void Content(PreviewViewModel viewModel)
    {
        EditorGraphViewModel graph = viewModel.Graph;
        string name = graph.Name.Value;
        string editLabel = "Edit";
        Ui.Column(
            modifier: Modifier.Empty.Padding(24),
            spacing: 12,
            content: () =>
            {
                Ui.Text($"Hello, {name}", fontSize: 24);
                Ui.Canvas(
                    Modifier.Empty.Size(240, 120).Background(Colors.SteelBlue),
                    scope =>
                    {
                        if (graph.DrawDecoration.Value)
                        {
                            scope.Canvas.StrokeColor = Colors.White;
                            scope.Canvas.StrokeSize = 3;
                            scope.Canvas.DrawLine(10, 10, 230, 110);
                        }
                    });
                Ui.OutlinedButton(viewModel.Back, content: () => Ui.Text(editLabel));
            });
    }
}
