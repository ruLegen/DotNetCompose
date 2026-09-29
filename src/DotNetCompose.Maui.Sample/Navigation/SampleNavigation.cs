using DotNetCompose.Maui.Sample.Screens;
using DotNetCompose.Maui.Sample.Screens.EditorGraph;
using DotNetCompose.Maui.Sample.Screens.EditorGraph.Editor;
using DotNetCompose.Maui.Sample.Screens.EditorGraph.Preview;
using DotNetCompose.Maui.Sample.Screens.Home;
using DotNetCompose.Maui.Sample.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetCompose.Maui.Sample.Navigation;

internal static class SampleNavigation
{
    internal static NavGraph CreateGraph()
    {
        return NavGraph.Create(new HomeRoute(), graph =>
        {
            graph.Screen<HomeRoute, HomeViewModel>(
                (_, context) => new HomeViewModel(context.Navigator),
                HomeScreen.Builders.Content);
            graph.Graph<EditorGraphRoute, EditorGraphViewModel>(
                _ => new EditorRoute(),
                (_, context) => new EditorGraphViewModel(
                    context.Services.GetRequiredService<SampleGreetingService>()),
                editor =>
                {
                    editor.Screen<EditorRoute, EditorViewModel>(
                        (_, context) => new EditorViewModel(
                            context.Navigator, context.GetGraphViewModel<EditorGraphViewModel>()),
                        EditorScreen.Builders.Content);
                    editor.Screen<PreviewRoute, PreviewViewModel>(
                        (_, context) => new PreviewViewModel(
                            context.Navigator, context.GetGraphViewModel<EditorGraphViewModel>()),
                        PreviewScreen.Builders.Content);
                });
        });
    }
}
