namespace DotNetCompose.Maui.Sample.Screens.EditorGraph.Preview;

public sealed class PreviewViewModel(MauiNavigator navigator, EditorGraphViewModel graph)
{
    public EditorGraphViewModel Graph { get; } = graph;

    public void Back()
    {
        navigator.Pop();
    }
}
