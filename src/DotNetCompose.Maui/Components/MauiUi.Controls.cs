using System.Runtime.CompilerServices;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui;

public static partial class MauiUi
{
    private static readonly ConditionalWeakTable<Entry, EntryCallback> EntryCallbacks = new();
    private static readonly ConditionalWeakTable<Switch, SwitchCallback> SwitchCallbacks = new();
    private static readonly ConditionalWeakTable<ScrollView, ScrollCallback> ScrollCallbacks = new();

    internal static void ReleaseScroll(ScrollView view)
    {
        if (ScrollCallbacks.TryGetValue(view, out ScrollCallback? callback))
        {
            callback.Dispose();
        }
        ScrollCallbacks.Remove(view);
    }

    [Composable]
    public static void Text(string? text, Modifier modifier = default,
        Color? color = null, double? fontSize = null)
    {
        Color resolvedColor = color ?? MauiTheme.ContentColor();
        double resolvedSize = fontSize ?? MauiTheme.TextSize();
        NativeView(() => new Label(), label =>
        {
            label.Text = text ?? "";
            label.TextColor = resolvedColor;
            label.FontSize = resolvedSize;
        }, modifier);
    }

    [Composable]
    public static void TextField(string value, Action<string> onValueChange,
        Modifier modifier = default, bool enabled = true, string? placeholder = null,
        bool isError = false, TextFieldColors? colors = null)
    {
        ArgumentNullException.ThrowIfNull(onValueChange);
        TextFieldColors resolvedColors = colors ?? TextFieldDefaults.Colors();
        SnapshotMutableState<bool> focusState = Composables.RememberState(false);
        Color borderColor = !enabled ? resolvedColors.DisabledBorder
            : isError ? resolvedColors.ErrorBorder
            : focusState.Value ? resolvedColors.FocusedBorder
            : resolvedColors.UnfocusedBorder;
        NativeView(
            factory: () =>
            {
                Entry entry = new();
                EntryCallbacks.Add(entry, new EntryCallback(entry));
                return entry;
            },
            update: entry =>
            {
                EntryCallbacks.GetValue(entry, item => new EntryCallback(item))
                    .Update(value, onValueChange, focused => focusState.Value = focused);
                entry.IsEnabled = enabled;
                entry.Placeholder = placeholder;
                entry.TextColor = enabled ? resolvedColors.Text : resolvedColors.DisabledText;
                entry.PlaceholderColor = resolvedColors.Placeholder;
                entry.BackgroundColor = resolvedColors.Background;
            },
            modifier: modifier.Border(borderColor),
            onRelease: entry =>
            {
                if (EntryCallbacks.TryGetValue(entry, out EntryCallback? callback))
                {
                    callback.Dispose();
                }
                EntryCallbacks.Remove(entry);
            });
    }

    [Composable]
    public static void Switch(bool isChecked, Action<bool> onCheckedChange,
        Modifier modifier = default, bool enabled = true, SwitchColors? colors = null)
    {
        ArgumentNullException.ThrowIfNull(onCheckedChange);
        SwitchColors resolvedColors = colors ?? SwitchDefaults.Colors();
        NativeView(
            factory: () =>
            {
                Switch control = new();
                SwitchCallbacks.Add(control, new SwitchCallback(control));
                return control;
            },
            update: control =>
            {
                SwitchCallbacks.GetValue(control, item => new SwitchCallback(item))
                    .Update(isChecked, onCheckedChange);
                control.IsEnabled = enabled;
                control.OnColor = enabled ? resolvedColors.On : resolvedColors.Disabled;
                control.ThumbColor = resolvedColors.Thumb;
                control.BackgroundColor = isChecked ? resolvedColors.On : resolvedColors.Off;
            },
            modifier: modifier,
            onRelease: control =>
            {
                if (SwitchCallbacks.TryGetValue(control, out SwitchCallback? callback))
                {
                    callback.Dispose();
                }
                SwitchCallbacks.Remove(control);
            });
    }

