using DotNetCompose.Maui.Drawing;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace DotNetCompose.Maui.Modifiers;

internal interface IModifierView : IDisposable
{
    View View { get; }
    void Update(ModifierElement element);
    void DetachChild();
}

internal sealed class MeasureModifierView : Microsoft.Maui.Controls.Layout, IModifierView
{
    private ModifierElement _element;

    internal MeasureModifierView(View child, ModifierElement element)
    {
        _element = element;
        Add(child);
    }

    View IModifierView.View
    {
        get
        {
            return this;
        }
    }

    internal View Child
    {
        get
        {
            return (View)Children[0];
        }
    }

    internal ModifierElement Element
    {
        get
        {
            return _element;
        }
    }

    public void Update(ModifierElement element)
    {
        _element = element;
        InvalidateMeasure();
    }

    protected override ILayoutManager CreateLayoutManager()
    {
        return new MeasureModifierLayoutManager(this);
    }

    public void DetachChild()
    {
        RemoveAt(0);
    }

    public void Dispose()
    {
    }
}

internal sealed class MeasureModifierLayoutManager(MeasureModifierView layout) : LayoutManager(layout)
{
    public override Size Measure(double widthConstraint, double heightConstraint)
    {
        if (layout.Children.Count == 0)
        {
            return Size.Zero;
        }

        ModifierElement element = layout.Element;
        double childWidth = widthConstraint;
        double childHeight = heightConstraint;
        if (element is PaddingElement padding)
        {
            childWidth = Math.Max(0, widthConstraint - padding.Left - padding.Right);
            childHeight = Math.Max(0, heightConstraint - padding.Top - padding.Bottom);
        }
        else if (element is SizeElement size)
        {
            childWidth = Math.Min(widthConstraint, size.Width);
            childHeight = Math.Min(heightConstraint, size.Height);
        }

        Size desired = layout.Child.Measure(childWidth, childHeight);
        double width = desired.Width;
        double height = desired.Height;
        switch (element)
        {
            case PaddingElement outerPadding:
                width += outerPadding.Left + outerPadding.Right;
                height += outerPadding.Top + outerPadding.Bottom;
                break;
            case SizeElement size:
                width = size.Width;
                height = size.Height;
                break;
            case FillElement fill:
                if (fill.Width && double.IsFinite(widthConstraint))
                {
                    width = widthConstraint;
                }
                if (fill.Height && double.IsFinite(heightConstraint))
                {
                    height = heightConstraint;
                }
                break;
        }
        return new Size(Math.Min(width, widthConstraint), Math.Min(height, heightConstraint));
    }

    public override Size ArrangeChildren(Rect bounds)
    {
        if (layout.Children.Count == 0)
        {
            return bounds.Size;
        }

        Rect childBounds = bounds;
        if (layout.Element is PaddingElement padding)
        {
            childBounds = new Rect(bounds.X + padding.Left, bounds.Y + padding.Top,
                Math.Max(0, bounds.Width - padding.Left - padding.Right),
                Math.Max(0, bounds.Height - padding.Top - padding.Bottom));
        }
        else if (layout.Element is SizeElement size)
        {
            childBounds = new Rect(bounds.X, bounds.Y,
                Math.Min(bounds.Width, size.Width), Math.Min(bounds.Height, size.Height));
        }
        layout.Child.Arrange(childBounds);
        return bounds.Size;
    }
}

internal sealed class DrawModifierView : Microsoft.Maui.Controls.Layout, IModifierView
{
    private readonly GraphicsView _graphics;
    private readonly ObservedDrawable _drawable;
    private ModifierElement _element;

    internal DrawModifierView(View child, ModifierElement element)
    {
        _element = element;
        _graphics = new GraphicsView { InputTransparent = true };
        bool foreground = IsForeground(element);
        if (foreground)
        {
            Add(child);
            Add(_graphics);
        }
        else
        {
            Add(_graphics);
            Add(child);
        }
        _drawable = new ObservedDrawable(_graphics, Draw);
    }

