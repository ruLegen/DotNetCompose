using System.Reflection;
using DotNetCompose.Maui.Drawing;
using DotNetCompose.Maui.Modifiers;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DotNetCompose.Maui.Tests;

public sealed partial class MauiAdapterTests
{
    private static readonly SnapshotMutableState<int> Value = Composables.CreateMutableState(0);
    private static readonly SnapshotMutableState<IReadOnlyList<int>> Items =
        Composables.CreateMutableState<IReadOnlyList<int>>(Array.Empty<int>());
    private static readonly SnapshotMutableState<int> Counter = Composables.CreateMutableState(0);
    private static Label? _created;
    private static int _released;

    [Composable]
    internal static void NativeContent()
    {
        int value = Value.Value;
        MauiUi.NativeView(
            factory: () => _created = new Label(),
            update: label => label.Text = value.ToString(),
            onRelease: _ => _released++);
    }

    [Composable]
    internal static void KeyedContent()
    {
        IReadOnlyList<int> items = Items.Value;
        MauiUi.Column(content: () =>
        {
            foreach (int item in items)
            {
                int id = item;
                Composables.Key(id, () => MauiUi.NativeView(() => new Label { Text = id.ToString() }));
            }
        });
    }

    [Composable]
    internal static void MixedContent()
    {
        int count = Counter.Value;
        MauiUi.Column(content: () =>
        {
            MauiUi.NativeView(() => new Label(), label => label.Text = count.ToString());
            MauiUi.NativeView(() =>
            {
                Button button = new();
                button.Clicked += (_, _) => Counter.Value++;
                return button;
            });
            MauiUi.Canvas(Modifier.Empty.Size(100, 20 + 10 * count), _ => { });
        });
    }

    [Fact]
    public void ApplierInsertsMovesRemovesAndReleasesViews()
    {
        Grid root = new();
        MauiApplier applier = new(root);
        int releases = 0;
        MauiNode a = new(new Label { Text = "a" });
        MauiNode b = new(new Label { Text = "b" });
        MauiNode c = new(new Label { Text = "c" });
        a.UpdateNative<Label>(null, Modifier.Empty, _ => releases++);
        b.UpdateNative<Label>(null, Modifier.Empty, _ => releases++);
        c.UpdateNative<Label>(null, Modifier.Empty, _ => releases++);

        applier.InsertBottomUp(0, a);
        applier.InsertBottomUp(1, b);
        applier.InsertBottomUp(2, c);
        Assert.Equal(new[] { a.View, b.View, c.View }, root.Children.Cast<View>());

        applier.Move(0, 3, 1);
        Assert.Equal(new[] { b, c, a }, applier.Root.Children);
        Assert.Equal(new[] { b.View, c.View, a.View }, root.Children.Cast<View>());
        applier.Remove(1, 1);
        Assert.Equal(new[] { b.View, a.View }, root.Children.Cast<View>());
        Assert.Equal(1, releases);
        applier.Clear();
        Assert.Empty(root.Children);
        Assert.Equal(3, releases);
    }

    [Fact]
    public void NativeViewKeepsInstanceAcrossRecompositionAndReleasesOnDispose()
    {
        Value.Value = 1;
        _created = null;
        _released = 0;
        MauiApplier applier = new(new Grid());
        using (Composition<MauiNode> composition = new(applier))
        {
            composition.SetContent(Builders.NativeContent);
            Label first = Assert.IsType<Label>(Assert.Single(applier.Root.Children).Core);
            Assert.Same(_created, first);
            Assert.Equal("1", first.Text);
            Value.Value = 2;
            Assert.True(composition.Recompose());
            composition.ApplyChanges();
            Assert.Same(first, Assert.Single(applier.Root.Children).Core);
            Assert.Equal("2", first.Text);
        }
        Assert.Equal(1, _released);
    }

