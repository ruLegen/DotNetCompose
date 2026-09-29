using DotNetCompose.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace DotNetCompose.Maui.Layout;

internal sealed class ComposeLinearLayout(bool horizontal) : Microsoft.Maui.Controls.Layout
{
    internal bool Horizontal { get; } = horizontal;
    internal double Spacing { get; set; }
    internal MainAxisArrangement Arrangement { get; set; }
    internal CrossAxisAlignment CrossAlignment { get; set; }

    internal void Update(double spacing, MainAxisArrangement arrangement, CrossAxisAlignment crossAlignment)
    {
        if (!double.IsFinite(spacing) || spacing < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spacing));
        }
        Spacing = spacing;
        Arrangement = arrangement;
        CrossAlignment = crossAlignment;
        InvalidateMeasure();
    }

    protected override ILayoutManager CreateLayoutManager()
    {
        return new ComposeLinearLayoutManager(this);
    }
}

internal sealed class ComposeLinearLayoutManager(ComposeLinearLayout layout) : LayoutManager(layout)
{
    public override Size Measure(double widthConstraint, double heightConstraint)
    {
        double main = 0;
        double cross = 0;
        double totalSpacing = Math.Max(0, layout.Children.Count - 1) * layout.Spacing;
        foreach (IView child in layout.Children)
        {
            double remainingMain = layout.Horizontal ? widthConstraint : heightConstraint;
            if (double.IsFinite(remainingMain))
            {
                remainingMain = Math.Max(0, remainingMain - main - totalSpacing);
            }
            Size size = layout.Horizontal
                ? child.Measure(remainingMain, heightConstraint)
                : child.Measure(widthConstraint, remainingMain);
            main += layout.Horizontal ? size.Width : size.Height;
            cross = Math.Max(cross, layout.Horizontal ? size.Height : size.Width);
        }
        main += totalSpacing;
        return layout.Horizontal
            ? new Size(Math.Min(main, widthConstraint), Math.Min(cross, heightConstraint))
            : new Size(Math.Min(cross, widthConstraint), Math.Min(main, heightConstraint));
    }

    public override Size ArrangeChildren(Rect bounds)
    {
        int count = layout.Children.Count;
        if (count == 0)
        {
            return bounds.Size;
        }

        double availableMain = layout.Horizontal ? bounds.Width : bounds.Height;
        double availableCross = layout.Horizontal ? bounds.Height : bounds.Width;
        double usedMain = (count - 1) * layout.Spacing;
        for (int index = 0; index < count; index++)
        {
            Size desiredSize = layout.Children[index].DesiredSize;
            usedMain += layout.Horizontal ? desiredSize.Width : desiredSize.Height;
        }

        double extra = Math.Max(0, availableMain - usedMain);
        double gap = layout.Spacing;
        double position = layout.Arrangement switch
        {
            MainAxisArrangement.Center => extra / 2,
            MainAxisArrangement.End => extra,
            MainAxisArrangement.SpaceAround => extra / (count * 2),
            MainAxisArrangement.SpaceEvenly => extra / (count + 1),
            _ => 0
        };
        if (layout.Arrangement == MainAxisArrangement.SpaceBetween && count > 1)
        {
            gap += extra / (count - 1);
        }
        else if (layout.Arrangement == MainAxisArrangement.SpaceAround)
        {
            gap += extra / count;
        }
        else if (layout.Arrangement == MainAxisArrangement.SpaceEvenly)
        {
            gap += extra / (count + 1);
        }

        for (int index = 0; index < count; index++)
        {
            Size size = layout.Children[index].DesiredSize;
            double mainSize = layout.Horizontal ? size.Width : size.Height;
            double crossSize = layout.CrossAlignment == CrossAxisAlignment.Stretch
                ? availableCross : Math.Min(availableCross, layout.Horizontal ? size.Height : size.Width);
            double crossPosition = layout.CrossAlignment switch
            {
                CrossAxisAlignment.Center => (availableCross - crossSize) / 2,
                CrossAxisAlignment.End => availableCross - crossSize,
                _ => 0
            };
            Rect childBounds = layout.Horizontal
                ? new Rect(bounds.X + position, bounds.Y + crossPosition, mainSize, crossSize)
                : new Rect(bounds.X + crossPosition, bounds.Y + position, crossSize, mainSize);
            layout.Children[index].Arrange(childBounds);
            position += mainSize + gap;
        }
        return bounds.Size;
    }
}

internal sealed class ComposeBoxLayout : Microsoft.Maui.Controls.Layout
{
    internal BoxAlignment Alignment { get; set; }
    internal bool PropagateMinConstraints { get; set; }

    internal void Update(BoxAlignment alignment, bool propagateMinConstraints)
    {
        Alignment = alignment;
        PropagateMinConstraints = propagateMinConstraints;
        InvalidateMeasure();
    }

    protected override ILayoutManager CreateLayoutManager()
    {
        return new ComposeBoxLayoutManager(this);
    }
}

internal sealed class ComposeBoxLayoutManager(ComposeBoxLayout layout) : LayoutManager(layout)
{
    public override Size Measure(double widthConstraint, double heightConstraint)
    {
        double width = 0;
        double height = 0;
        foreach (IView child in layout.Children)
        {
            Size size = child.Measure(widthConstraint, heightConstraint);
            width = Math.Max(width, size.Width);
            height = Math.Max(height, size.Height);
        }
        return new Size(Math.Min(width, widthConstraint), Math.Min(height, heightConstraint));
    }

    public override Size ArrangeChildren(Rect bounds)
    {
        foreach (IView child in layout.Children)
        {
            if (layout.PropagateMinConstraints)
            {
                child.Arrange(bounds);
                continue;
            }

            Size size = child.DesiredSize;
            double width = Math.Min(size.Width, bounds.Width);
            double height = Math.Min(size.Height, bounds.Height);
            double x = layout.Alignment switch
            {
                BoxAlignment.TopCenter or BoxAlignment.Center or BoxAlignment.BottomCenter => (bounds.Width - width) / 2,
                BoxAlignment.TopEnd or BoxAlignment.CenterEnd or BoxAlignment.BottomEnd => bounds.Width - width,
                _ => 0
            };
            double y = layout.Alignment switch
            {
                BoxAlignment.CenterStart or BoxAlignment.Center or BoxAlignment.CenterEnd => (bounds.Height - height) / 2,
                BoxAlignment.BottomStart or BoxAlignment.BottomCenter or BoxAlignment.BottomEnd => bounds.Height - height,
                _ => 0
            };
            child.Arrange(new Rect(bounds.X + x, bounds.Y + y, width, height));
        }
        return bounds.Size;
    }
}
