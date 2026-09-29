using DotNetCompose.Maui.Navigation;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Effects;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Maui;

public sealed class NavContext
{
    internal NavContext(MauiNavigator navigator, IServiceProvider services)
    {
        Navigator = navigator;
        Services = services;
    }

    public MauiNavigator Navigator { get; }
    public IServiceProvider Services { get; }

    public TViewModel GetGraphViewModel<TViewModel>() where TViewModel : class
    {
        return Navigator.GetGraphViewModel<TViewModel>();
    }
}

public sealed class MauiNavigator : IDisposable
{
    private readonly NavGraph _graph;
    private readonly IServiceProvider _services;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<NavStackEntry> _stack = new();
    private readonly Dictionary<Guid, List<NavStackEntry>> _pendingDisposals = new();
    private readonly SnapshotMutableState<ScreenEntry> _current;
    private readonly SnapshotMutableState<bool> _canPop;
    private Guid? _visibleEntry;
    private bool _disposed;

    public MauiNavigator(NavGraph graph, IServiceProvider services)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        PushCore(graph.StartRoute);
        _current = Composables.CreateMutableState(TopScreen());
        _canPop = Composables.CreateMutableState(false);
    }

    public object CurrentRoute
    {
        get
        {
            return _current.Value.Route;
        }
    }

    public bool CanPop
    {
        get
        {
            return _canPop.Value;
        }
    }

    internal ScreenEntry CurrentEntry
    {
        get
        {
            return _current.Value;
        }
    }

    public void Push<TRoute>(TRoute route) where TRoute : notnull
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(route);
        PushCore(route);
        Publish();
    }

    public bool Pop()
    {
        VerifyAccess();
        if (!CanPop)
        {
            return false;
        }

        ScreenEntry removed = TopScreen();
        List<NavStackEntry> popped = new() { removed };
        _stack.RemoveAt(_stack.Count - 1);
        while (_stack.Count > 0 && _stack[^1] is GraphEntry graphEntry)
        {
            popped.Add(graphEntry);
            _stack.RemoveAt(_stack.Count - 1);
        }

        if (_visibleEntry == removed.Id)
        {
            _pendingDisposals.Add(removed.Id, popped);
        }
        else
        {
            DisposeEntries(popped);
        }

        Publish();
        return true;
    }

    internal TViewModel GetGraphViewModel<TViewModel>() where TViewModel : class
    {
        for (int index = _stack.Count - 1; index >= 0; index--)
        {
            if (_stack[index] is GraphEntry graph && graph.ViewModel is TViewModel viewModel)
            {
                return viewModel;
            }
        }

        throw new InvalidOperationException($"No active graph owns {typeof(TViewModel).Name}.");
    }

    private void PushCore(object route)
    {
        int originalCount = _stack.Count;
        try
        {
            NavDestination destination = _graph.Destination(route.GetType());
            EnsureParent(destination.Parent);
            NavContext context = new(this, _services);
            if (destination is ScreenDestination screen)
            {
                _stack.Add(new ScreenEntry(route, screen, screen.CreateViewModel(route, context)));
            }
            else if (destination is GraphDestination graph)
            {
                object start = graph.StartRoute(route);
                NavDestination startDestination = _graph.Destination(start.GetType());
                if (!ReferenceEquals(startDestination.Parent, graph))
                {
                    throw new InvalidOperationException("A graph start route must be its direct child.");
                }

                _stack.Add(new GraphEntry(route, graph, graph.CreateViewModel(route, context)));
                PushCore(start);
            }
        }
        catch
        {
            List<NavStackEntry> created = _stack.Skip(originalCount).Reverse().ToList();
            _stack.RemoveRange(originalCount, _stack.Count - originalCount);
            DisposeEntries(created);
            throw;
        }
    }

    private void EnsureParent(GraphDestination? parent)
    {
        if (parent is null)
        {
            return;
        }

        if (!_stack.Any(entry => entry is GraphEntry graph && ReferenceEquals(graph.Destination, parent)))
        {
            throw new InvalidOperationException($"Enter graph {parent.RouteType.Name} before navigating to its child.");
        }
    }

    private ScreenEntry TopScreen()
    {
        if (_stack.Count == 0 || _stack[^1] is not ScreenEntry screen)
        {
            throw new InvalidOperationException("The navigation graph did not produce a screen.");
        }

        return screen;
    }

    private void Publish()
    {
        _current.Value = TopScreen();
        _canPop.Value = _stack.Count(entry => entry is ScreenEntry) > 1;
    }

    internal void MarkVisible(Guid entryId)
    {
        _visibleEntry = entryId;
    }

    internal void MarkHidden(Guid entryId)
    {
        if (_visibleEntry == entryId)
        {
            _visibleEntry = null;
        }

        if (_pendingDisposals.Remove(entryId, out List<NavStackEntry>? pending))
        {
            DisposeEntries(pending);
        }
    }

    private static void DisposeEntries(IEnumerable<NavStackEntry> entries)
    {
        foreach (NavStackEntry entry in entries)
        {
            if (entry.ViewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private void VerifyAccess()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MauiNavigator));
        }

        if (_thread != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Navigate on the MAUI UI thread.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        VerifyAccess();
        if (_visibleEntry is not null)
        {
            throw new InvalidOperationException("Dispose the NavHost composition before its navigator.");
        }

        _disposed = true;
        foreach (List<NavStackEntry> pending in _pendingDisposals.Values)
        {
            DisposeEntries(pending);
        }

        _pendingDisposals.Clear();
        DisposeEntries(_stack.AsEnumerable().Reverse());
        _stack.Clear();
    }

    internal abstract class NavStackEntry(object route, object viewModel)
    {
        internal object Route { get; } = route;
        internal object ViewModel { get; } = viewModel;
    }

    internal sealed class GraphEntry(object route, GraphDestination destination, object viewModel)
        : NavStackEntry(route, viewModel)
    {
        internal GraphDestination Destination { get; } = destination;
    }

    internal sealed class ScreenEntry(object route, ScreenDestination destination, object viewModel)
        : NavStackEntry(route, viewModel)
    {
        internal Guid Id { get; } = Guid.NewGuid();

        internal void Render(IComposerContext context)
        {
            destination.Render(ViewModel, context);
        }
    }

    internal sealed class VisualLifetime(MauiNavigator navigator, Guid entryId) : IRememberObserver
    {
        public void OnRemembered()
        {
            navigator.MarkVisible(entryId);
        }

        public void OnForgotten()
        {
            navigator.MarkHidden(entryId);
        }

        public void OnAbandoned()
        {
        }
    }
}
