using DotNetCompose.Runtime;

namespace DotNetCompose.Maui;

public static partial class MauiUi
{
    [Composable(ComposableMode.NonSkippable)]
    public static void NavHost(MauiNavigator navigator, Modifier modifier = default)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        MauiNavigator.ScreenEntry entry = navigator.CurrentEntry;
        Box(modifier: modifier, content: () =>
        {
            Composables.Key(entry.Id, () =>
            {
                _ = Composables.Remember(entry.Id,
                    () => new MauiNavigator.VisualLifetime(navigator, entry.Id));
                entry.Render(Composables.CurrentContext()
                    ?? throw new InvalidOperationException("NavHost requires a composition."));
            });
        });
    }
}
