using DotNetCompose.Runtime;

namespace DotNetCompose.Tui;

public static partial class Tui
{
    public static readonly ProvidableCompositionLocal<TuiTheme> LocalTheme =
        Composables.CompositionLocalOf(() => TuiTheme.Default);

    [Composable]
    public static void Theme(TuiTheme theme, [Composable] Action content)
    {
        Composables.CompositionLocalProvider(LocalTheme.Provides(theme), content);
    }

    [Composable]
    public static void Text(
        string text,
        TuiLayout layout = default,
        TuiStyle? style = null)
    {
        TuiTheme theme = LocalTheme.Current();
        Composables.ComposeNode(
            () => new TextNode(),
            node =>
            {
                node.Text = text;
                node.Layout = layout;
                node.Style = style;
                node.Theme = theme;
            });
    }

    [Composable]
    public static void Spacer(TuiLayout layout = default)
    {
        Composables.ComposeNode(
            () => new SpacerNode(),
            node => node.Layout = layout);
    }

    [Composable(ComposableMode.Inline)]
    public static void Row(
        int gap = 0,
        TuiLayout layout = default,
        [Composable] Action content = null!)
    {
        Stack(StackOrientation.Horizontal, content, gap, layout);
    }

    [Composable(ComposableMode.Inline)]
    public static void Column(
        int gap = 0,
        TuiLayout layout = default,
        [Composable] Action content = null!)
    {
        Stack(StackOrientation.Vertical, content, gap, layout);
    }

    [Composable(ComposableMode.Inline)]
    private static void Stack(
        StackOrientation orientation,
        [Composable] Action content,
        int gap,
        TuiLayout layout)
    {
        TuiTheme theme = LocalTheme.Current();
        Composables.ComposeNode(
            () => new StackNode(orientation),
            node =>
            {
                node.Gap = gap;
                node.Layout = layout;
                node.Theme = theme;
            },
            content);
    }

    [Composable(ComposableMode.Inline)]
    public static void Border(
        string? title = null,
        TuiLayout layout = default,
        [Composable] Action content = null!)
    {
        TuiTheme theme = LocalTheme.Current();
        Composables.ComposeNode(
            () => new BorderNode(),
            node =>
            {
                node.Title = title;
                node.Layout = layout;
                node.Theme = theme;
            },
            content);
    }

    [Composable]
    public static void Button(
        string label,
        Action onClick,
        bool enabled = true,
        TuiLayout layout = default)
    {
        TuiTheme theme = LocalTheme.Current();
        Composables.ComposeNode(
            () => new ButtonNode(),
            node =>
            {
                node.Label = label;
                node.OnClick = onClick;
                node.Enabled = enabled;
                node.Layout = layout;
                node.Theme = theme;
            });
    }

    [Composable]
    public static void TextField(
        string value,
        Action<string> onValueChanged,
        string placeholder = "",
        bool enabled = true,
        TuiLayout layout = default)
    {
        TuiTheme theme = LocalTheme.Current();
        Composables.ComposeNode(
            () => new TextFieldNode(),
            node =>
            {
                node.Value = value;
                node.OnValueChanged = onValueChanged;
                node.Placeholder = placeholder;
                node.Enabled = enabled;
                node.Layout = layout;
                node.Theme = theme;
            });
    }

    [Composable(ComposableMode.Inline)]
    public static void List<T>(
        IReadOnlyList<T> items,
        Func<T, object?> key,
        int selectedIndex,
        Action<int> onSelectionChanged,
        TuiLayout layout = default,
        [Composable] Action<T> itemContent = null!)
    {
        TuiTheme theme = LocalTheme.Current();
        Composables.ComposeNode(
            () => new ListNode(),
            node =>
            {
                node.SelectedIndex = selectedIndex;
                node.OnSelectionChanged = onSelectionChanged;
                node.Layout = layout;
                node.Theme = theme;
            },
            () =>
            {
                foreach (T item in items)
                {
                    T captured = item;
                    Composables.Key(
                        key(captured),
                        () => itemContent(captured));
                }
            });
    }
}