    [Fact]
    public void KeyedReorderingRetainsNativeViews()
    {
        Items.Value = new[] { 1, 2, 3 };
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent(Builders.KeyedContent);
        MauiNode column = Assert.Single(applier.Root.Children);
        Dictionary<string, View> original = column.Children.ToDictionary(
            node => ((Label)node.Core).Text, node => node.Core);

        Items.Value = new[] { 3, 1, 2 };
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Equal(new[] { "3", "1", "2" }, column.Children.Select(node => ((Label)node.Core).Text));
        foreach (MauiNode node in column.Children)
            Assert.Same(original[((Label)node.Core).Text], node.Core);
    }

    [Fact]
    public void NativeButtonClickRecomposesLabelAndCanvasSize()
    {
        Counter.Value = 0;
        MauiApplier applier = new(new Grid());
        using Composition<MauiNode> composition = new(applier);
        composition.SetContent(Builders.MixedContent);
        MauiNode column = Assert.Single(applier.Root.Children);
        Label label = Assert.IsType<Label>(column.Children[0].Core);
        Button button = Assert.IsType<Button>(column.Children[1].Core);
        CanvasLayout canvas = Assert.IsType<CanvasLayout>(column.Children[2].Core);
        Assert.Equal(new Size(100, 20), new CanvasLayoutManager(canvas).Measure(500, 500));

        button.SendClicked();
        Assert.Equal(1, Counter.Value);
        Assert.True(composition.Recompose());
        composition.ApplyChanges();
        Assert.Same(label, column.Children[0].Core);
        Assert.Same(button, column.Children[1].Core);
        Assert.Same(canvas, column.Children[2].Core);
        Assert.Equal("1", label.Text);
        Assert.Equal(new Size(100, 30), new CanvasLayoutManager(canvas).Measure(500, 500));
    }

    [Fact]
    public void ModifierOrderChangesWrapperNestingAndSize()
    {
        MauiNode outerPadding = new(new Label());
        outerPadding.ApplyModifier(Modifier.Empty.Padding(10).Background(Colors.Red));
        MauiNode innerPadding = new(new Label());
        innerPadding.ApplyModifier(Modifier.Empty.Background(Colors.Red).Padding(10));

        MeasureModifierView padding = Assert.IsType<MeasureModifierView>(((ContentView)outerPadding.View).Content);
        DrawModifierView backgroundInside = Assert.IsType<DrawModifierView>(padding.Child);
        Assert.IsType<DrawModifierView>(((ContentView)innerPadding.View).Content);
        Assert.IsType<MeasureModifierView>(((DrawModifierView)((ContentView)innerPadding.View).Content).Child);
        Assert.Same(outerPadding.Core, backgroundInside.Child);

        MauiNode sized = new(new Label());
        sized.ApplyModifier(Modifier.Empty.Size(80, 40));
        Assert.Equal(LayoutOptions.Start, ((ContentView)sized.View).HorizontalOptions);
        MeasureModifierView size = Assert.IsType<MeasureModifierView>(((ContentView)sized.View).Content);
        Assert.Equal(new Size(80, 40), new MeasureModifierLayoutManager(size).Measure(500, 500));

        sized.ApplyModifier(Modifier.Empty.Fill().Size(80, 40));
        Assert.Equal(LayoutOptions.Fill, ((ContentView)sized.View).HorizontalOptions);
    }

    [Fact]
    public void ChangingDrawModifierKindRebuildsLayerOrder()
    {
        Label label = new();
        MauiNode node = new(label);
        node.ApplyModifier(Modifier.Empty.Background(Colors.Red));
        DrawModifierView behind = Assert.IsType<DrawModifierView>(((ContentView)node.View).Content);
        Assert.IsType<GraphicsView>(behind.Children[0]);
        Assert.Same(label, behind.Children[1]);

        node.ApplyModifier(Modifier.Empty.Border(Colors.Blue));
        DrawModifierView foreground = Assert.IsType<DrawModifierView>(((ContentView)node.View).Content);
        Assert.Same(label, foreground.Children[0]);
        Assert.IsType<GraphicsView>(foreground.Children[1]);
    }

