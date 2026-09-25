using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Tui;

public static partial class Tui
{
    public static readonly ProvidableCompositionLocal<TuiTheme> LocalTheme =
        Composables.CompositionLocalOf(() => TuiTheme.Default);

    public static partial class Builders
    {
        public static void Theme(TuiTheme theme, ComposableAction content, IComposerContext context,
            ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            => Composables.Builders.CompositionLocalProvider(LocalTheme.Provides(theme), content, context, changed, defaultState);

        public static void Text(string text, TuiLayout layout, TuiStyle? style, IComposerContext context,
            ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
        {
            TuiTheme theme = LocalTheme.Current;
            Composables.Builders.ComposeNode(
                () => new TextNode(),
                node => { node.Text = text; node.Layout = layout; node.Style = style; node.Theme = theme; },
                context, changed, defaultState);
        }

        public static void Spacer(TuiLayout layout, IComposerContext context,
            ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            => Composables.Builders.ComposeNode(() => new SpacerNode(), node => node.Layout = layout,
                context, changed, defaultState);

        public static void Row(int gap, TuiLayout layout, ComposableAction content, IComposerContext context,
            ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            => Stack(StackOrientation.Horizontal, content, gap, layout, context, changed, defaultState);

        public static void Column(int gap, TuiLayout layout, ComposableAction content, IComposerContext context,
            ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
            => Stack(StackOrientation.Vertical, content, gap, layout, context, changed, defaultState);

        private static void Stack(StackOrientation orientation, ComposableAction content, int gap, TuiLayout layout,
            IComposerContext context, ComposableArgumentsState changed, ComposableArgumentsDefaultState defaultState)
        {
            TuiTheme theme = LocalTheme.Current;
            Composables.Builders.ComposeNode(
                () => new StackNode(orientation),
                node => { node.Gap = gap; node.Layout = layout; node.Theme = theme; },
                content, context, changed, defaultState);
        }

        public static void Border(string? title, TuiLayout layout, ComposableAction content, IComposerContext context,
            ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
        {
            TuiTheme theme = LocalTheme.Current;
            Composables.Builders.ComposeNode(
                () => new BorderNode(),
                node => { node.Title = title; node.Layout = layout; node.Theme = theme; },
                content, context, changed, defaultState);
        }

        public static void Button(string label, Action onClick, bool enabled, TuiLayout layout, IComposerContext context,
            ComposableArgumentsState changed = default, ComposableArgumentsDefaultState defaultState = default)
        {
            TuiTheme theme = LocalTheme.Current;
            Composables.Builders.ComposeNode(
                () => new ButtonNode(),
                node =>
                {
                    node.Label = label; node.OnClick = onClick; node.Enabled = enabled;
                    node.Layout = layout; node.Theme = theme;
                }, context, changed, defaultState);
        }

        public static void TextField(string value, Action<string> onValueChanged, string placeholder, bool enabled,
            TuiLayout layout, IComposerContext context, ComposableArgumentsState changed = default,
            ComposableArgumentsDefaultState defaultState = default)
        {
            TuiTheme theme = LocalTheme.Current;
            Composables.Builders.ComposeNode(
                () => new TextFieldNode(),
                node =>
                {
                    node.Value = value; node.OnValueChanged = onValueChanged; node.Placeholder = placeholder;
                    node.Enabled = enabled; node.Layout = layout; node.Theme = theme;
                }, context, changed, defaultState);
        }

        public static void List<T>(IReadOnlyList<T> items,
            Func<T, object?> key,
            int selectedIndex,
            Action<int> onSelectionChanged,
            TuiLayout layout,
            ComposableAction<T> itemContent,
            IComposerContext context, 
            ComposableArgumentsState changed = default,
            ComposableArgumentsDefaultState defaultState = default)
        {
            TuiTheme theme = LocalTheme.Current;
            Composables.Builders.ComposeNode(
                () => new ListNode(),
                node =>
                {
                    node.SelectedIndex = selectedIndex; 
                    node.OnSelectionChanged = onSelectionChanged;
                    node.Layout = layout; 
                    node.Theme = theme;
                },
                (composer, _, _) =>
                {
                    foreach (T item in items)
                    {
                        T captured = item;
                        Composables.Builders.Key(key(captured),
                            (keyed, __, ___) => itemContent(captured, keyed, default, default), composer);
                    }
                }, context, changed, defaultState);
        }
    }

    [Composable, ComposableIgnore]
    public static void Theme(TuiTheme theme, [Composable] Action content) => throw Stub();

    [Composable, ComposableIgnore]
    public static void Text(string text, TuiLayout layout = default, TuiStyle? style = null) => throw Stub();

    [Composable, ComposableIgnore]
    public static void Spacer(TuiLayout layout = default) => throw Stub();

    [Composable, ComposableIgnore]
    public static void Row(int gap = 0, TuiLayout layout = default, [Composable] Action content = null!) => throw Stub();

    [Composable, ComposableIgnore]
    public static void Column(int gap = 0, TuiLayout layout = default, [Composable] Action content = null!) => throw Stub();

    [Composable, ComposableIgnore]
    public static void Border(string? title = null, TuiLayout layout = default, [Composable] Action content = null!) => throw Stub();

    [Composable, ComposableIgnore]
    public static void Button(string label, Action onClick, bool enabled = true, TuiLayout layout = default) => throw Stub();

    [Composable, ComposableIgnore]
    public static void TextField(string value, Action<string> onValueChanged, string placeholder = "",
        bool enabled = true, TuiLayout layout = default) => throw Stub();

    [Composable, ComposableIgnore]
    public static void List<T>(IReadOnlyList<T> items, Func<T, object?> key, int selectedIndex,
        Action<int> onSelectionChanged, TuiLayout layout = default, [Composable] Action<T> itemContent = null!) => throw Stub();

    private static NotImplementedException Stub() => new("Composable calls must be rewritten by DotNetCompose.SourceGenerators.");
}
