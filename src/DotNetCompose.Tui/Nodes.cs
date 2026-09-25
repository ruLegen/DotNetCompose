namespace DotNetCompose.Tui;

internal enum TuiInvalidationKind { Paint, Layout }

public abstract class TuiNode
{
    private readonly List<TuiNode> _children = new();

    public IReadOnlyList<TuiNode> Children => _children;
    public TuiNode? Parent { get; private set; }
    public TuiRect Bounds { get; private set; }
    public TuiSize DesiredSize { get; protected set; }
    public TuiLayout Layout { get; set; } = TuiLayout.Auto;
    public TuiTheme? Theme { get; set; }
    public bool Enabled { get; set; } = true;
    public virtual bool Focusable => false;

    internal void Insert(int index, TuiNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent != null) throw new InvalidOperationException("A TUI node already has a parent.");
        child.Parent = this;
        _children.Insert(index, child);
        Invalidate(TuiInvalidationKind.Layout);
    }

    internal void Remove(int index, int count)
    {
        for (int offset = 0; offset < count; offset++) _children[index + offset].Parent = null;
        _children.RemoveRange(index, count);
        Invalidate(TuiInvalidationKind.Layout);
    }

    internal void Move(int from, int to, int count)
    {
        List<TuiNode> moved = _children.GetRange(from, count);
        _children.RemoveRange(from, count);
        _children.InsertRange(to > from ? to - count : to, moved);
        Invalidate(TuiInvalidationKind.Layout);
    }

    internal void ClearChildren()
    {
        foreach (TuiNode child in _children) child.Parent = null;
        _children.Clear();
        Invalidate(TuiInvalidationKind.Layout);
    }

    public virtual TuiSize Measure(TuiConstraints constraints)
    {
        int width = 0;
        int height = 0;
        foreach (TuiNode child in _children)
        {
            TuiSize desired = child.Measure(new TuiConstraints(0, constraints.MaxWidth, 0, constraints.MaxHeight));
            width = Math.Max(width, desired.Width);
            height += desired.Height;
        }
        DesiredSize = constraints.Constrain(ResolveDesired(new TuiSize(width, height), constraints));
        return DesiredSize;
    }

    public virtual void Arrange(TuiRect bounds)
    {
        Bounds = bounds;
        foreach (TuiNode child in _children) child.Arrange(bounds);
    }

    public virtual void Render(CellBuffer buffer, TuiRect clip, FocusManager focus, TuiTheme theme)
    {
        TuiRect childClip = clip.Intersect(Bounds);
        foreach (TuiNode child in _children) child.Render(buffer, childClip, focus, theme);
    }

    public virtual bool HandleKey(TuiKeyEvent key) => false;

    internal virtual void Invalidate(TuiInvalidationKind kind, TuiRect? region = null)
    {
        Parent?.Invalidate(kind, region ?? Bounds);
    }

    protected void SetBounds(TuiRect bounds) => Bounds = bounds;

    protected TuiSize ResolveDesired(TuiSize content, TuiConstraints constraints)
    {
        int width = Layout.Width.Kind switch
        {
            TuiLengthKind.Cells => Layout.Width.Value,
            TuiLengthKind.Fill => constraints.MaxWidth,
            _ => content.Width
        };
        int height = Layout.Height.Kind switch
        {
            TuiLengthKind.Cells => Layout.Height.Value,
            TuiLengthKind.Fill => constraints.MaxHeight,
            _ => content.Height
        };
        return new TuiSize(width, height);
    }
}

internal sealed class TuiRootNode : StackNode
{
    internal TuiRootNode() : base(StackOrientation.Vertical) { Layout = TuiLayout.Fill; }
}

public enum StackOrientation { Horizontal, Vertical }

public class StackNode : TuiNode
{
    public StackNode(StackOrientation orientation) => Orientation = orientation;
    public StackOrientation Orientation { get; }
    public int Gap { get; set; }

    public override TuiSize Measure(TuiConstraints constraints)
    {
        int main = 0;
        int cross = 0;
        foreach (TuiNode child in Children)
        {
            TuiSize desired = child.Measure(new TuiConstraints(0, constraints.MaxWidth, 0, constraints.MaxHeight));
            main += Orientation == StackOrientation.Horizontal ? desired.Width : desired.Height;
            cross = Math.Max(cross, Orientation == StackOrientation.Horizontal ? desired.Height : desired.Width);
        }
        if (Children.Count > 1) main += Gap * (Children.Count - 1);
        TuiSize content = Orientation == StackOrientation.Horizontal ? new TuiSize(main, cross) : new TuiSize(cross, main);
        DesiredSize = constraints.Constrain(ResolveDesired(content, constraints));
        return DesiredSize;
    }