    [Fact]
    public void NativeViewRejectsDrawWithContent()
    {
        MauiNode node = new(new Label());
        NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
            node.ApplyModifier(Modifier.Empty.DrawWithContent(_ => { })));
        Assert.Contains("MauiUi.Canvas", error.Message);
    }

    [Fact]
    public void HostCanAcceptContentBeforeItIsAttachedToAWindow()
    {
        using MauiComposeView host = new();
        host.SetContent(Builders.NativeContent);
        Assert.Empty(Assert.IsType<Grid>(host.Content).Children);
    }

    [Fact]
    public void CanvasDrawWithContentCanSkipOrRepeatAndObservesSnapshotReads()
    {
        SnapshotMutableState<int> color = Composables.CreateMutableState(0);
        List<string> calls = new();
        CanvasLayout layout = new();
        RecordingCanvas canvas = DispatchProxy.Create<ICanvas, RecordingCanvas>() as RecordingCanvas
            ?? throw new InvalidOperationException();
        layout.Update(Modifier.Empty.Size(60, 30).DrawWithContent(_ => calls.Add("skip")),
            _ => calls.Add("content"));
        layout.CanvasView.Drawable.Draw((ICanvas)canvas, new RectF(0, 0, 60, 30));
        Assert.Equal(new[] { "skip" }, calls);

        calls.Clear();
        layout.Update(Modifier.Empty.Size(60, 30)
                .DrawBehind(_ => calls.Add("behind"))
                .DrawWithContent(scope =>
                {
                    calls.Add("before");
                    scope.DrawContent();
                    scope.DrawContent();
                    calls.Add("after");
                })
                .DrawForeground(_ => calls.Add("foreground")),
            scope => { _ = color.Value; calls.Add("content"); });
        layout.CanvasView.Drawable.Draw((ICanvas)canvas, new RectF(0, 0, 60, 30));
        Assert.Equal(new[] { "behind", "before", "content", "foreground", "content", "foreground", "after" }, calls);
        Assert.Equal(canvas.Calls.Count(name => name == "SaveState"), canvas.Calls.Count(name => name == "RestoreState"));

        int before = layout.InvalidationCount;
        color.Value = 1;
        Snapshot.SendApplyNotifications();
        Assert.True(layout.InvalidationCount > before);
        layout.Dispose();
    }

    [Fact]
    public void CanvasPaddingAndBackgroundOrderChangesPaintedBounds()
    {
        CanvasLayout layout = new();
        RecordingCanvas canvas = (RecordingCanvas)DispatchProxy.Create<ICanvas, RecordingCanvas>();
        RectF bounds = new(0, 0, 100, 50);

        layout.Update(Modifier.Empty.Padding(10).Background(Colors.Red), _ => { });
        layout.CanvasView.Drawable.Draw((ICanvas)canvas, bounds);
        Assert.Equal(new RectF(0, 0, 80, 30), Assert.Single(canvas.FilledRectangles));
        Assert.Contains("Translate", canvas.Calls);

        canvas.FilledRectangles.Clear();
        layout.Update(Modifier.Empty.Background(Colors.Red).Padding(10), _ => { });
        layout.CanvasView.Drawable.Draw((ICanvas)canvas, bounds);
        Assert.Equal(bounds, Assert.Single(canvas.FilledRectangles));
        layout.Dispose();
    }

    [Fact]
    public void CanvasSizeAndPaddingOrderChangesMeasuredSize()
    {
        CanvasLayout layout = new();
        CanvasLayoutManager manager = new(layout);
        layout.Update(Modifier.Empty.Padding(10).Size(50, 20), _ => { });
        Assert.Equal(new Size(70, 40), manager.Measure(500, 500));
        layout.Update(Modifier.Empty.Size(50, 20).Padding(10), _ => { });
        Assert.Equal(new Size(50, 20), manager.Measure(500, 500));
        layout.Dispose();
    }

    public class RecordingCanvas : DispatchProxy
    {
        public List<string> Calls { get; } = new();
        public List<RectF> FilledRectangles { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
                return null;
            Calls.Add(targetMethod.Name);
            if (targetMethod.Name == "FillRectangle" && args is [float x, float y, float width, float height])
                FilledRectangles.Add(new RectF(x, y, width, height));
            Type result = targetMethod.ReturnType;
            return result == typeof(void) ? null : result.IsValueType ? Activator.CreateInstance(result) : null;
        }
    }
}
