using System.Reflection;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace DotNetCompose.Maui.Tests;

[CollectionDefinition("MauiHotReload", DisableParallelization = true)]
public sealed class MauiHotReloadCollection
{
}

[Collection("MauiHotReload")]
public sealed partial class MauiHotReloadTests
{
    internal sealed class ViewModel : IDisposable
    {
        public string Text = "external";
        public bool Fail;
        public bool FailRelease;
        public int Releases;
        public int EffectDisposals;
        public bool Disposed;
        public readonly List<SnapshotMutableState<int>> Remembered = new();
        public readonly List<CancellationToken> Tokens = new();
        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed record Route(int Id);
    private sealed class Services : IServiceProvider
    {
        public object? GetService(Type type)
        {
            return null;
        }
    }

    private sealed class Cleanup(Action action) : IDisposable
    {
        public void Dispose()
        {
            action();
        }
    }

    [Composable]
    internal static void Screen(ViewModel model)
    {
        SnapshotMutableState<int> state = Composables.Remember(0, () => Composables.CreateMutableState(0));
        model.Remembered.Add(state);
        Composables.DisposableEffect(model, () => new Cleanup(() => model.EffectDisposals++));
        Composables.LaunchedEffect(model, token =>
        {
            model.Tokens.Add(token);
            return new ValueTask(Task.Delay(Timeout.Infinite, token));
        });
        MauiUi.NativeView(() => new Label(), label =>
        {
            if (model.Fail)
            {
                throw new InvalidOperationException("failed UI update");
            }
            label.Text = model.Text;
        }, onRelease: _ =>
        {
            model.Releases++;
            if (model.FailRelease)
            {
                throw new InvalidOperationException("failed UI release");
            }
        });
    }

    [Composable]
    internal static void Navigation(MauiNavigator navigator)
    {
        MauiUi.NavHost(navigator);
    }

    [Fact]
    public void ReloadRecreatesUiAndRememberAndRestartsEffectsWithSameExternalModel()
    {
        using Host fixture = new();
        ViewModel model = new();
        fixture.View.SetContent(model, Builders.Screen);
        fixture.Load();
        Grid root = Assert.IsType<Grid>(fixture.View.Content);
        object original = Assert.Single(root.Children);
        SnapshotMutableState<int> remembered = Assert.Single(model.Remembered);
        CancellationToken effect = Assert.Single(model.Tokens);
        remembered.Value = 42;
        model.Text = "preserved";
        object? recomposer = Field(fixture.View, "_recomposer");
        object? context = Field(fixture.View, "_context");

        fixture.View.Reload();

        Assert.Same(root, fixture.View.Content);
        Assert.NotSame(original, Assert.Single(root.Children));
        Assert.Equal("preserved", Assert.IsType<Label>(Assert.IsType<ContentView>(Assert.Single(root.Children)).Content).Text);
        Assert.NotSame(remembered, model.Remembered.Last());
        Assert.Equal(0, model.Remembered.Last().Value);
        Assert.Equal(42, remembered.Value);
        Assert.Same(recomposer, Field(fixture.View, "_recomposer"));
        Assert.Same(context, Field(fixture.View, "_context"));
        Assert.Equal(1, model.Releases);
        Assert.Equal(1, model.EffectDisposals);
        Assert.True(effect.IsCancellationRequested);
        Assert.Equal(2, model.Tokens.Count);
        Assert.False(model.Tokens.Last().IsCancellationRequested);
        Assert.False(model.Disposed);
        fixture.View.Dispose();
        Assert.Equal(2, model.Releases);
        Assert.Equal(2, model.EffectDisposals);
        Assert.All(model.Tokens, token => Assert.True(token.IsCancellationRequested));
        Assert.False(model.Disposed);
    }

    [Fact]
    public void ReloadPreservesNavigatorStackAndViewModels()
    {
        using Host fixture = new();
        List<ViewModel> models = new();
        NavGraph graph = NavGraph.Create(new Route(0), builder =>
            builder.Screen<Route, ViewModel>((route, _) =>
            {
                ViewModel model = new() { Text = route.Id.ToString() };
                models.Add(model);
                return model;
            }, Builders.Screen));
        using MauiNavigator navigator = new(graph, new Services());
        navigator.Push(new Route(1));
        fixture.View.SetContent(navigator, Builders.Navigation);
        fixture.Load();
        object entry = navigator.CurrentEntry;
        fixture.View.Reload();
        Assert.Same(entry, navigator.CurrentEntry);
        Assert.Equal(new Route(1), navigator.CurrentRoute);
        Assert.True(navigator.CanPop);
        Assert.Equal(2, models.Count);
        Assert.All(models, model => Assert.False(model.Disposed));
        Assert.Equal(2, models[1].Tokens.Count);
        Assert.True(navigator.Pop());
        fixture.View.Reload();
        Assert.Equal(new Route(0), navigator.CurrentRoute);
        Assert.False(navigator.CanPop);
        Assert.Equal(2, models.Count);
        Assert.False(models[0].Disposed);
        Assert.True(models[1].Disposed);
        fixture.View.Dispose();
    }

    [Fact]
    public void ManualFailureCleansUiAndAllowsRetry()
    {
        using Host fixture = new();
        ViewModel model = new();
        fixture.View.SetContent(model, Builders.Screen);
        fixture.Load();
        model.Fail = true;
        int events = 0;
        fixture.View.HotReloadFailed += (_, _) => events++;
        Assert.Throws<InvalidOperationException>(fixture.View.Reload);
        Assert.Empty(Assert.IsType<Grid>(fixture.View.Content).Children);
        Assert.Null(Field(fixture.View, "_composition"));
        Assert.Equal(0, events);
        model.Fail = false;
        fixture.View.Reload();
        Assert.Single(Assert.IsType<Grid>(fixture.View.Content).Children);
        Assert.False(model.Disposed);
    }

