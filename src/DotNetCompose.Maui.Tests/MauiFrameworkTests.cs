using DotNetCompose.Maui.Layout;
using DotNetCompose.Maui.Modifiers;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui.Tests;

public sealed partial class MauiFrameworkTests
{
    private sealed record HomeRoute
    {
    }

    private sealed record FeatureRoute
    {
    }

    private sealed record ListRoute
    {
    }

    private sealed record DetailRoute(int Id)
    {
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class LayoutProbe(Size desiredSize) : View
    {
        internal Size DesiredSizeValue { get; set; } = desiredSize;
        internal Rect ArrangedBounds { get; private set; }
        internal int MeasureCount { get; private set; }

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            MeasureCount++;
            return new Size(
                Math.Min(DesiredSizeValue.Width, widthConstraint),
                Math.Min(DesiredSizeValue.Height, heightConstraint));
        }

        protected override Size ArrangeOverride(Rect bounds)
        {
            ArrangedBounds = bounds;
            return bounds.Size;
        }
    }

    internal sealed class HomeViewModel(List<string> events) : IDisposable
    {
        public void Dispose()
        {
            events.Add("home VM");
        }
    }

    internal sealed class FeatureViewModel(List<string> events) : IDisposable
    {
        public void Dispose()
        {
            events.Add("graph VM");
        }
    }

    internal sealed class ListViewModel(List<string> events) : IDisposable
    {
        public void ViewReleased()
        {
            events.Add("list View");
        }

        public void Dispose()
        {
            events.Add("list VM");
        }
    }

    internal sealed class DetailViewModel(List<string> events, int id) : IDisposable
    {
        public int Id { get; } = id;

        public void ViewReleased()
        {
            events.Add("detail View");
        }

        public void Dispose()
        {
            events.Add("detail VM");
        }
    }

    [Composable]
    internal static void Home(HomeViewModel viewModel)
    {
        MauiUi.Text("Home");
    }

    [Composable]
    internal static void List(ListViewModel viewModel)
    {
        MauiUi.NativeView(() => new Label { Text = "List" }, onRelease: _ => viewModel.ViewReleased());
    }

    [Composable]
    internal static void Detail(DetailViewModel viewModel)
    {
        MauiUi.NativeView(() => new Label { Text = viewModel.Id.ToString() },
            onRelease: _ => viewModel.ViewReleased());
    }

    [Composable]
    internal static void Root(MauiNavigator navigator)
    {
        MauiUi.NavHost(navigator);
    }

    [Composable]
    internal static void ButtonScreen(Action onClick)
    {
        MauiUi.Button(onClick, content: () => MauiUi.Text(onClick.Method.Name));
    }

    [Composable]
    internal static void ThemeScreen(MauiThemeSet theme)
    {
        MauiTheme.Provide(theme, MauiThemeMode.Dark,
            () => MauiUi.Text(theme.Dark.Typography.BodySize.ToString()));
    }

    [Composable]
    internal static void FormScreen(SnapshotMutableState<string> text,
        SnapshotMutableState<bool> isChecked, Action<string> onTextChange,
        Action<bool> onCheckedChange)
    {
        MauiUi.Column(content: () =>
        {
            MauiUi.TextField(text.Value, onTextChange);
            MauiUi.Switch(isChecked.Value, onCheckedChange);
        });
    }

    [Composable]
    internal static void ForEachScreen(IReadOnlyList<int> items)
    {
        MauiUi.Column(content: () =>
        {
            MauiUi.ForEach(items, item => item, item =>
            {
                MauiUi.NativeView(() => new Label(), label => label.Text = item.ToString());
            });
        });
    }

    [Composable]
    internal static void ThemeModeScreen(SnapshotMutableState<MauiThemeMode> mode)
    {
        MauiTheme.Provide(mode: mode.Value, content: () =>
        {
            MauiUi.Text("Themed");
        });
    }

    [Composable]
    internal static void TextScreen(string? text, Modifier modifier = default)
    {
        MauiUi.Text(text, modifier);
    }

    [Composable]
    internal static void ChangingTextScreen(SnapshotMutableState<string?> text)
    {
        MauiUi.Text(text.Value);
    }

