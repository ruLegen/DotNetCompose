namespace DotNetCompose.Tui.Tests;

public sealed class LayoutAndInputTests
{
    [Fact]
    public void RowDistributesRemainingCellsByFillWeight()
    {
        StackNode row = new(StackOrientation.Horizontal) { Gap = 1 };
        row.Insert(0, new SpacerNode { Layout = new TuiLayout(TuiLength.Cells(3), TuiLength.Fill()) });
        row.Insert(1, new SpacerNode { Layout = new TuiLayout(TuiLength.Fill(1), TuiLength.Fill()) });
        row.Insert(2, new SpacerNode { Layout = new TuiLayout(TuiLength.Fill(2), TuiLength.Fill()) });

        row.Measure(TuiConstraints.Tight(new TuiSize(20, 2)));
        row.Arrange(new TuiRect(0, 0, 20, 2));

        Assert.Equal(3, row.Children[0].Bounds.Width);
        Assert.Equal(5, row.Children[1].Bounds.Width);
        Assert.Equal(10, row.Children[2].Bounds.Width);
    }

    [Fact]
    public void FocusButtonTextFieldAndListHandleKeyboard()
    {
        StackNode root = new(StackOrientation.Vertical);
        ButtonNode button = new();
        int clicks = 0;
        button.OnClick = () => clicks++;
        TextFieldNode field = new() { Value = "a" };
        string edited = string.Empty;
        field.OnValueChanged = value => edited = value;
        ListNode list = new();
        list.Insert(0, new TextNode { Text = "one" });
        list.Insert(1, new TextNode { Text = "two" });
        root.Insert(0, button); root.Insert(1, field); root.Insert(2, list);
        root.Measure(TuiConstraints.Tight(new TuiSize(30, 6)));
        root.Arrange(new TuiRect(0, 0, 30, 6));
        FocusManager focus = new();
        focus.Rebuild(root);

        Assert.True(focus.HandleKey(new TuiKeyEvent(ConsoleKey.Enter, '\r', 0)));
        Assert.Equal(1, clicks);
        focus.HandleKey(new TuiKeyEvent(ConsoleKey.Tab, '\t', 0));
        focus.HandleKey(new TuiKeyEvent(ConsoleKey.B, 'b', 0));
        Assert.Equal("ba", edited);
        focus.HandleKey(new TuiKeyEvent(ConsoleKey.Tab, '\t', 0));
        focus.HandleKey(new TuiKeyEvent(ConsoleKey.DownArrow, '\0', 0));
        Assert.Equal(1, list.SelectedIndex);
    }
}