    View IModifierView.View
    {
        get
        {
            return this;
        }
    }

    internal View Child
    {
        get
        {
            return (View)Children[IsForeground(_element) ? 0 : 1];
        }
    }

    internal ModifierElement Element
    {
        get
        {
            return _element;
        }
    }

    public void Update(ModifierElement element)
    {
        _element = element;
        _drawable.Update(Draw);
    }

    private void Draw(ICanvas canvas, RectF dirtyRect)
    {
        RectF bounds = new(0, 0, (float)Math.Max(0, Width), (float)Math.Max(0, Height));
        if (bounds.Width == 0 && bounds.Height == 0)
        {
            bounds = dirtyRect;
        }
        canvas.SaveState();
        try
        {
            switch (_element)
            {
                case BackgroundElement background:
                    canvas.FillColor = background.Color;
                    canvas.FillRectangle(bounds);
                    break;
                case BorderElement border:
                    canvas.StrokeColor = border.Color;
                    canvas.StrokeSize = (float)border.Thickness;
                    float inset = (float)border.Thickness / 2;
                    canvas.DrawRectangle(inset, inset,
                        Math.Max(0, bounds.Width - 2 * inset),
                        Math.Max(0, bounds.Height - 2 * inset));
                    break;
                case DrawBehindElement behind:
                    behind.Draw(new MauiDrawScope(canvas, bounds));
                    break;
                case DrawForegroundElement foreground:
                    foreground.Draw(new MauiDrawScope(canvas, bounds));
                    break;
            }
        }
        finally
        {
            canvas.RestoreState();
        }
    }

    private static bool IsForeground(ModifierElement element)
    {
        return element is BorderElement or DrawForegroundElement;
    }

    protected override ILayoutManager CreateLayoutManager()
    {
        return new DrawModifierLayoutManager(this);
    }

    public void DetachChild()
    {
        Remove(Child);
    }

    public void Dispose()
    {
        _drawable.Dispose();
    }
}

internal sealed class DrawModifierLayoutManager(DrawModifierView layout) : LayoutManager(layout)
{
    public override Size Measure(double widthConstraint, double heightConstraint)
    {
        return layout.Children.Count < 2 ? Size.Zero : layout.Child.Measure(widthConstraint, heightConstraint);
    }

    public override Size ArrangeChildren(Rect bounds)
    {
        if (layout.Children.Count == 2)
        {
            foreach (IView child in layout.Children)
            {
                child.Arrange(bounds);
            }
        }
        return bounds.Size;
    }
}

internal sealed class ClickableModifierView : Microsoft.Maui.Controls.Layout, IModifierView
{
#if WINDOWS
    private readonly Button _overlay;
    private readonly GraphicsView _feedback;
    private readonly ObservedDrawable _feedbackDrawable;
    private bool _focused;
    private bool _pressed;
#else
    private readonly TapGestureRecognizer _tap = new();
#endif
    private ClickableElement _element;

    internal ClickableModifierView(View child, ClickableElement element)
    {
        Add(child);
        _element = element;
#if WINDOWS
        _feedback = new GraphicsView { InputTransparent = true };
        Add(_feedback);
        _feedbackDrawable = new ObservedDrawable(_feedback, DrawFeedback);
        _overlay = new Button
        {
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Opacity = 0.01,
            Padding = 0
        };
        _overlay.Clicked += OnClicked;
        _overlay.Pressed += OnPressed;
        _overlay.Released += OnReleased;
        _overlay.Focused += OnFocused;
        _overlay.Unfocused += OnUnfocused;
        Add(_overlay);
#else
        _tap.Tapped += OnTapped;
        GestureRecognizers.Add(_tap);
#endif
        Update(element);
    }

    View IModifierView.View
    {
        get
        {
            return this;
        }
    }

    internal View Child
    {
        get
        {
            return (View)Children[0];
        }
    }