    [Composable]
    internal static void SurfaceThemeScreen(SnapshotMutableState<MauiThemeMode> mode)
    {
        MauiTheme.Provide(mode: mode.Value, content: () =>
        {
            MauiUi.Surface(content: () => MauiUi.Text("Surface text"));
        });
    }

    [Composable]
    internal static void ExplicitSurfaceScreen(Color color, Color contentColor)
    {
        MauiTheme.Provide(mode: MauiThemeMode.Dark, content: () =>
        {
            MauiUi.Surface(color: color, contentColor: contentColor,
                content: () => MauiUi.Text("Surface text"));
        });
    }

    [Composable]
    internal static void EffectScreen(int key, List<string> events)
    {
        Composables.DisposableEffect(key, () =>
        {
            events.Add($"setup {key}");
            return new CallbackDisposable(() => events.Add($"dispose {key}"));
        });
    }

    [Composable]
    internal static void ScrollScreen(ScrollState state, string text)
    {
        MauiUi.Scroll(state, content: () => MauiUi.Text(text));
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        public void Dispose()
        {
            callback();
        }
    }

    [Fact]
    public void ModifierUsesSharedOrderedChainAndDefaultIsEmpty()
    {
        Modifier root = default;
        Modifier padded = root.Padding(8);
        Modifier first = padded.Background(Colors.Red);
        Modifier second = padded.Border(Colors.Blue);

        Assert.Empty(root.Elements);
        Assert.Single(padded.Elements);
        Assert.Equal(2, first.Elements.Count);
        Assert.Equal(2, second.Elements.Count);
        Assert.Same(padded.Elements[0], first.Elements[0]);
        Assert.Same(padded.Elements[0], second.Elements[0]);
        Assert.IsType<BackgroundElement>(first.Elements[1]);
        Assert.IsType<BorderElement>(second.Elements[1]);
    }

    [Fact]
    public void BoxUsesDesiredSizeAndAlignmentByDefault()
    {
        ComposeBoxLayout layout = new();
        layout.Update(BoxAlignment.BottomEnd, propagateMinConstraints: false);
        LayoutProbe child = new(new Size(80, 30));
        layout.Add(child);
        ComposeBoxLayoutManager manager = new(layout);

        Assert.Equal(new Size(80, 30), manager.Measure(300, 200));
        int measureCount = child.MeasureCount;
        manager.ArrangeChildren(new Rect(10, 20, 300, 200));

        Assert.Equal(measureCount, child.MeasureCount);
        Assert.Equal(new Rect(230, 190, 80, 30), child.ArrangedBounds);
    }

    [Fact]
    public void BoxPropagatesAllocatedBoundsToAllChildren()
    {
        ComposeBoxLayout layout = new();
        layout.Update(BoxAlignment.Center, propagateMinConstraints: true);
        LayoutProbe first = new(new Size(80, 30));
        LayoutProbe second = new(new Size(120, 50));
        layout.Add(first);
        layout.Add(second);
        ComposeBoxLayoutManager manager = new(layout);

        Assert.Equal(new Size(120, 50), manager.Measure(300, 200));
        int firstMeasureCount = first.MeasureCount;
        int secondMeasureCount = second.MeasureCount;
        Rect allocated = new(10, 20, 300, 200);
        manager.ArrangeChildren(allocated);

        Assert.Equal(firstMeasureCount, first.MeasureCount);
        Assert.Equal(secondMeasureCount, second.MeasureCount);
        Assert.Equal(allocated, first.ArrangedBounds);
        Assert.Equal(allocated, second.ArrangedBounds);
    }

