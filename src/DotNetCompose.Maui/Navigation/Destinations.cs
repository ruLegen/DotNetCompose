using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Maui.Navigation;

internal abstract class NavDestination(Type routeType, GraphDestination? parent)
{
    internal Type RouteType { get; } = routeType;
    internal GraphDestination? Parent { get; } = parent;
}

internal abstract class GraphDestination(Type routeType, GraphDestination? parent,
    Func<object, object> startRoute, Func<object, NavContext, object> createViewModel)
    : NavDestination(routeType, parent)
{
    internal object StartRoute(object route)
    {
        return startRoute(route) ?? throw new InvalidOperationException("Graph start route factory returned null.");
    }

    internal object CreateViewModel(object route, NavContext context)
    {
        return createViewModel(route, context)
            ?? throw new InvalidOperationException("Graph ViewModel factory returned null.");
    }
}

internal sealed class GraphDestination<TRoute, TViewModel>(GraphDestination? parent,
    Func<object, object> startRoute, Func<object, NavContext, object> createViewModel)
    : GraphDestination(typeof(TRoute), parent, startRoute, createViewModel)
    where TRoute : notnull
    where TViewModel : class
{
}

internal abstract class ScreenDestination(Type routeType, GraphDestination? parent)
    : NavDestination(routeType, parent)
{
    internal abstract object CreateViewModel(object route, NavContext context);
    internal abstract void Render(object viewModel, IComposerContext context);
}

internal sealed class ScreenDestination<TRoute, TViewModel>(GraphDestination? parent,
    Func<TRoute, NavContext, TViewModel> createViewModel,
    ComposableAction<TViewModel> content)
    : ScreenDestination(typeof(TRoute), parent)
    where TRoute : notnull
    where TViewModel : class
{
    internal override object CreateViewModel(object route, NavContext context)
    {
        return createViewModel((TRoute)route, context)
            ?? throw new InvalidOperationException("Screen ViewModel factory returned null.");
    }

    internal override void Render(object viewModel, IComposerContext context)
    {
        content((TViewModel)viewModel, context, default, default);
    }
}
