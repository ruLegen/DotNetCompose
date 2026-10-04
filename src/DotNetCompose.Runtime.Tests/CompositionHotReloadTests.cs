using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.Tests;

[Collection("Snapshots")]
public sealed class CompositionHotReloadTests
{
    [Fact]
    public void DeliversOnHostThreadWithCapturedExecutionContext()
    {
        Pump context = new();
        AsyncLocal<string> value = new() { Value = "registration" };
        int thread = Environment.CurrentManagedThreadId;
        int calls = 0;
        using IDisposable registration = CompositionHotReload.Register(context, () =>
        {
            Assert.Equal(thread, Environment.CurrentManagedThreadId);
            Assert.Same(context, SynchronizationContext.Current);
            Assert.Equal("registration", value.Value);
            value.Value = "callback";
            calls++;
        }, exception => throw new Xunit.Sdk.XunitException(exception.ToString()));
        value.Value = "caller";
        Thread worker = new(() => CompositionHotReload.UpdateApplication(null));
        worker.Start();
        worker.Join();
        Assert.Equal(0, calls);
        context.Drain();
        Assert.Equal(1, calls);
        Assert.Equal("caller", value.Value);
        CompositionHotReload.UpdateApplication(new[] { typeof(string) });
        context.Drain();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CoalescesPendingRequestsAndSchedulesRequestsDuringReload()
    {
        Pump context = new();
        int calls = 0;
        using IDisposable registration = CompositionHotReload.Register(context, () =>
        {
            if (++calls == 1)
            {
                CompositionHotReload.UpdateApplication(null);
                CompositionHotReload.UpdateApplication(null);
            }
        }, _ => { });
        Parallel.For(0, 20, _ => CompositionHotReload.UpdateApplication(null));
        Assert.Equal(1, context.Count);
        context.Step();
        Assert.Equal(1, calls);
        Assert.Equal(1, context.Count);
        context.Drain();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DisposeCancelsQueuedAndFutureCallbacks()
    {
        Pump context = new();
        int calls = 0;
        IDisposable registration = CompositionHotReload.Register(context, () => calls++, _ => calls++);
        CompositionHotReload.UpdateApplication(null);
        registration.Dispose();
        registration.Dispose();
        context.Drain();
        CompositionHotReload.UpdateApplication(null);
        context.Drain();
        Assert.Equal(0, calls);
    }

    [Fact]
    public void DisposeDuringReloadCancelsNextPass()
    {
        Pump context = new();
        int calls = 0;
        IDisposable? registration = null;
        registration = CompositionHotReload.Register(context, () =>
        {
            calls++;
            CompositionHotReload.UpdateApplication(null);
            registration!.Dispose();
        }, _ => { });
        CompositionHotReload.UpdateApplication(null);
        context.Drain();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ErrorsDoNotPreventOtherHostsOrSubsequentRetries()
    {
        Pump first = new();
        Pump second = new();
        Exception failure = new InvalidOperationException("reload");
        int attempts = 0;
        int errors = 0;
        int successes = 0;
        using IDisposable failing = CompositionHotReload.Register(first, () =>
        {
            if (++attempts == 1) throw failure;
        }, exception =>
        {
            Assert.Same(failure, exception);
            Assert.Same(first, SynchronizationContext.Current);
            errors++;
            throw new InvalidOperationException("error handler");
        });
        using IDisposable healthy = CompositionHotReload.Register(second, () => successes++, _ => { });
        CompositionHotReload.UpdateApplication(null);
        first.Drain();
        second.Drain();
        CompositionHotReload.UpdateApplication(Array.Empty<Type>());
        first.Drain();
        second.Drain();
        Assert.Equal(2, attempts);
        Assert.Equal(1, errors);
        Assert.Equal(2, successes);
    }

    [Fact]
    public void RejectedDispatchDoesNotPreventOtherHostsAndCanRetry()
    {
        Pump rejected = new() { Reject = true };
        Pump healthy = new();
        int calls = 0;
        using IDisposable first = CompositionHotReload.Register(rejected, () => calls++, _ => { });
        using IDisposable second = CompositionHotReload.Register(healthy, () => calls++, _ => { });
        CompositionHotReload.UpdateApplication(null);
        healthy.Drain();
        Assert.Equal(1, calls);
        rejected.Reject = false;
        CompositionHotReload.UpdateApplication(null);
        rejected.Drain();
        healthy.Drain();
        Assert.Equal(3, calls);
    }

    [Fact]
    public void RegistryDoesNotKeepUnownedHostsAlive()
    {
        WeakReference reference = RegisterUnownedHost();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
        CompositionHotReload.UpdateApplication(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterUnownedHost()
    {
        object host = new();
        _ = CompositionHotReload.Register(new Pump(), () => GC.KeepAlive(host), _ => { });
        return new WeakReference(host);
    }

    [Fact]
    public void RuntimeRegistersMetadataHandlerAndUsesRuntimeSupportFlag()
    {
        MetadataUpdateHandlerAttribute attribute = Assert.Single(typeof(CompositionHotReload).Assembly
            .GetCustomAttributes(typeof(MetadataUpdateHandlerAttribute), false).Cast<MetadataUpdateHandlerAttribute>());
        Assert.Equal(typeof(CompositionHotReload), attribute.HandlerType);
        Assert.Equal(MetadataUpdater.IsSupported, CompositionHotReload.IsSupported);
    }

    private sealed class Pump : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public bool Reject { get; set; }
        public int Count => _queue.Count;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (Reject) throw new InvalidOperationException("Rejected");
            _queue.Enqueue((callback, state));
        }
        public void Step()
        {
            Assert.True(_queue.TryDequeue(out var item));
            item.Callback(item.State);
        }
        public void Drain()
        {
            while (!_queue.IsEmpty) Step();
        }
    }
}