    [Fact]
    public void BoxPropagatesPartiallyFilledAndUpdatedBounds()
    {
        ComposeBoxLayout layout = new();
        layout.Update(BoxAlignment.TopStart, propagateMinConstraints: true);
        LayoutProbe child = new(new Size(80, 30));
        layout.Add(child);
        ComposeBoxLayoutManager manager = new(layout);

        manager.Measure(300, 200);
        int firstMeasureCount = child.MeasureCount;
        manager.ArrangeChildren(new Rect(0, 0, 300, 30));
        Assert.Equal(firstMeasureCount, child.MeasureCount);
        Assert.Equal(new Rect(0, 0, 300, 30), child.ArrangedBounds);

        child.DesiredSizeValue = new Size(140, 70);
        manager.Measure(300, 200);
        int secondMeasureCount = child.MeasureCount;
        manager.ArrangeChildren(new Rect(0, 0, 140, 200));
        Assert.Equal(secondMeasureCount, child.MeasureCount);
        Assert.Equal(new Rect(0, 0, 140, 200), child.ArrangedBounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinearLayoutArrangeUsesMeasuredDesiredSizes(bool horizontal)
    {
        ComposeLinearLayout layout = new(horizontal);
        layout.Update(8, MainAxisArrangement.Start, CrossAxisAlignment.Start);
        LayoutProbe first = new(new Size(40, 20));
        LayoutProbe second = new(new Size(60, 30));
        layout.Add(first);
        layout.Add(second);
        ComposeLinearLayoutManager manager = new(layout);

        manager.Measure(300, 200);
        int firstMeasureCount = first.MeasureCount;
        int secondMeasureCount = second.MeasureCount;
        manager.ArrangeChildren(new Rect(10, 20, 300, 200));

        Assert.Equal(firstMeasureCount, first.MeasureCount);
        Assert.Equal(secondMeasureCount, second.MeasureCount);
        Assert.Equal(new Rect(10, 20, 40, 20), first.ArrangedBounds);
        Assert.Equal(
            horizontal ? new Rect(58, 20, 60, 30) : new Rect(10, 48, 60, 30),
            second.ArrangedBounds);
    }

    [Fact]
    public void ColumnMeasuresInsertedChildBeforeArrangeAndKeepsFollowingChildVisible()
    {
        ComposeLinearLayout layout = new(horizontal: false);
        layout.Update(8, MainAxisArrangement.Start, CrossAxisAlignment.Start);
        LayoutProbe first = new(new Size(80, 20));
        LayoutProbe last = new(new Size(90, 20));
        layout.Add(first);
        layout.Add(last);
        ComposeLinearLayoutManager manager = new(layout);

        manager.Measure(200, 200);
        manager.ArrangeChildren(new Rect(0, 0, 200, 200));

        LayoutProbe middle = new(new Size(100, 40));
        layout.Insert(1, middle);
        manager.Measure(200, 200);
        int firstMeasureCount = first.MeasureCount;
        int middleMeasureCount = middle.MeasureCount;
        int lastMeasureCount = last.MeasureCount;
        manager.ArrangeChildren(new Rect(0, 0, 200, 200));

        Assert.Equal(firstMeasureCount, first.MeasureCount);
        Assert.Equal(middleMeasureCount, middle.MeasureCount);
        Assert.Equal(lastMeasureCount, last.MeasureCount);
        Assert.Equal(new Rect(0, 0, 80, 20), first.ArrangedBounds);
        Assert.Equal(new Rect(0, 28, 100, 40), middle.ArrangedBounds);
        Assert.Equal(new Rect(0, 76, 90, 20), last.ArrangedBounds);
        Assert.All(
            new[] { first, middle, last },
            child =>
            {
                Assert.True(child.ArrangedBounds.Width > 0);
                Assert.True(child.ArrangedBounds.Height > 0);
            });
    }

    [Fact]
    public void ClickableOrderChangesHitAreaAndCallbackIsCurrent()
    {
        int clicks = 0;
        MauiNode outside = new(new Label());
        outside.ApplyModifier(Modifier.Empty.Clickable(() => clicks++).Padding(10));
        ClickableModifierView outerClick = Assert.IsType<ClickableModifierView>(
            ((ContentView)outside.View).Content);
        Assert.IsType<MeasureModifierView>(outerClick.Child);

        MauiNode inside = new(new Label());
        inside.ApplyModifier(Modifier.Empty.Padding(10).Clickable(() => clicks += 10));
        MeasureModifierView padding = Assert.IsType<MeasureModifierView>(
            ((ContentView)inside.View).Content);
        ClickableModifierView innerClick = Assert.IsType<ClickableModifierView>(padding.Child);

        outerClick.Activate();
        innerClick.Activate();
        Assert.Equal(11, clicks);
        outside.ApplyModifier(Modifier.Empty.Clickable(() => clicks += 100, enabled: false).Padding(10));
        outerClick.Activate();
        Assert.Equal(11, clicks);
    }

    [Fact]
    public void ButtonHasComposableContentAndClickAction()
    {
        int clicks = 0;
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.ButtonScreen(() => clicks++, context, changed, defaults));

        MauiNode button = Assert.Single(applier.Root.Children);
        Assert.IsType<ComposeLinearLayout>(button.Core);
        Assert.IsType<Label>(Assert.Single(button.Children).Core);
        ClickableModifierView clickable = Assert.IsType<ClickableModifierView>(
            ((ContentView)button.View).Content);
        clickable.Activate();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void NestedGraphKeepsViewModelsAndReleasesAfterViews()
    {
        List<string> events = new();
        FeatureViewModel? graphViewModel = null;
        NavGraph graph = NavGraph.Create(new HomeRoute(), root =>
        {
            root.Screen<HomeRoute, HomeViewModel>((_, _) => new HomeViewModel(events), Builders.Home);
            root.Graph<FeatureRoute, FeatureViewModel>(
                start: _ => new ListRoute(),
                createViewModel: (_, _) => graphViewModel = new FeatureViewModel(events),
                content: feature =>
                {
                    feature.Screen<ListRoute, ListViewModel>(
                        (_, context) =>
                        {
                            Assert.Same(graphViewModel, context.GetGraphViewModel<FeatureViewModel>());
                            return new ListViewModel(events);
                        }, Builders.List);
                    feature.Screen<DetailRoute, DetailViewModel>(
                        (route, context) =>
                        {
                            Assert.Same(graphViewModel, context.GetGraphViewModel<FeatureViewModel>());
                            return new DetailViewModel(events, route.Id);
                        }, Builders.Detail);
                });
        });

        using MauiNavigator navigator = new(graph, new EmptyServices());
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.Root(navigator, context, changed, defaults));
        Assert.IsType<HomeRoute>(navigator.CurrentRoute);
        Assert.False(navigator.CanPop);

        navigator.Push(new FeatureRoute());
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.IsType<ListRoute>(navigator.CurrentRoute);
        Assert.True(navigator.CanPop);

        navigator.Push(new DetailRoute(42));
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.IsType<DetailRoute>(navigator.CurrentRoute);
        Assert.Equal(new[] { "list View" }, events);

        Assert.True(navigator.Pop());
        Assert.DoesNotContain("detail VM", events);
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal(new[] { "list View", "detail View", "detail VM" }, events);
        Assert.IsType<ListRoute>(navigator.CurrentRoute);

        Assert.True(navigator.Pop());
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal(new[] { "list View", "detail View", "detail VM", "list View", "list VM", "graph VM" }, events);
        Assert.IsType<HomeRoute>(navigator.CurrentRoute);
        Assert.False(navigator.Pop());
    }

    [Fact]
    public void ThemeProvidesDarkDefaultsToText()
    {
        MauiThemeSet set = MauiThemeSet.Default;
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.ThemeScreen(set, context, changed, defaults));

        Label label = Assert.IsType<Label>(Assert.Single(applier.Root.Children).Core);
        Assert.Equal(set.Dark.Colors.OnSurface, label.TextColor);
    }