    public override void Arrange(TuiRect bounds)
    {
        SetBounds(bounds);
        if (Children.Count == 0) return;
        int availableMain = (Orientation == StackOrientation.Horizontal ? bounds.Width : bounds.Height) - Gap * (Children.Count - 1);
        int fixedMain = 0;
        int fillWeight = 0;
        foreach (TuiNode child in Children)
        {
            TuiLength length = Orientation == StackOrientation.Horizontal ? child.Layout.Width : child.Layout.Height;
            if (length.Kind == TuiLengthKind.Fill) fillWeight += length.Value;
            else fixedMain += length.Kind == TuiLengthKind.Cells
                ? length.Value
                : Orientation == StackOrientation.Horizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
        }
        int remaining = Math.Max(0, availableMain - fixedMain);
        int cursor = Orientation == StackOrientation.Horizontal ? bounds.X : bounds.Y;
        int remainingWeight = fillWeight;
        foreach (TuiNode child in Children)
        {
            TuiLength mainLength = Orientation == StackOrientation.Horizontal ? child.Layout.Width : child.Layout.Height;
            int main = mainLength.Kind switch
            {
                TuiLengthKind.Cells => mainLength.Value,
                TuiLengthKind.Fill when remainingWeight > 0 => remaining * mainLength.Value / remainingWeight,
                _ => Orientation == StackOrientation.Horizontal ? child.DesiredSize.Width : child.DesiredSize.Height
            };
            if (mainLength.Kind == TuiLengthKind.Fill)
            {
                remaining -= main;
                remainingWeight -= mainLength.Value;
            }
            TuiLength crossLength = Orientation == StackOrientation.Horizontal ? child.Layout.Height : child.Layout.Width;
            int crossAvailable = Orientation == StackOrientation.Horizontal ? bounds.Height : bounds.Width;
            int cross = crossLength.Kind switch
            {
                TuiLengthKind.Cells => Math.Min(crossAvailable, crossLength.Value),
                TuiLengthKind.Auto => Math.Min(crossAvailable, Orientation == StackOrientation.Horizontal ? child.DesiredSize.Height : child.DesiredSize.Width),
                _ => crossAvailable
            };
            TuiRect childBounds = Orientation == StackOrientation.Horizontal
                ? new TuiRect(cursor, bounds.Y, Math.Max(0, main), Math.Max(0, cross))
                : new TuiRect(bounds.X, cursor, Math.Max(0, cross), Math.Max(0, main));
            child.Arrange(childBounds);
            cursor += main + Gap;
        }
    }
}

public sealed class TextNode : TuiNode
{
    public string Text { get; set; } = string.Empty;
    public TuiStyle? Style { get; set; }

    public override TuiSize Measure(TuiConstraints constraints)
    {
        string[] lines = Text.Replace("\r", string.Empty).Split('\n');
        int width = lines.Length == 0 ? 0 : lines.Max(UnicodeWidth.Measure);
        DesiredSize = constraints.Constrain(ResolveDesired(new TuiSize(width, Math.Max(1, lines.Length)), constraints));
        return DesiredSize;
    }

    public override void Render(CellBuffer buffer, TuiRect clip, FocusManager focus, TuiTheme theme)
    {
        theme = Theme ?? theme;
        string[] lines = Text.Replace("\r", string.Empty).Split('\n');
        TuiRect ownClip = clip.Intersect(Bounds);
        for (int line = 0; line < lines.Length && line < Bounds.Height; line++)
            buffer.Write(Bounds.X, Bounds.Y + line, lines[line], Style ?? theme.Text, ownClip);
    }
}

public sealed class SpacerNode : TuiNode
{
    public override TuiSize Measure(TuiConstraints constraints)
    {
        DesiredSize = constraints.Constrain(ResolveDesired(TuiSize.Empty, constraints));
        return DesiredSize;
    }
}

public sealed class BorderNode : TuiNode
{
    public string? Title { get; set; }

    public override TuiSize Measure(TuiConstraints constraints)
    {
        TuiNode? child = Children.FirstOrDefault();
        TuiSize content = child?.Measure(new TuiConstraints(0, Math.Max(0, constraints.MaxWidth - 2), 0, Math.Max(0, constraints.MaxHeight - 2))) ?? TuiSize.Empty;
        DesiredSize = constraints.Constrain(ResolveDesired(new TuiSize(content.Width + 2, content.Height + 2), constraints));
        return DesiredSize;
    }

