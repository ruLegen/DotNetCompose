using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui;

public enum MauiThemeMode
{
    System,
    Light,
    Dark
}

public sealed record MauiColorScheme(
    Color Primary,
    Color OnPrimary,
    Color Surface,
    Color OnSurface,
    Color Outline,
    Color Error);

public sealed record MauiTypography(double BodySize, double TitleSize, double ButtonSize);

public sealed record MauiSpacing(double Small, double Medium, double Large);

public sealed record ButtonColors(
    Color Container,
    Color Content,
    Color DisabledContainer,
    Color DisabledContent,
    Color Outline);

public sealed record TextFieldColors(
    Color Text,
    Color Background,
    Color Placeholder,
    Color UnfocusedBorder,
    Color FocusedBorder,
    Color ErrorBorder,
    Color DisabledBorder,
    Color DisabledText);

public sealed record SwitchColors(
    Color On,
    Color Off,
    Color Thumb,
    Color Disabled);

public sealed record MauiThemeData(
    MauiColorScheme Colors,
    MauiTypography Typography,
    MauiSpacing Spacing,
    ButtonColors Buttons,
    TextFieldColors TextFields,
    SwitchColors Switches);

public sealed record MauiThemeSet(MauiThemeData Light, MauiThemeData Dark)
{
    public static MauiThemeSet Default { get; } = new(
        new MauiThemeData(
            new MauiColorScheme(Colors.SteelBlue, Colors.White, Colors.White,
                Colors.Black, Colors.SlateGray, Colors.Firebrick),
            new MauiTypography(16, 24, 16),
            new MauiSpacing(4, 12, 24),
            new ButtonColors(Colors.SteelBlue, Colors.White, Colors.LightGray,
                Colors.Gray, Colors.SteelBlue),
            new TextFieldColors(Colors.Black, Colors.White, Colors.Gray,
                Colors.SlateGray, Colors.SteelBlue, Colors.Firebrick,
                Colors.LightGray, Colors.Gray),
            new SwitchColors(Colors.SteelBlue, Colors.LightGray, Colors.White, Colors.Gray)),
        new MauiThemeData(
            new MauiColorScheme(Colors.CornflowerBlue, Colors.Black, Colors.Black,
                Colors.White, Colors.LightSlateGray, Colors.IndianRed),
            new MauiTypography(16, 24, 16),
            new MauiSpacing(4, 12, 24),
            new ButtonColors(Colors.CornflowerBlue, Colors.Black, Colors.DarkGray,
                Colors.LightGray, Colors.CornflowerBlue),
            new TextFieldColors(Colors.White, Colors.Black, Colors.LightGray,
                Colors.LightSlateGray, Colors.CornflowerBlue, Colors.IndianRed,
                Colors.DarkGray, Colors.Gray),
            new SwitchColors(Colors.CornflowerBlue, Colors.DarkGray, Colors.White, Colors.Gray)));
}

public static partial class MauiTheme
{
    private static readonly ProvidableCompositionLocal<MauiThemeData> LocalTheme =
        Composables.CompositionLocalOf(() => MauiThemeSet.Default.Light);

    private static readonly ProvidableCompositionLocal<Color?> LocalContentColor =
        Composables.CompositionLocalOf<Color?>(() => null);

    private static readonly ProvidableCompositionLocal<double?> LocalTextSize =
        Composables.CompositionLocalOf<double?>(() => null);

    [Composable(ComposableMode.ReadOnly)]
    public static MauiThemeData Current()
    {
        return LocalTheme.Current();
    }

    [Composable(ComposableMode.ReadOnly)]
    public static Color ContentColor()
    {
        Color? provided = LocalContentColor.Current();
        return provided ?? Current().Colors.OnSurface;
    }

    [Composable(ComposableMode.ReadOnly)]
    public static double TextSize()
    {
        double? provided = LocalTextSize.Current();
        return provided ?? Current().Typography.BodySize;
    }

    [Composable(ComposableMode.NonSkippable)]
    public static void Provide(MauiThemeSet? themeSet = null, MauiThemeMode mode = MauiThemeMode.System,
        [Composable] Action content = null!)
    {
        Application? application = Application.Current;
        SnapshotMutableState<AppTheme> systemTheme = Composables.RememberState(
            application?.RequestedTheme ?? AppTheme.Light);
        Composables.DisposableEffect(application, () =>
        {
            if (application is null)
            {
                return new ActionDisposable(() =>
                {
                });
            }

            EventHandler<AppThemeChangedEventArgs> handler = (_, args) => systemTheme.Value = args.RequestedTheme;
            application.RequestedThemeChanged += handler;
            return new ActionDisposable(() => application.RequestedThemeChanged -= handler);
        });

        MauiThemeSet set = themeSet ?? MauiThemeSet.Default;
        bool dark = mode == MauiThemeMode.Dark ||
            (mode == MauiThemeMode.System && systemTheme.Value == AppTheme.Dark);
        Composables.CompositionLocalProvider(LocalTheme.Provides(dark ? set.Dark : set.Light), content);
    }

    [Composable(ComposableMode.NonSkippable)]
    internal static void ProvideContent(Color color, double textSize, [Composable] Action content)
    {
        Composables.CompositionLocalProvider(
            new ProvidedValue[] { LocalContentColor.Provides(color), LocalTextSize.Provides(textSize) },
            content);
    }

    [Composable(ComposableMode.NonSkippable)]
    internal static void ProvideContentColor(Color color, [Composable] Action content)
    {
        Composables.CompositionLocalProvider(LocalContentColor.Provides(color), content);
    }

    private sealed class ActionDisposable(Action action) : IDisposable
    {
        public void Dispose()
        {
            action();
        }
    }
}

public static partial class ButtonDefaults
{
    [Composable(ComposableMode.ReadOnly)]
    public static ButtonColors Colors()
    {
        return MauiTheme.Current().Buttons;
    }
}

public static partial class TextFieldDefaults
{
    [Composable(ComposableMode.ReadOnly)]
    public static TextFieldColors Colors()
    {
        return MauiTheme.Current().TextFields;
    }
}

public static partial class SwitchDefaults
{
    [Composable(ComposableMode.ReadOnly)]
    public static SwitchColors Colors()
    {
        return MauiTheme.Current().Switches;
    }
}
