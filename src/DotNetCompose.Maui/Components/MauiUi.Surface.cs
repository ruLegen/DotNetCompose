using DotNetCompose.Runtime;
using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui;

public static partial class MauiUi
{
    [Composable(ComposableMode.NonSkippable)]
    public static void Surface(
        Modifier modifier = default,
        Color? color = null,
        Color? contentColor = null,
        [Composable] Action content = null!)
    {
        ArgumentNullException.ThrowIfNull(content);
        MauiThemeData theme = MauiTheme.Current();
        Color resolvedColor = color ?? theme.Colors.Surface;
        Color resolvedContentColor = contentColor ?? theme.Colors.OnSurface;
        MauiTheme.ProvideContentColor(resolvedContentColor, () =>
        {
            Box(
                modifier: modifier.Background(resolvedColor),
                propagateMinConstraints: true,
                content: content);
        });
    }
}