    [Fact]
    public void ControlledFormReusesViewsAndDetachesCallbacksOnRelease()
    {
        SnapshotMutableState<string> text = Composables.CreateMutableState("initial");
        SnapshotMutableState<bool> isChecked = Composables.CreateMutableState(false);
        int textChanges = 0;
        int checkedChanges = 0;
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.FormScreen(text, isChecked,
                value =>
                {
                    textChanges++;
                    text.Value = value;
                },
                value =>
                {
                    checkedChanges++;
                    isChecked.Value = value;
                },
                context, changed, defaults));

        MauiNode column = Assert.Single(applier.Root.Children);
        Entry entry = Assert.IsType<Entry>(column.Children[0].Core);
        Switch control = Assert.IsType<Switch>(column.Children[1].Core);
        Assert.Equal("initial", entry.Text);

        text.Value = "from ViewModel";
        isChecked.Value = true;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Same(entry, column.Children[0].Core);
        Assert.Same(control, column.Children[1].Core);
        Assert.Equal("from ViewModel", entry.Text);
        Assert.True(control.IsToggled);
        Assert.Equal(0, textChanges);
        Assert.Equal(0, checkedChanges);

        entry.Text = "from user";
        control.IsToggled = false;
        Assert.Equal(1, textChanges);
        Assert.Equal(1, checkedChanges);
        Assert.Equal("from user", text.Value);
        Assert.False(isChecked.Value);

