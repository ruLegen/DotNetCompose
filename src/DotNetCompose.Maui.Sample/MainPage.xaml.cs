using DotNetCompose.Maui.Sample.Navigation;
using DotNetCompose.Maui.Sample.Screens;
using DotNetCompose.Maui.Sample.Screens.App;

namespace DotNetCompose.Maui.Sample;

public partial class MainPage : ContentPage, IDisposable
{
    private readonly MauiComposeView _compose = new();
    private readonly MauiNavigator _navigator;
    private readonly SampleAppState _state;

    public MainPage(IServiceProvider services)
    {
        InitializeComponent();
        _navigator = new MauiNavigator(SampleNavigation.CreateGraph(), services);
        _state = new SampleAppState(_navigator);

        Grid root = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };

        HorizontalStackLayout controls = new();
        Button reload = new() { Text = "Reload host" };
        reload.Clicked += (_, _) =>
        {
            root.Remove(_compose);
            Dispatcher.Dispatch(() =>
            {
                root.Add(_compose);
                Grid.SetRow(_compose, 1);
            });
        };
        Button theme = new() { Text = "Light / Dark" };
        theme.Clicked += (_, _) =>
        {
            _state.Theme = _state.Theme == MauiThemeMode.Dark
                ? MauiThemeMode.Light
                : MauiThemeMode.Dark;
        };
        controls.Add(reload);
        controls.Add(theme);
        root.Add(controls);
        root.Add(_compose);
        Grid.SetRow(_compose, 1);
        Content = root;

        _compose.SetContent(_state, AppScreen.Builders.Content);
    }

    protected override bool OnBackButtonPressed()
    {
        return _navigator.Pop() || base.OnBackButtonPressed();
    }

    public void Dispose()
    {
        _compose.Dispose();
        _navigator.Dispose();
    }
}