    public override void Arrange(TuiRect bounds)
    {
        SetBounds(bounds);
        if (Children.FirstOrDefault() is { } child)
            child.Arrange(new TuiRect(bounds.X + 1, bounds.Y + 1, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - 2)));
    }

    public override void Render(CellBuffer buffer, TuiRect clip, FocusManager focus, TuiTheme theme)
    {
        theme = Theme ?? theme;
        TuiRect own = clip.Intersect(Bounds);
        if (!own.IsEmpty && Bounds.Width >= 2 && Bounds.Height >= 2)
        {
            for (int x = Bounds.X + 1; x < Bounds.Right - 1; x++)
            {
                buffer.Write(x, Bounds.Y, "─", theme.Border, own);
                buffer.Write(x, Bounds.Bottom - 1, "─", theme.Border, own);
            }
            for (int y = Bounds.Y + 1; y < Bounds.Bottom - 1; y++)
            {
                buffer.Write(Bounds.X, y, "│", theme.Border, own);
                buffer.Write(Bounds.Right - 1, y, "│", theme.Border, own);
            }
            buffer.Write(Bounds.X, Bounds.Y, "┌", theme.Border, own);
            buffer.Write(Bounds.Right - 1, Bounds.Y, "┐", theme.Border, own);
            buffer.Write(Bounds.X, Bounds.Bottom - 1, "└", theme.Border, own);
            buffer.Write(Bounds.Right - 1, Bounds.Bottom - 1, "┘", theme.Border, own);
            if (!string.IsNullOrEmpty(Title)) buffer.Write(Bounds.X + 2, Bounds.Y, $" {Title} ", theme.Border, own);
        }
        base.Render(buffer, own, focus, theme);
    }
}

public sealed class ButtonNode : TuiNode
{
    public string Label { get; set; } = string.Empty;
    public Action? OnClick { get; set; }
    public override bool Focusable => Enabled;

    public override TuiSize Measure(TuiConstraints constraints)
    {
        DesiredSize = constraints.Constrain(ResolveDesired(new TuiSize(UnicodeWidth.Measure(Label) + 4, 1), constraints));
        return DesiredSize;
    }

    public override void Render(CellBuffer buffer, TuiRect clip, FocusManager focus, TuiTheme theme)
    {
        theme = Theme ?? theme;
        TuiStyle style = !Enabled ? theme.Disabled : focus.IsFocused(this) ? theme.Focused : theme.Text;
        buffer.Write(Bounds.X, Bounds.Y, $"[ {Label} ]", style, clip.Intersect(Bounds));
    }

    public override bool HandleKey(TuiKeyEvent key)
    {
        if (!Enabled || key.Key is not (ConsoleKey.Enter or ConsoleKey.Spacebar)) return false;
        OnClick?.Invoke();
        return true;
    }
}

public sealed class TextFieldNode : TuiNode
{
    private string _value = string.Empty;
    private int _cursor;
    public string Value { get => _value; set { _value = value ?? string.Empty; _cursor = Math.Clamp(_cursor, 0, _value.Length); } }
    public string Placeholder { get; set; } = string.Empty;
    public Action<string>? OnValueChanged { get; set; }
    public override bool Focusable => Enabled;

    public override TuiSize Measure(TuiConstraints constraints)
    {
        int width = Math.Max(10, Math.Max(UnicodeWidth.Measure(Value), UnicodeWidth.Measure(Placeholder)) + 2);
        DesiredSize = constraints.Constrain(ResolveDesired(new TuiSize(width, 1), constraints));
        return DesiredSize;
    }

    public override void Render(CellBuffer buffer, TuiRect clip, FocusManager focus, TuiTheme theme)
    {
        theme = Theme ?? theme;
        bool focused = focus.IsFocused(this);
        string shown = Value.Length == 0 ? Placeholder : Value;
        TuiStyle style = !Enabled ? theme.Disabled : focused ? theme.Focused : theme.Text;
        string framed = "[" + shown.PadRight(Math.Max(0, Bounds.Width - 2)) + "]";
        buffer.Write(Bounds.X, Bounds.Y, framed, style, clip.Intersect(Bounds));
        if (focused && Value.Length > 0)
        {
            int cursorX = Bounds.X + 1 + UnicodeWidth.Measure(Value[..Math.Min(_cursor, Value.Length)]);
            if (cursorX < Bounds.Right - 1)
            {
                string grapheme = _cursor < Value.Length ? Value[_cursor].ToString() : " ";
                buffer.Write(cursorX, Bounds.Y, grapheme, new TuiStyle(style.Foreground, style.Background, style.Attributes | TuiAttributes.Reverse), clip.Intersect(Bounds));
            }
        }
    }