    [Fact]
    public void OldCompositionReleaseFailureLeavesHostReadyForRetry()
    {
        using Host fixture = new();
        ViewModel model = new();
        fixture.View.SetContent(model, Builders.Screen);
        fixture.Load();
        model.FailRelease = true;
        Assert.Throws<InvalidOperationException>(fixture.View.Reload);
        Assert.Null(Field(fixture.View, "_composition"));
        Assert.Empty(Assert.IsType<Grid>(fixture.View.Content).Children);
        Assert.True(Assert.Single(model.Tokens).IsCancellationRequested);
        model.FailRelease = false;
        fixture.View.Reload();
        Assert.Single(Assert.IsType<Grid>(fixture.View.Content).Children);
        Assert.Equal(2, model.Tokens.Count);
    }

    [Fact]
    public void AutomaticFailureReportsEventAndCanRetryAfterUnloadAndReload()
    {
        using Host fixture = new();
        ViewModel model = new();
        fixture.View.SetContent(model, Builders.Screen);
        fixture.Load();
        if (!CompositionHotReload.IsSupported)
        {
            Assert.Null(Field(fixture.View, "_hotReloadRegistration"));
            return;
        }
        int errors = 0;
        fixture.View.HotReloadFailed += (_, _) => throw new InvalidOperationException("handler");
        fixture.View.HotReloadFailed += (sender, args) =>
        {
            Assert.Same(fixture.View, sender);
            Assert.IsType<InvalidOperationException>(args.Exception);
            errors++;
        };
        model.Fail = true;
        Thread worker = new(Notify);
        worker.Start();
        worker.Join();
        Assert.Equal(0, errors);
        fixture.Dispatcher.Drain();
        Assert.Equal(1, errors);
        Assert.Empty(Assert.IsType<Grid>(fixture.View.Content).Children);
        model.Fail = false;
        Notify();
        fixture.Dispatcher.Drain();
        Assert.Single(Assert.IsType<Grid>(fixture.View.Content).Children);
        int created = model.Remembered.Count;
        Notify();
        fixture.Unload();
        fixture.Dispatcher.Drain();
        Assert.Equal(created, model.Remembered.Count);
        Assert.Null(Field(fixture.View, "_hotReloadRegistration"));
        fixture.Load();
        Assert.NotNull(Field(fixture.View, "_hotReloadRegistration"));
        Notify();
        fixture.View.Dispose();
        fixture.Dispatcher.Drain();
        Assert.Empty(Assert.IsType<Grid>(fixture.View.Content).Children);
    }

    [Fact]
    public void UnloadedReloadDoesNothingAndDisposedReloadThrows()
    {
        using MauiComposeView view = new();
        ViewModel model = new();
        view.SetContent(model, Builders.Screen);
        view.Reload();
        Assert.Empty(model.Remembered);
        view.Dispose();
        Assert.Throws<ObjectDisposedException>(view.Reload);
    }

    private static object? Field(object instance, string name)
    {
        return typeof(MauiComposeView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance);
    }

    private static void Notify()
    {
        typeof(CompositionHotReload).GetMethod("UpdateApplication", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { null });
    }

    private sealed class Host : IDisposable
    {
        private readonly IDispatcherProvider _previous = DispatcherProvider.Current;
        private readonly ContentPage _page;
        private readonly Window _window;
        public readonly TestDispatcher Dispatcher = new();
        public readonly MauiComposeView View;

        public Host()
        {
            DispatcherProvider.SetCurrent(Dispatcher);
            View = new MauiComposeView();
            _page = new ContentPage();
            _window = new Window(_page);
        }

        public void Load()
        {
            _page.Content = View;
            Assert.True(View.IsLoaded);
            SendLifecycle("SendLoaded");
        }

        public void Unload()
        {
            _page.Content = null;
            Assert.False(View.IsLoaded);
            SendLifecycle("SendUnloaded");
        }

        private void SendLifecycle(string name)
        {
            // The portable MAUI target has no platform view to raise these events.
            typeof(VisualElement).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(bool) }, null)!.Invoke(View, new object[] { false });
        }

        public void Dispose()
        {
            try
            {
                View.Dispose();
            }
            finally
            {
                DispatcherProvider.SetCurrent(_previous);
            }
            GC.KeepAlive(_window);
        }
    }

    private sealed class TestDispatcher : IDispatcher, IDispatcherProvider
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly Queue<Action> _queue = new();
        public bool IsDispatchRequired
        {
            get
            {
                return _thread != Environment.CurrentManagedThreadId;
            }
        }
        public IDispatcher? GetForCurrentThread()
        {
            return IsDispatchRequired ? null : this;
        }
        public bool Dispatch(Action action)
        {
            lock (_queue)
            {
                _queue.Enqueue(action);
            }
            return true;
        }
        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            throw new NotSupportedException();
        }
        public IDispatcherTimer CreateTimer()
        {
            throw new NotSupportedException();
        }
        public void Drain()
        {
            while (true)
            {
                Action action;
                lock (_queue)
                {
                    if (!_queue.TryDequeue(out action!))
                    {
                        return;
                    }
                }
                action();
            }
        }
    }
}
