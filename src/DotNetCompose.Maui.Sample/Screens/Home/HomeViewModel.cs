using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using DotNetCompose.Maui.Sample.Screens.EditorGraph;

namespace DotNetCompose.Maui.Sample.Screens.Home;

public sealed class HomeViewModel(MauiNavigator navigator)
{
    public SnapshotMutableState<string> ButtonText { get; } = Composables.CreateMutableState("OpenEditor");

    private Random _random = new(); 
    public void OpenEditor()
    {
        navigator.Push(new EditorGraphRoute());
    }

    public void ChangeButtonText()
    {
        ButtonText.Value = $"Random {_random.Next()}";
    }
}
