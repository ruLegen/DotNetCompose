using DotNetCompose.Runtime;
using Microsoft.Maui.Controls;

namespace DotNetCompose.Maui;

public static partial class MauiUi
{
    /// <summary>Embeds one MAUI view or a MAUI-managed view hierarchy as one composition node.</summary>
    [Composable]
    public static void NativeView<TView>(
        Func<TView> factory,
        Action<TView>? update = null,
        Modifier? modifier = null,
        Action<TView>? onRelease = null) where TView : View
    {
        ArgumentNullException.ThrowIfNull(factory);
        Composables.ComposeNode(
            () => new MauiNode(factory() ?? throw new InvalidOperationException("NativeView factory returned null.")),
            node => node.UpdateNative(update, modifier ?? Modifier.Empty, onRelease));
    }

    [Composable(ComposableMode.Inline)]
    public static void Row(
        double spacing = 0,
        Modifier? modifier = null,
        [Composable] Action content = null!)
    {
        Composables.ComposeNode(
            () => new MauiNode(new HorizontalStackLayout(), acceptsChildren: true),
            node =>
            {
                ((HorizontalStackLayout)node.Core).Spacing = spacing;
                node.ApplyModifier(modifier ?? Modifier.Empty);
            },
            content);
    }

    [Composable(ComposableMode.Inline)]
    public static void Column(
        double spacing = 0,
        Modifier? modifier = null,
        [Composable] Action content = null!)
    {
        Composables.ComposeNode(
            () => new MauiNode(new VerticalStackLayout(), acceptsChildren: true),
            node =>
            {
                ((VerticalStackLayout)node.Core).Spacing = spacing;
                node.ApplyModifier(modifier ?? Modifier.Empty);
            },
            content);
    }

    [Composable(ComposableMode.Inline)]
    public static void Box(Modifier? modifier = null, [Composable] Action content = null!)
    {
        Composables.ComposeNode(
            () => new MauiNode(new Grid(), acceptsChildren: true),
            node => node.ApplyModifier(modifier ?? Modifier.Empty),
            content);
    }

    /// <summary>Draws into one MAUI GraphicsView. DrawWithContent wraps this node's onDraw callback.</summary>
    [Composable]
    public static void Canvas(Modifier? modifier, Action<MauiDrawScope> onDraw)
    {
        ArgumentNullException.ThrowIfNull(onDraw);
        Composables.ComposeNode(
            () => new MauiNode(new CanvasLayout()),
            node => node.UpdateCanvas(modifier ?? Modifier.Empty, onDraw));
    }
}
