using DotNetCompose.Maui.Sample.Screens.EditorGraph.Preview;

namespace DotNetCompose.Maui.Sample.Screens.EditorGraph.Editor;

public sealed class EditorViewModel(MauiNavigator navigator, EditorGraphViewModel graph)
{
    public EditorGraphViewModel Graph { get; } = graph;

    public void ShowPreview()
    {
        navigator.Push(new PreviewRoute());
    }

    public void Back()
    {
        navigator.Pop();
    }
}
