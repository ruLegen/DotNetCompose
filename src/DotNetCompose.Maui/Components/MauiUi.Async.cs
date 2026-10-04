using DotNetCompose.Runtime;

namespace DotNetCompose.Maui;

public static partial class MauiUi
{
    /// <summary>The page/window activities explicitly configured on the composition host.</summary>
    public static readonly ProvidableCompositionLocal<MauiTaskActivities> LocalTaskActivities =
        Composables.StaticCompositionLocalOf<MauiTaskActivities>(() =>
            throw new InvalidOperationException("Configure MauiComposeView.TaskActivities before mounting lifecycle-aware UI."));
}