    internal ClickableElement Element
    {
        get
        {
            return _element;
        }
    }

    public void Update(ModifierElement element)
    {
        _element = (ClickableElement)element;
        InputTransparent = false;
        SemanticProperties.SetHint(this, _element.OnClickLabel);
#if WINDOWS
        _overlay.IsEnabled = _element.Enabled;
        _overlay.Text = _element.OnClickLabel ?? AccessibleText(Child) ?? "Activate";
#endif
        InvalidateMeasure();
    }

#if WINDOWS
    private void OnClicked(object? sender, EventArgs args)
    {
        Activate();
    }

    private void OnPressed(object? sender, EventArgs args)
    {
        _pressed = true;
        _feedbackDrawable.Update(DrawFeedback);
    }

    private void OnReleased(object? sender, EventArgs args)
    {
        _pressed = false;
        _feedbackDrawable.Update(DrawFeedback);
    }

    private void OnFocused(object? sender, FocusEventArgs args)
    {
        _focused = true;
        _feedbackDrawable.Update(DrawFeedback);
    }

    private void OnUnfocused(object? sender, FocusEventArgs args)
    {
        _focused = false;
        _feedbackDrawable.Update(DrawFeedback);
    }

    private void DrawFeedback(ICanvas canvas, RectF bounds)
    {
        if (_pressed)
        {
            canvas.FillColor = Colors.Black.WithAlpha(0.12f);
            canvas.FillRectangle(bounds);
        }

        if (_focused)
        {
            canvas.StrokeColor = Colors.DodgerBlue;
            canvas.StrokeSize = 2;
            canvas.DrawRectangle(1, 1, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2));
        }
    }

    internal void RefreshAccessibleName()
    {
        if (_element.OnClickLabel is null)
        {
            _overlay.Text = AccessibleText(Child) ?? "Activate";
        }
    }

    private static string? AccessibleText(View view)
    {
        if (view is Label label && !string.IsNullOrWhiteSpace(label.Text))
        {
            return label.Text;
        }

        if (view is ContentView content && content.Content is View contentChild)
        {
            return AccessibleText(contentChild);
        }

        if (view is Microsoft.Maui.Controls.Layout layout)
        {
            foreach (View child in layout.Children.OfType<View>())
            {
                string? text = AccessibleText(child);
                if (text is not null)
                {
                    return text;
                }
            }
        }

        return null;
    }
#else
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        Activate();
    }
#endif

    internal void Activate()
    {
        if (_element.Enabled)
        {
            _element.OnClick();
        }
    }

    protected override ILayoutManager CreateLayoutManager()
    {
        return new ClickableModifierLayoutManager(this);
    }
    public void DetachChild()
    {
        RemoveAt(0);
    }

    public void Dispose()
    {
#if WINDOWS
        _overlay.Clicked -= OnClicked;
        _overlay.Pressed -= OnPressed;
        _overlay.Released -= OnReleased;
        _overlay.Focused -= OnFocused;
        _overlay.Unfocused -= OnUnfocused;
        _feedbackDrawable.Dispose();
#else
        _tap.Tapped -= OnTapped;
        GestureRecognizers.Remove(_tap);
#endif
    }
}

internal sealed class ClickableModifierLayoutManager(ClickableModifierView layout) : LayoutManager(layout)
{
    public override Size Measure(double widthConstraint, double heightConstraint)
    {
        return layout.Children.Count == 0 ? Size.Zero : layout.Child.Measure(widthConstraint, heightConstraint);
    }

    public override Size ArrangeChildren(Rect bounds)
    {
        if (layout.Children.Count > 0)
        {
            layout.Child.Arrange(bounds);
        }
#if WINDOWS
        for (int index = 1; index < layout.Children.Count; index++)
        {
            layout.Children[index].Arrange(bounds);
        }
        layout.RefreshAccessibleName();
#endif
        return bounds.Size;
    }
}
