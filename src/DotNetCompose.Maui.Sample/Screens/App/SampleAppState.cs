using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Maui.Sample.Screens.App;

public sealed class SampleAppState(MauiNavigator navigator)
{
    private readonly SnapshotMutableState<MauiThemeMode> _theme =
        Composables.CreateMutableState(MauiThemeMode.System);

    public MauiNavigator Navigator { get; } = navigator;

    public MauiThemeMode Theme
    {
        get
        {
            return _theme.Value;
        }
        set
        {
            _theme.Value = value;
        }
    }
}