        composition.Dispose();
        entry.Text = "after release";
        control.IsToggled = true;
        Assert.Equal(1, textChanges);
        Assert.Equal(1, checkedChanges);
    }

    [Fact]
    public void ForEachMovesKeyedNodesWithoutReplacingViews()
    {
        SnapshotMutableState<IReadOnlyList<int>> items =
            Composables.CreateMutableState<IReadOnlyList<int>>(new[] { 1, 2, 3 });
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.ForEachScreen(items.Value, context, changed, defaults));

        MauiNode column = Assert.Single(applier.Root.Children);
        View first = column.Children[0].Core;
        View second = column.Children[1].Core;
        View third = column.Children[2].Core;

        items.Value = new[] { 3, 1, 2 };
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Same(third, column.Children[0].Core);
        Assert.Same(first, column.Children[1].Core);
        Assert.Same(second, column.Children[2].Core);
    }

    [Fact]
    public void ThemeModeSwitchUpdatesExistingTextView()
    {
        SnapshotMutableState<MauiThemeMode> mode =
            Composables.CreateMutableState(MauiThemeMode.Light);
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.ThemeModeScreen(mode, context, changed, defaults));

        Label label = Assert.IsType<Label>(Assert.Single(applier.Root.Children).Core);
        Assert.Equal(MauiThemeSet.Default.Light.Colors.OnSurface, label.TextColor);
        mode.Value = MauiThemeMode.Dark;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Same(label, Assert.Single(applier.Root.Children).Core);
        Assert.Equal(MauiThemeSet.Default.Dark.Colors.OnSurface, label.TextColor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyTextNormalizesNullAndKeepsNativeLabel(string? value)
    {
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.TextScreen(value, default, context, changed, defaults));

        Label label = Assert.IsType<Label>(Assert.Single(applier.Root.Children).Core);
        Assert.Equal(string.Empty, label.Text);
    }

    [Fact]
    public void EmptyTextTransitionsRetainNodeAndLabel()
    {
        SnapshotMutableState<string?> value = Composables.CreateMutableState<string?>(null);
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.ChangingTextScreen(value, context, changed, defaults));

        MauiNode node = Assert.Single(applier.Root.Children);
        Label label = Assert.IsType<Label>(node.Core);
        Assert.Equal(string.Empty, label.Text);

        value.Value = "first";
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Same(node, Assert.Single(applier.Root.Children));
        Assert.Same(label, node.Core);
        Assert.Equal("first", label.Text);

        value.Value = "";
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Same(node, Assert.Single(applier.Root.Children));
        Assert.Same(label, node.Core);
        Assert.Equal(string.Empty, label.Text);

        value.Value = "second";
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Same(node, Assert.Single(applier.Root.Children));
        Assert.Same(label, node.Core);
        Assert.Equal("second", label.Text);
    }

    [Fact]
    public void EmptyTextModifiersProvidePaddingAndExplicitSize()
    {
        MauiApplier paddedApplier = new(new Grid());
        using (Composition<MauiNode> composition = new(paddedApplier))
        {
            composition.SetContent((context, changed, defaults) =>
                Builders.TextScreen(null, Modifier.Empty.Padding(10), context, changed, defaults));
            MauiNode node = Assert.Single(paddedApplier.Root.Children);
            MeasureModifierView padding = Assert.IsType<MeasureModifierView>(
                ((ContentView)node.View).Content);
            Assert.Equal(new Size(20, 20), new MeasureModifierLayoutManager(padding).Measure(500, 500));
        }

        MauiApplier sizedApplier = new(new Grid());
        using Composition<MauiNode> sizedComposition = new(sizedApplier);
        sizedComposition.SetContent((context, changed, defaults) =>
            Builders.TextScreen("", Modifier.Empty.Size(80, 40), context, changed, defaults));
        MauiNode sizedNode = Assert.Single(sizedApplier.Root.Children);
        MeasureModifierView size = Assert.IsType<MeasureModifierView>(
            ((ContentView)sizedNode.View).Content);
        Assert.Equal(new Size(80, 40), new MeasureModifierLayoutManager(size).Measure(500, 500));
    }

    [Fact]
    public void SurfaceUpdatesBackgroundAndInheritedTextColorWithTheme()
    {
        SnapshotMutableState<MauiThemeMode> mode = Composables.CreateMutableState(MauiThemeMode.Light);
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.SurfaceThemeScreen(mode, context, changed, defaults));

        MauiNode surface = Assert.Single(applier.Root.Children);
        ComposeBoxLayout surfaceLayout = Assert.IsType<ComposeBoxLayout>(surface.Core);
        DrawModifierView background = Assert.IsType<DrawModifierView>(((ContentView)surface.View).Content);
        Label label = Assert.IsType<Label>(Assert.Single(surface.Children).Core);
        Assert.True(surfaceLayout.PropagateMinConstraints);
        Assert.Equal(MauiThemeSet.Default.Light.Colors.Surface,
            Assert.IsType<BackgroundElement>(background.Element).Color);
        Assert.Equal(MauiThemeSet.Default.Light.Colors.OnSurface, label.TextColor);

        mode.Value = MauiThemeMode.Dark;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Same(surface, Assert.Single(applier.Root.Children));
        Assert.Same(background, ((ContentView)surface.View).Content);
        Assert.Same(label, Assert.Single(surface.Children).Core);
        Assert.Equal(MauiThemeSet.Default.Dark.Colors.Surface,
            Assert.IsType<BackgroundElement>(background.Element).Color);
        Assert.Equal(MauiThemeSet.Default.Dark.Colors.OnSurface, label.TextColor);
    }

    [Fact]
    public void SurfaceUsesExplicitBackgroundAndContentColor()
    {
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.ExplicitSurfaceScreen(Colors.HotPink, Colors.LimeGreen,
                context, changed, defaults));

        MauiNode surface = Assert.Single(applier.Root.Children);
        DrawModifierView background = Assert.IsType<DrawModifierView>(((ContentView)surface.View).Content);
        Label label = Assert.IsType<Label>(Assert.Single(surface.Children).Core);
        Assert.Equal(Colors.HotPink, Assert.IsType<BackgroundElement>(background.Element).Color);
        Assert.Equal(Colors.LimeGreen, label.TextColor);
    }

    [Fact]
    public void DisposableEffectReleasesPreviousKeyAndComposition()
    {
        List<string> events = new();
        SnapshotMutableState<int> key = Composables.CreateMutableState(1);
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent((context, changed, defaults) =>
            Builders.EffectScreen(key.Value, events, context, changed, defaults));
        Assert.Equal(new[] { "setup 1" }, events);

        key.Value = 2;
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal(new[] { "setup 1", "dispose 1", "setup 2" }, events);

        composition.Dispose();
        Assert.Equal(new[] { "setup 1", "dispose 1", "setup 2", "dispose 2" }, events);
    }

    [Fact]
    public void HoistedScrollStateSurvivesViewRecreation()
    {
        ScrollState state = new(42);
        MauiApplier first = new(new Grid());
        using (Composition<MauiNode> composition = new(first))
        {
            composition.SetContent((context, changed, defaults) =>
                Builders.ScrollScreen(state, "first", context, changed, defaults));
            MauiNode scroll = Assert.Single(first.Root.Children);
            Assert.IsType<ScrollView>(scroll.Core);
            Assert.Single(scroll.Children);
        }

        state.ScrollTo(84);
        MauiApplier second = new(new Grid());
        using Composition<MauiNode> reloaded = new(second);
        reloaded.SetContent((context, changed, defaults) =>
            Builders.ScrollScreen(state, "second", context, changed, defaults));
        Assert.IsType<ScrollView>(Assert.Single(second.Root.Children).Core);
        Assert.Equal(84, state.Offset);
    }
}
