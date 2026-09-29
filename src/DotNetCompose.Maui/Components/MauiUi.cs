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
        Modifier modifier = default,
        Action<TView>? onRelease = null) where TView : View
    {
        ArgumentNullException.ThrowIfNull(factory);
        Composables.ComposeNode(
            () => new MauiNode(factory() ?? throw new InvalidOperationException("NativeView factory returned null.")),
            node => node.UpdateNative(update, modifier, onRelease));
    }

    [Composable(ComposableMode.Inline)]
    public static void Row(
        Modifier modifier = default,
        double spacing = 0,
        MainAxisArrangement arrangement = MainAxisArrangement.Start,
        CrossAxisAlignment verticalAlignment = CrossAxisAlignment.Center,
        [Composable] Action content = null!)
    {
        Composables.ComposeNode(
            () => new MauiNode(new Layout.ComposeLinearLayout(horizontal: true), acceptsChildren: true),
            node =>
            {
                ((Layout.ComposeLinearLayout)node.Core).Update(spacing, arrangement, verticalAlignment);
                node.ApplyModifier(modifier);
            },
            content);
    }

    [Composable(ComposableMode.Inline)]
    public static void Column(
        Modifier modifier = default,
        double spacing = 0,
        MainAxisArrangement arrangement = MainAxisArrangement.Start,
        CrossAxisAlignment horizontalAlignment = CrossAxisAlignment.Start,
        [Composable] Action content = null!)
    {
        Composables.ComposeNode(
            () => new MauiNode(new Layout.ComposeLinearLayout(horizontal: false), acceptsChildren: true),
            node =>
            {
                ((Layout.ComposeLinearLayout)node.Core).Update(spacing, arrangement, horizontalAlignment);
                node.ApplyModifier(modifier);
            },
            content);
    }

    [Composable(ComposableMode.Inline)]
    public static void Box(
        Modifier modifier = default,
        BoxAlignment alignment = BoxAlignment.TopStart,
        bool propagateMinConstraints = false,
        [Composable] Action content = null!)
    {
        Composables.ComposeNode(
            () => new MauiNode(new Layout.ComposeBoxLayout(), acceptsChildren: true),
            node =>
            {
                ((Layout.ComposeBoxLayout)node.Core).Update(alignment, propagateMinConstraints);
                node.ApplyModifier(modifier);
            },
            content);
    }

    /// <summary>Draws into one MAUI GraphicsView. DrawWithContent wraps this node's onDraw callback.</summary>
    [Composable]
    public static void Canvas(Modifier modifier, Action<MauiDrawScope> onDraw)
    {
        ArgumentNullException.ThrowIfNull(onDraw);
        Composables.ComposeNode(
            () => new MauiNode(new Drawing.CanvasLayout()),
            node => node.UpdateCanvas(modifier, onDraw));
    }
}