    [Composable]
    public static void Image(ImageSource source, string? contentDescription,
        Modifier modifier = default, Aspect aspect = Aspect.AspectFit)
    {
        ArgumentNullException.ThrowIfNull(source);
        NativeView(() => new Microsoft.Maui.Controls.Image(), image =>
        {
            image.Source = source;
            image.Aspect = aspect;
            SemanticProperties.SetDescription(image, contentDescription);
            AutomationProperties.SetIsInAccessibleTree(image, contentDescription is not null);
        }, modifier);
    }

    [Composable]
    public static void Spacer(Modifier modifier)
    {
        NativeView(() => new ContentView(), modifier: modifier);
    }

    [Composable(ComposableMode.Inline)]
    public static ScrollState RememberScrollState(double initialOffset = 0)
    {
        return Composables.Remember(0, () => new ScrollState(initialOffset));
    }

    [Composable(ComposableMode.Inline)]
    public static void Scroll(Modifier modifier = default, ScrollOrientation orientation = ScrollOrientation.Vertical,
        [Composable] Action content = null!)
    {
        ScrollState state = RememberScrollState(0);
        Scroll(state, modifier, orientation, content);
    }

    [Composable(ComposableMode.Inline)]
    public static void Scroll(ScrollState state, Modifier modifier = default,
        ScrollOrientation orientation = ScrollOrientation.Vertical, [Composable] Action content = null!)
    {
        ArgumentNullException.ThrowIfNull(state);
        double offset = state.Offset;
        Composables.ComposeNode(
            () =>
            {
                ScrollView view = new();
                ScrollCallbacks.Add(view, new ScrollCallback(view));
                return new MauiNode(view, acceptsChildren: true);
            },
            node =>
            {
                ScrollView view = (ScrollView)node.Core;
                view.Orientation = orientation;
                ScrollCallbacks.GetValue(view, item => new ScrollCallback(item))
                    .Update(state, offset, orientation);
                node.ApplyModifier(modifier);
            },
            content);
    }

