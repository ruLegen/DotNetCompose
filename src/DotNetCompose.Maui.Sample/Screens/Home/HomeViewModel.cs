using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using DotNetCompose.Maui.Sample.Screens.EditorGraph;

namespace DotNetCompose.Maui.Sample.Screens.Home;

public sealed partial class HomeViewModel(MauiNavigator navigator)
{
    public SnapshotMutableState<string> ButtonText { get; } = Composables.CreateMutableState("OpenEditor");
    public SnapshotMutableState<bool> IsInited { get; } = Composables.CreateMutableState(false);

    private Random _random = new(); 
    public void OpenEditor()
    {
        navigator.Push(new EditorGraphRoute());
    }

    public void ChangeButtonText()
    {
        ButtonText.Value = $"Random {_random.Next()}";
    }

    public async Task Init(CancellationToken cancellationToken)
    {
        if (IsInited.Value)
            return;
        await Task.Delay(2000,cancellationToken);
        IsInited.Value = true;
    }

    [Composable(ComposableMode.Inline)]
    public void ComposableTest()
    {
    }
}
