using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public sealed class AsyncCompositionTests
{
    internal sealed class Activity(bool active = true) : ITaskActivity
    {
        private EventHandler? _changed;
        public bool IsActive { get; private set; } = active;
        public int Listeners { get; private set; }
        public event EventHandler? Changed
        {
            add { _changed += value; Listeners++; }
            remove { _changed -= value; Listeners--; }
        }
        internal void Set(bool value)
        {
            if (IsActive == value) return;
            IsActive = value;
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal sealed class Pump : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly SynchronizationContext? _previous = Current;
        internal Pump() => SetSynchronizationContext(this);
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue(() => callback(state));
        internal void Drain()
        {
            int remaining = 1000;
            while (_queue.TryDequeue(out Action? action))
            {
                Assert.True(remaining-- > 0, "The UI queue did not become idle.");
                action();
            }
        }
        internal void Until(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                Drain();
                if (DateTime.UtcNow >= deadline)
                    Assert.Fail("The asynchronous operation did not finish.");
                Thread.Yield();
            }
            Drain();
        }
        public void Dispose() => SetSynchronizationContext(_previous);
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly Pump Context = new();
        internal readonly Recomposer Recomposer;
        internal readonly Composition<CompositionTests.Node> Composition;
        internal readonly List<Exception> Errors = new();
        internal Fixture()
        {
            Recomposer = new Recomposer(Context);
            Recomposer.Error += (_, args) => Errors.Add(args.Exception);
            Composition = new Composition<CompositionTests.Node>(new CompositionTests.Applier(), Recomposer);
        }
        public void Dispose()
        {
            Composition.Dispose();
            Context.Drain();
            Recomposer.Dispose();
            Context.Dispose();
        }
    }

    [Fact]
    public void AsyncFactoriesAndSubscriptionsStartOnlyAfterApply()
    {
        using Fixture fixture = new();
        Activity activity = new();
        int started = 0;
        fixture.Composition.ComposeContent((context, _, _) =>
            Composables.Builders.RememberAsync(1, activity, _ => { started++; return Task.FromResult(42); }, context));
        Assert.Equal(0, started);
        Assert.Equal(0, activity.Listeners);
        fixture.Composition.DiscardChanges();
        Assert.Equal(0, started);
        Assert.Equal(0, activity.Listeners);
        fixture.Composition.SetContent((context, _, _) =>
            Composables.Builders.RememberAsync(1, activity, _ => { started++; return Task.FromResult(42); }, context));
        Assert.Equal(1, started);
        Assert.Equal(1, activity.Listeners);
        fixture.Composition.SetContent((_, _, _) => { });
        Assert.Equal(0, activity.Listeners);
    }

    [Fact]
    public void ChangedKeyResetsStateAndLateCancelledResultCannotOverwriteNewResult()
    {
        using Fixture fixture = new();
        TaskCompletionSource<string> old = new();
        IState<AsyncState<string>>? state = null;
        int calls = 0;
        CancellationToken oldToken = default;
        fixture.Composition.SetContent((context, _, _) => state = Composables.Builders.RememberAsync(1,
            token => { calls++; oldToken = token; return old.Task; }, context));
        Assert.True(state!.Value.IsLoading);
        fixture.Composition.SetContent((context, _, _) => state = Composables.Builders.RememberAsync(2,
            _ => { calls++; return Task.FromResult("new"); }, context));
        Assert.True(oldToken.IsCancellationRequested);
        Assert.Equal("new", state.Value.Value);
        old.SetResult("old");
        fixture.Context.Drain();
        Assert.Equal("new", state.Value.Value);
        Assert.Equal(2, calls);
        fixture.Composition.SetContent((context, _, _) => state = Composables.Builders.RememberAsync(2,
            _ => { calls++; return Task.FromResult("unexpected"); }, context));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DiscardedKeyAndFactoryChangesDoNotAffectCommittedRequest()
    {
        using Fixture fixture = new();
        Activity activity = new();
        IState<AsyncState<int>>? state = null;
        fixture.Composition.SetContent((context, _, _) => state = Composables.Builders.RememberAsync(1, activity,
            _ => Task.FromResult(10), context));
        fixture.Composition.ComposeContent((context, _, _) => state = Composables.Builders.RememberAsync(2, activity,
            _ => Task.FromResult(20), context));
        Assert.False(state!.Value.HasValue);
        fixture.Composition.DiscardChanges();
        Assert.Equal(10, state.Value.Value);
        activity.Set(false);
        activity.Set(true);
        fixture.Context.Drain();
        Assert.Equal(10, state.Value.Value);
    }

    [Fact]
    public void SameKeyResumeKeepsValueAndUsesLatestAppliedFactory()
    {
        using Fixture fixture = new();
        Activity activity = new();
        IState<AsyncState<int>>? state = null;
        TaskCompletionSource<int> refreshed = new();
        fixture.Composition.SetContent((context, _, _) => state = Composables.Builders.RememberAsync(1, activity,
            _ => Task.FromResult(10), context));
        fixture.Composition.SetContent((context, _, _) => state = Composables.Builders.RememberAsync(1, activity,
            _ => refreshed.Task, context));
        Assert.Equal(10, state!.Value.Value);
        activity.Set(false);
        Assert.False(state.Value.IsRunning);
        activity.Set(true);
        Assert.True(state.Value.IsLoading);
        Assert.Equal(10, state.Value.Value);
        refreshed.SetResult(11);
        fixture.Context.Drain();
        Assert.Equal(11, state.Value.Value);
    }

    [Fact]
    public void NullEmptySequenceAndFailuresAreRepresentedWithoutRetry()
    {
        using Fixture fixture = new();
        IState<AsyncState<string?>>? value = null;
        fixture.Composition.SetContent((context, _, _) => value = Composables.Builders.RememberAsync(1,
            _ => Task.FromResult<string?>(null), context));
        Assert.True(value!.Value.HasValue);
        Assert.Null(value.Value.Value);

        IState<AsyncState<int>>? stream = null;
        fixture.Composition.SetContent((context, _, _) => stream = Composables.Builders.CollectAsState(1, _ => Empty(), context));
        fixture.Context.Drain();
        Assert.True(stream!.Value.IsCompleted);
        Assert.False(stream.Value.HasValue);
        Assert.Throws<InvalidOperationException>(() => stream.Value.Value);

        Exception expected = new InvalidOperationException("fetch failed");
        int calls = 0;
        fixture.Composition.SetContent((context, _, _) => value = Composables.Builders.RememberAsync(1,
            _ => { calls++; return Task.FromException<string?>(expected); }, context));
        fixture.Context.Drain();
        Assert.Same(expected, value.Value.Error);
        Assert.True(value.Value.IsCompleted);
        Assert.Equal(1, calls);
        Assert.Empty(fixture.Errors);
    }

    private static async IAsyncEnumerable<int> Empty()
    {
        await Task.Yield();
        yield break;
    }

    [Fact]
    public void LifecycleEffectWaitsForCleanupAndDoesNotReplayWhileActive()
    {
        using Fixture fixture = new();
        Activity activity = new();
        TaskCompletionSource<object?> cleanup = new();
        List<string> events = new();
        fixture.Composition.SetContent((context, _, _) => Composables.Builders.LaunchedEffect(1, activity, async token =>
        {
            events.Add("start");
            try { await Task.Delay(Timeout.Infinite, token); }
            finally
            {
                events.Add("cleanup");
                await cleanup.Task;
                events.Add("cleaned");
            }
        }, context));
        activity.Set(false);
        fixture.Context.Drain();
        activity.Set(true);
        Assert.Equal(new[] { "start", "cleanup" }, events);
        cleanup.SetResult(null);
        fixture.Context.Drain();
        Assert.Equal(new[] { "start", "cleanup", "cleaned", "start" }, events);
        activity.Set(true);
        Assert.Equal(2, events.Count(item => item == "start"));
        fixture.Composition.SetContent((_, _, _) => { });
        fixture.Context.Drain();
        Assert.Equal(0, activity.Listeners);
    }

    [Fact]
    public void SequenceDisposalFinishesBeforeReactivationStartsNewEnumerator()
    {
        using Fixture fixture = new();
        Activity activity = new();
        TaskCompletionSource<object?> cleanup = new();
        List<string> events = new();
        IState<AsyncState<int>>? state = null;
        fixture.Composition.SetContent((context, _, _) => state = Composables.Builders.CollectAsState(1, activity,
            token => Sequence(token), context));
        Assert.Equal(7, state!.Value.Value);
        Assert.False(state.Value.IsLoading);
        activity.Set(false);
        fixture.Context.Drain();
        activity.Set(true);
        Assert.Equal(new[] { "start", "dispose" }, events);
        cleanup.SetResult(null);
        fixture.Context.Drain();
        Assert.Equal(new[] { "start", "dispose", "disposed", "start" }, events);

        async IAsyncEnumerable<int> Sequence([EnumeratorCancellation] CancellationToken token)
        {
            events.Add("start");
            try
            {
                yield return 7;
                await Task.Delay(Timeout.Infinite, token);
            }
            finally
            {
                events.Add("dispose");
                await cleanup.Task;
                events.Add("disposed");
            }
        }
    }

    [Fact]
    public void ActionSharesPendingTaskAndOnlyUsesAppliedHandlers()
    {
        using Fixture fixture = new();
        AsyncAction? action = null;
        TaskCompletionSource<object?> first = new();
        int calls = 0;
        fixture.Composition.SetContent((context, _, _) => action = Composables.Builders.RememberAsyncAction(
            _ => { calls++; return first.Task; }, context));
        Task running = action!.ExecuteAsync();
        Assert.Same(running, action.ExecuteAsync());
        action.Execute();
        Assert.Equal(1, calls);
        Assert.True(action.IsRunning);
        fixture.Composition.ComposeContent((context, _, _) => action = Composables.Builders.RememberAsyncAction(
            _ => throw new InvalidOperationException("discarded"), context));
        fixture.Composition.DiscardChanges();
        first.SetResult(null);
        fixture.Context.Drain();
        Assert.True(running.IsCompletedSuccessfully);
        fixture.Composition.SetContent((context, _, _) => action = Composables.Builders.RememberAsyncAction(
            _ => { calls += 10; return Task.CompletedTask; }, context));
        Assert.True(action.ExecuteAsync().IsCompletedSuccessfully);
        Assert.Equal(11, calls);
    }

    [Fact]
    public void ActionExposesSyncAsyncErrorsAndCancellationDoesNotReplay()
    {
        using Fixture fixture = new();
        Activity activity = new();
        AsyncAction? action = null;
        Exception expected = new InvalidOperationException("save failed");
        fixture.Composition.SetContent((context, _, _) => action = Composables.Builders.RememberAsyncAction(activity,
            _ => throw expected, context));
        Task failed = action!.ExecuteAsync();
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => failed.GetAwaiter().GetResult()));
        Assert.Same(expected, action.Error);
        fixture.Composition.SetContent((context, _, _) => action = Composables.Builders.RememberAsyncAction(activity,
            async _ => { await Task.Yield(); throw expected; }, context));
        action.Execute();
        fixture.Context.Drain();
        Assert.Same(expected, action.Error);
        int calls = 0;
        fixture.Composition.SetContent((context, _, _) => action = Composables.Builders.RememberAsyncAction(activity,
            token => { calls++; return Task.Delay(Timeout.Infinite, token); }, context));
        Task cancelled = action.ExecuteAsync();
        Assert.Null(action.Error);
        activity.Set(false);
        fixture.Context.Drain();
        Assert.True(cancelled.IsCanceled);
        activity.Set(true);
        Assert.Equal(1, calls);
        fixture.Composition.SetContent((_, _, _) => { });
        Assert.Equal(0, activity.Listeners);
        Assert.True(action.ExecuteAsync().IsCanceled);
    }

    [Fact]
    public void OwnerContextIsRestoredForEventCallsAndBackgroundResults()
    {
        using Fixture fixture = new();
        int thread = Environment.CurrentManagedThreadId;
        AsyncAction? action = null;
        fixture.Composition.SetContent((context, _, _) => action = Composables.Builders.RememberAsyncAction(async _ =>
        {
            Assert.Equal(thread, Environment.CurrentManagedThreadId);
            Assert.Same(fixture.Context, SynchronizationContext.Current);
            await Task.Run(() => 42);
            Assert.Equal(thread, Environment.CurrentManagedThreadId);
            Assert.Same(fixture.Context, SynchronizationContext.Current);
        }, context));
        SynchronizationContext.SetSynchronizationContext(null);
        Task execution;
        try { execution = action!.ExecuteAsync(); }
        finally { SynchronizationContext.SetSynchronizationContext(fixture.Context); }
        fixture.Context.Until(() => execution.IsCompleted);
        Assert.True(execution.IsCompletedSuccessfully, execution.Exception?.ToString());
    }

    [Fact]
    public void LegacyEffectReportsNonCancellationFailureAfterCancellation()
    {
        using Fixture fixture = new();
        TaskCompletionSource<object?> pending = new();
        Exception expected = new InvalidOperationException("late failure");
        fixture.Composition.SetContent((context, _, _) => Composables.Builders.LaunchedEffect(1,
            _ => new ValueTask(pending.Task), context));
        fixture.Composition.SetContent((_, _, _) => { });
        pending.SetException(expected);
        fixture.Context.Drain();
        Assert.Same(expected, Assert.Single(fixture.Errors));
    }

    [Fact]
    public void CombinedActivitySubscribesLazilyAndOnlyReportsEffectiveChanges()
    {
        Activity page = new();
        Activity window = new(false);
        ITaskActivity combined = TaskActivity.All(page, window);
        Assert.Equal(0, page.Listeners);
        int changed = 0;
        EventHandler handler = (_, _) => changed++;
        combined.Changed += handler;
        Assert.Equal(1, page.Listeners);
        page.Set(false);
        window.Set(true);
        Assert.Equal(0, changed);
        page.Set(true);
        Assert.Equal(1, changed);
        combined.Changed -= handler;
        Assert.Equal(0, page.Listeners);
        Assert.Equal(0, window.Listeners);
    }
}