    [Composable(ComposableMode.Inline)]
    public static void ForEach<T>(IReadOnlyList<T> items, Func<T, object> key,
        [Composable] Action<T> content)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);
        foreach (T item in items)
        {
            Composables.Key(key(item), () => content(item));
        }
    }

    [Composable(ComposableMode.NonSkippable)]
    public static void Button(Action onClick, Modifier modifier = default, bool enabled = true,
        string? onClickLabel = null, ButtonColors? colors = null, [Composable] Action content = null!)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        MauiThemeData theme = MauiTheme.Current();
        ButtonColors palette = colors ?? theme.Buttons;
        Color background = enabled ? palette.Container : palette.DisabledContainer;
        Color foreground = enabled ? palette.Content : palette.DisabledContent;
        MauiTheme.ProvideContent(foreground, theme.Typography.ButtonSize, () =>
        {
            Row(modifier: modifier.Clickable(onClick, enabled, onClickLabel, ClickableRole.Button)
                    .Background(background).Padding(16, 10, 16, 10),
                content: content);
        });
    }

    [Composable(ComposableMode.NonSkippable)]
    public static void OutlinedButton(Action onClick, Modifier modifier = default, bool enabled = true,
        string? onClickLabel = null, ButtonColors? colors = null, [Composable] Action content = null!)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        MauiThemeData theme = MauiTheme.Current();
        ButtonColors palette = colors ?? theme.Buttons;
        Color foreground = enabled ? palette.Outline : palette.DisabledContent;
        MauiTheme.ProvideContent(foreground, theme.Typography.ButtonSize, () =>
        {
            Row(modifier: modifier.Clickable(onClick, enabled, onClickLabel, ClickableRole.Button)
                    .Border(foreground, 1).Padding(16, 10, 16, 10),
                content: content);
        });
    }

    [Composable(ComposableMode.NonSkippable)]
    public static void TextButton(Action onClick, Modifier modifier = default, bool enabled = true,
        string? onClickLabel = null, ButtonColors? colors = null, [Composable] Action content = null!)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        MauiThemeData theme = MauiTheme.Current();
        ButtonColors palette = colors ?? theme.Buttons;
        Color foreground = enabled ? palette.Container : palette.DisabledContent;
        MauiTheme.ProvideContent(foreground, theme.Typography.ButtonSize, () =>
        {
            Row(modifier: modifier.Clickable(onClick, enabled, onClickLabel, ClickableRole.Button)
                    .Padding(16, 10, 16, 10),
                content: content);
        });
    }

    private sealed class EntryCallback : IDisposable
    {
        private readonly Entry _entry;
        private Action<string>? _callback;
        private Action<bool>? _focusChanged;
        private bool _updating;

        internal EntryCallback(Entry entry)
        {
            _entry = entry;
            _entry.TextChanged += OnChanged;
            _entry.Focused += OnFocused;
            _entry.Unfocused += OnUnfocused;
        }

        internal void Update(string value, Action<string> callback, Action<bool> focusChanged)
        {
            _callback = callback;
            _focusChanged = focusChanged;
            if (_entry.Text == value)
            {
                return;
            }
            _updating = true;
            try
            {
                _entry.Text = value;
            }
            finally
            {
                _updating = false;
            }
        }

        private void OnChanged(object? sender, TextChangedEventArgs args)
        {
            if (!_updating)
            {
                _callback?.Invoke(args.NewTextValue);
            }
        }

        private void OnFocused(object? sender, FocusEventArgs args)
        {
            _focusChanged?.Invoke(true);
        }

        private void OnUnfocused(object? sender, FocusEventArgs args)
        {
            _focusChanged?.Invoke(false);
        }

        public void Dispose()
        {
            _entry.TextChanged -= OnChanged;
            _entry.Focused -= OnFocused;
            _entry.Unfocused -= OnUnfocused;
            _callback = null;
            _focusChanged = null;
        }
    }

    private sealed class SwitchCallback : IDisposable
    {
        private readonly Switch _switch;
        private Action<bool>? _callback;
        private bool _updating;

        internal SwitchCallback(Switch control)
        {
            _switch = control;
            _switch.Toggled += OnToggled;
        }

        internal void Update(bool value, Action<bool> callback)
        {
            _callback = callback;
            if (_switch.IsToggled == value)
            {
                return;
            }
            _updating = true;
            try
            {
                _switch.IsToggled = value;
            }
            finally
            {
                _updating = false;
            }
        }

        private void OnToggled(object? sender, ToggledEventArgs args)
        {
            if (!_updating)
            {
                _callback?.Invoke(args.Value);
            }
        }

        public void Dispose()
        {
            _switch.Toggled -= OnToggled;
            _callback = null;
        }
    }

    private sealed class ScrollCallback : IDisposable
    {
        private readonly ScrollView _view;
        private ScrollState? _state;
        private ScrollOrientation _orientation;

        internal ScrollCallback(ScrollView view)
        {
            _view = view;
            _view.Scrolled += OnScrolled;
            _view.Loaded += OnLoaded;
        }

        internal void Update(ScrollState state, double offset, ScrollOrientation orientation)
        {
            _state = state;
            _orientation = orientation;
            if (_view.IsLoaded)
            {
                _ = Restore(offset);
            }
        }

        private void OnScrolled(object? sender, ScrolledEventArgs args)
        {
            _state?.ScrollTo(Math.Max(0, _orientation == ScrollOrientation.Horizontal ? args.ScrollX : args.ScrollY));
        }

        private void OnLoaded(object? sender, EventArgs args)
        {
            if (_state is not null)
            {
                _ = Restore(_state.Offset);
            }
        }

        private async Task Restore(double offset)
        {
            double current = _orientation == ScrollOrientation.Horizontal ? _view.ScrollX : _view.ScrollY;
            if (Math.Abs(current - offset) < 0.5)
            {
                return;
            }
            if (_orientation == ScrollOrientation.Horizontal)
            {
                await _view.ScrollToAsync(offset, 0, false);
            }
            else
            {
                await _view.ScrollToAsync(0, offset, false);
            }
        }

        public void Dispose()
        {
            _view.Scrolled -= OnScrolled;
            _view.Loaded -= OnLoaded;
            _state = null;
        }
    }
}
