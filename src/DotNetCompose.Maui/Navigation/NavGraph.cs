using DotNetCompose.Maui.Navigation;
using DotNetCompose.Runtime;

namespace DotNetCompose.Maui;

public sealed class NavGraph
{
    private readonly Dictionary<Type, NavDestination> _destinations = new();

    private NavGraph(object startRoute)
    {
        StartRoute = startRoute;
    }

    internal object StartRoute { get; }

    internal NavDestination Destination(Type routeType)
    {
        if (!_destinations.TryGetValue(routeType, out NavDestination? destination))
        {
            throw new InvalidOperationException($"Route {routeType.Name} is not registered in the navigation graph.");
        }

        return destination;
    }

    internal void Add(NavDestination destination)
    {
        if (!_destinations.TryAdd(destination.RouteType, destination))
        {
            throw new InvalidOperationException($"Route {destination.RouteType.Name} is registered more than once.");
        }
    }

    public static NavGraph Create<TRoute>(TRoute startRoute, Action<NavGraphBuilder> configure)
        where TRoute : notnull
    {
        ArgumentNullException.ThrowIfNull(startRoute);
        ArgumentNullException.ThrowIfNull(configure);
        NavGraph graph = new(startRoute);
        configure(new NavGraphBuilder(graph, null));
        NavDestination start = graph.Destination(startRoute.GetType());
        if (start.Parent is not null)
        {
            throw new InvalidOperationException("The root start route must be registered in the root graph.");
        }

        return graph;
    }
}

public sealed class NavGraphBuilder
{
    private readonly NavGraph _graph;
    private readonly GraphDestination? _parent;

    internal NavGraphBuilder(NavGraph graph, GraphDestination? parent)
    {
        _graph = graph;
        _parent = parent;
    }

    public void Screen<TRoute, TViewModel>(
        Func<TRoute, NavContext, TViewModel> createViewModel,
        ComposableAction<TViewModel> content)
        where TRoute : notnull
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(createViewModel);
        ArgumentNullException.ThrowIfNull(content);
        _graph.Add(new ScreenDestination<TRoute, TViewModel>(_parent, createViewModel, content));
    }

    public void Graph<TRoute, TViewModel>(
        Func<TRoute, object> start,
        Func<TRoute, NavContext, TViewModel> createViewModel,
        Action<NavGraphBuilder> content)
        where TRoute : notnull
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(createViewModel);
        ArgumentNullException.ThrowIfNull(content);

        GraphDestination destination = new GraphDestination<TRoute, TViewModel>(
            _parent, route => start((TRoute)route),
            (route, context) => createViewModel((TRoute)route, context));
        _graph.Add(destination);
        content(new NavGraphBuilder(_graph, destination));
    }
}

