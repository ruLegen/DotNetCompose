using System;
using System.Threading;

namespace DotNetCompose.Runtime.HotReload;

/// <summary>Serializes reload requests for one host and restores its registration context.</summary>
internal sealed class HotReloadRegistration : IDisposable
{
    private readonly HotReloadRegistry _registry;
    private readonly object _gate = new();
    private SynchronizationContext? _context;
    private ExecutionContext? _executionContext;
    private Action? _reload;
    private Action<Exception>? _onError;
    private bool _queued;
    private bool _running;
    private bool _requested;
    private bool _disposed;

    public HotReloadRegistration(HotReloadRegistry registry, SynchronizationContext context,
        Action reload, Action<Exception> onError)
    {
        _registry = registry;
        _context = context;
        _executionContext = ExecutionContext.Capture();
        _reload = reload;
        _onError = onError;
    }

    public void RequestReload()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _requested = true;
            // Queued requests merge; requests during execution are handled by the next pass.
            if (_queued || _running)
            {
                return;
            }
            _queued = true;
        }
        PostReload();
    }

    private void PostReload()
    {
        try
        {
            SynchronizationContext? context;
            lock (_gate)
            {
                context = _context;
            }
            // Post may run inline. Call it outside the lock, like the host callbacks.
            context?.Post(_ => RunReload(), null);
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _queued = false;
            }
            // Delivery failed: there is no UI context on which to report this to the host.
            HotReloadDiagnostics.ReportError(exception);
        }
    }

    private void RunReload()
    {
        SynchronizationContext context;
        ExecutionContext? executionContext;
        Action reload;
        Action<Exception> onError;
        lock (_gate)
        {
            _queued = false;
            if (_disposed)
            {
                return;
            }
            // Consume the pending request before invoking the host, allowing it to request another pass.
            _running = true;
            _requested = false;
            context = _context!;
            executionContext = _executionContext;
            reload = _reload!;
            onError = _onError!;
        }
        try
        {
            if (executionContext == null)
            {
                InvokeReload(context, reload, onError);
            }
            else
            {
                ExecutionContext.Run(executionContext.CreateCopy(), _ => InvokeReload(context, reload, onError), null);
            }
        }
        catch (Exception exception)
        {
            HotReloadDiagnostics.ReportError(exception);
        }
        finally
        {
            bool post;
            lock (_gate)
            {
                _running = false;
                post = !_disposed && _requested;
                if (post)
                {
                    _queued = true;
                }
            }
            if (post)
            {
                PostReload();
            }
        }
    }

    private static void InvokeReload(SynchronizationContext context, Action reload, Action<Exception> onError)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            reload();
        }
        catch (Exception exception)
        {
            HotReloadDiagnostics.ReportError(exception);
            try
            {
                onError(exception);
            }
            catch (Exception handlerException)
            {
                HotReloadDiagnostics.ReportError(handlerException);
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            // Queued work will see the disposed flag; release references to the host immediately.
            _context = null;
            _executionContext = null;
            _reload = null;
            _onError = null;
        }
        _registry.Unregister(this);
    }
}