    public override bool HandleKey(TuiKeyEvent key)
    {
        if (!Enabled) return false;
        string next = Value;
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow: _cursor = Math.Max(0, _cursor - 1); return true;
            case ConsoleKey.RightArrow: _cursor = Math.Min(Value.Length, _cursor + 1); return true;
            case ConsoleKey.Home: _cursor = 0; return true;
            case ConsoleKey.End: _cursor = Value.Length; return true;
            case ConsoleKey.Backspace when _cursor > 0:
                next = Value.Remove(_cursor - 1, 1); _cursor--; break;
            case ConsoleKey.Delete when _cursor < Value.Length:
                next = Value.Remove(_cursor, 1); break;
            default:
                if (key.Character == '\0' || char.IsControl(key.Character)) return false;
                next = Value.Insert(_cursor, key.Character.ToString()); _cursor++; break;
        }
        Value = next;
        OnValueChanged?.Invoke(next);
        return true;
    }
}

public sealed class ListNode : TuiNode
{
    private int _scrollOffset;
    public int SelectedIndex { get; set; }
    public Action<int>? OnSelectionChanged { get; set; }
    public override bool Focusable => Enabled;

    public override TuiSize Measure(TuiConstraints constraints)
    {
        int width = 0;
        int height = 0;
        foreach (TuiNode child in Children)
        {
            TuiSize desired = child.Measure(new TuiConstraints(0, Math.Max(0, constraints.MaxWidth - 2), 0, constraints.MaxHeight));
            width = Math.Max(width, desired.Width + 2);
            height += Math.Max(1, desired.Height);
        }
        DesiredSize = constraints.Constrain(ResolveDesired(new TuiSize(width, height), constraints));
        return DesiredSize;
    }

    public override void Arrange(TuiRect bounds)
    {
        SetBounds(bounds);
        SelectedIndex = Children.Count == 0 ? 0 : Math.Clamp(SelectedIndex, 0, Children.Count - 1);
        if (SelectedIndex < _scrollOffset) _scrollOffset = SelectedIndex;
        if (SelectedIndex >= _scrollOffset + bounds.Height) _scrollOffset = SelectedIndex - bounds.Height + 1;
        for (int index = 0; index < Children.Count; index++)
            Children[index].Arrange(new TuiRect(bounds.X + 2, bounds.Y + index - _scrollOffset, Math.Max(0, bounds.Width - 2), 1));
    }

    public override void Render(CellBuffer buffer, TuiRect clip, FocusManager focus, TuiTheme theme)
    {
        theme = Theme ?? theme;
        TuiRect own = clip.Intersect(Bounds);
        for (int index = 0; index < Children.Count; index++)
        {
            TuiNode child = Children[index];
            if (child.Bounds.Y < own.Y || child.Bounds.Y >= own.Bottom) continue;
            string marker = index == SelectedIndex ? "> " : "  ";
            TuiStyle markerStyle = focus.IsFocused(this) && index == SelectedIndex ? theme.Selection : theme.Text;
            buffer.Write(Bounds.X, child.Bounds.Y, marker, markerStyle, own);
            child.Render(buffer, own, focus, theme);
        }
    }

    public override bool HandleKey(TuiKeyEvent key)
    {
        if (!Enabled || Children.Count == 0) return false;
        int next = SelectedIndex;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: next--; break;
            case ConsoleKey.DownArrow: next++; break;
            case ConsoleKey.PageUp: next -= Math.Max(1, Bounds.Height); break;
            case ConsoleKey.PageDown: next += Math.Max(1, Bounds.Height); break;
            case ConsoleKey.Home: next = 0; break;
            case ConsoleKey.End: next = Children.Count - 1; break;
            default: return false;
        }
        next = Math.Clamp(next, 0, Children.Count - 1);
        if (next != SelectedIndex)
        {
            SelectedIndex = next;
            OnSelectionChanged?.Invoke(next);
        }
        return true;
    }
}

public sealed class FocusManager
{
    private readonly List<TuiNode> _order = new();
    private TuiNode? _focused;

    public TuiNode? Focused => _focused;
    public bool IsFocused(TuiNode node) => ReferenceEquals(_focused, node);

    internal void Rebuild(TuiNode root)
    {
        _order.Clear();
        AddFocusable(root);
        if (_focused == null || !_order.Contains(_focused)) _focused = _order.FirstOrDefault();
    }

    public bool HandleKey(TuiKeyEvent key)
    {
        if (key.Key == ConsoleKey.Tab)
        {
            Move(key.Shift ? -1 : 1);
            return true;
        }
        return _focused?.HandleKey(key) == true;
    }

    public void Move(int delta)
    {
        if (_order.Count == 0) { _focused = null; return; }
        int index = _focused == null ? 0 : _order.IndexOf(_focused);
        _focused = _order[(index + delta % _order.Count + _order.Count) % _order.Count];
    }

    private void AddFocusable(TuiNode node)
    {
        if (node.Focusable && node.Enabled) _order.Add(node);
        foreach (TuiNode child in node.Children) AddFocusable(child);
    }
}
