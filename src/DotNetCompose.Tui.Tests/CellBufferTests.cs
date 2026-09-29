namespace DotNetCompose.Tui.Tests;

public sealed class CellBufferTests
{
    [Fact]
    public void WideCharactersReserveContinuationCellAndCombiningMarksStayTogether()
    {
        CellBuffer buffer = new(8, 1);
        buffer.Write(0, 0, "界e\u0301", new TuiStyle(), new TuiRect(0, 0, 8, 1));

        Assert.Equal("界", buffer[0, 0].Grapheme);
        Assert.Equal(2, buffer[0, 0].Width);
        Assert.True(buffer[1, 0].Continuation);
        Assert.Equal("e\u0301", buffer[2, 0].Grapheme);
    }

    [Fact]
    public void DiffWritesOnlyChangedRangeAfterFirstFrame()
    {
        CellBuffer first = new(5, 1);
        first.Write(0, 0, "hello", new TuiStyle(), new TuiRect(0, 0, 5, 1));
        CellBuffer second = new(5, 1);
        second.Write(0, 0, "hallo", new TuiStyle(), new TuiRect(0, 0, 5, 1));

        string diff = CellBufferDiff.Build(first, second);

        Assert.Contains("\u001b[1;2H", diff);
        Assert.Contains("a", diff);
        Assert.DoesNotContain("\u001b[2J", diff);
        Assert.DoesNotContain("hello", diff);
    }
}
