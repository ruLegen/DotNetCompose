using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Maui;

/// <summary>Hoisted scroll offset that survives recreation of its MAUI ScrollView.</summary>
public sealed class ScrollState
{
    private readonly SnapshotMutableState<double> _offset;

    public ScrollState(double initialOffset = 0)
    {
        Check(initialOffset);
        _offset = Composables.CreateMutableState(initialOffset);
    }

    public double Offset
    {
        get
        {
            return _offset.Value;
        }
    }

    public void ScrollTo(double offset)
    {
        Check(offset);
        _offset.Value = offset;
    }

    private static void Check(double offset)
    {
        if (!double.IsFinite(offset) || offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }
}
