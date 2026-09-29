using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using DotNetCompose.Maui.Sample.Services;

namespace DotNetCompose.Maui.Sample.Screens.EditorGraph;

public sealed class EditorGraphViewModel(SampleGreetingService greeting)
{
    public SnapshotMutableState<string> Name { get; } = Composables.CreateMutableState("Name");
    public SnapshotMutableState<bool> DrawDecoration { get; } = Composables.CreateMutableState(false);
    public SnapshotMutableState<int> Counter { get; } = Composables.CreateMutableState(0);

    public string Greeting
    {
        get
        {
            return greeting.Greeting;
        }
    }

    internal void AddToCounter(int v)
    {
        int next = Counter.Value + v;
        Counter.Value = next;
    }
}
