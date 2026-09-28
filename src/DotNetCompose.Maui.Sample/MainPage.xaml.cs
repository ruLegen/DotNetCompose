using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using Microsoft.Maui.Graphics;
using Ui = DotNetCompose.Maui.MauiUi;
using UiModifier = DotNetCompose.Maui.Modifier;

namespace DotNetCompose.Maui.Sample;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        Grid root = new()
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        Button reload = new() { Text = "Unload and reload composition" };
        MauiComposeView compose = new();
        root.Add(reload);
        root.Add(compose);
        Grid.SetRow(compose, 1);
        reload.Clicked += (_, _) =>
        {
            root.Remove(compose);
            Dispatcher.Dispatch(() =>
            {
                root.Add(compose);
                Grid.SetRow(compose, 1);
            });
        };
        Content = root;
        compose.SetContent(DemoScreen.Builders.Content);
    }
}

internal static partial class DemoScreen
{
    private static readonly SnapshotMutableState<int> Count = Composables.CreateMutableState(0);

    [Composable]
    public static void Content()
    {
        int count = Count.Value;
        Ui.Column(spacing: 12, modifier: UiModifier.Empty.Fill().Padding(24), content: () =>
        {
            Ui.NativeView(
                factory: () => new Label(),
                update: label => label.Text = $"Count: {count}",
                modifier: UiModifier.Empty.Padding(8).Background(Colors.LightBlue));

            Ui.NativeView(
                factory: () =>
                {
                    Button button = new() { Text = "Increment" };
                    button.Clicked += (_, _) => Count.Value++;
                    return button;
                },
                modifier: UiModifier.Empty.Size(160, 48));

            Ui.Canvas(
                UiModifier.Empty.Size(160, 100 + count % 3 * 20).DrawWithContent(scope =>
                {
                    scope.DrawContent();
                    scope.Canvas.StrokeColor = Colors.White;
                    scope.Canvas.StrokeSize = 2;
                    scope.Canvas.DrawLine(8, 8, 152, 92);
                }),
                onDraw: scope =>
                {
                    scope.Canvas.FillColor = Count.Value % 2 == 0 ? Colors.SteelBlue : Colors.OrangeRed;
                    scope.Canvas.FillRectangle(scope.Bounds);
                });
        });
    }
}
