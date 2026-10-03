using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace DotNetCompose.Maui;

/// <summary>Hosts a DotNetCompose composition inside an existing MAUI page.</summary>
public sealed partial class MauiComposeView : ContentView, IDisposable
{
    private readonly Grid _root = new();
    private MauiDispatcherSynchronizationContext? _context;
    private Recomposer? _recomposer;
    private Composition<MauiNode>? _composition;
    private ComposableAction? _content;
    private bool _disposed;

    public MauiComposeView()
    {
        Content = _root;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void SetContent(ComposableAction content)
    {
        ArgumentNullException.ThrowIfNull(content);
        VerifyAccess();
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MauiComposeView));
        }
        _content = content;
        if (IsLoaded)
        {
            MountOrUpdate();
        }
    }

    public void SetContent<TViewModel>(TViewModel viewModel, ComposableAction<TViewModel> screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        SetContent((context, changed, defaults) => screen(viewModel, context, changed, defaults));
    }

    private void OnLoaded(object? sender, EventArgs args)
    {
        MountOrUpdate();
    }

    private void OnUnloaded(object? sender, EventArgs args)
    {
        Unmount();
    }

    private void MountOrUpdate()
    {
        VerifyAccess();
        if (_disposed)
        {
            return;
        }

        if (_context == null)
        {
            _context = new MauiDispatcherSynchronizationContext(Dispatcher);
            _recomposer = new Recomposer(_context);
        }
        AttachHotReload();
        if (_content == null)
        {
            return;
        }
        RunWithContext(() =>
        {
            _composition ??= new Composition<MauiNode>(new MauiApplier(_root), _recomposer!);
            try
            {
                _composition.SetContent(_content);
            }
            catch (Exception exception)
            {
                Composition<MauiNode> failed = _composition;
                _composition = null;
                try
                {
                    failed.Dispose();
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(exception, cleanupException);
                }
                throw;
            }
        });
    }

    private void Unmount()
    {
        VerifyAccess();
        DetachHotReload();
        Composition<MauiNode>? composition = _composition;
        Recomposer? recomposer = _recomposer;
        _composition = null;
        _recomposer = null;
        try
        {
            RunWithContext(() =>
            {
                try
                {
                    composition?.Dispose();
                }
                finally
                {
                    recomposer?.Dispose();
                }
            });
        }
        finally
        {
            _context = null;
        }
    }

    private void RunWithContext(Action action)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_context);
        try
        {
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private void VerifyAccess()
    {
        if ((Handler is not null || _context is not null) && Dispatcher.IsDispatchRequired)
        {
            throw new InvalidOperationException("MauiComposeView must be used on its MAUI UI thread.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        VerifyAccess();
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        try
        {
            Unmount();
        }
        finally
        {
            _content = null;
        }
    }
}

internal sealed class MauiDispatcherSynchronizationContext(IDispatcher dispatcher) : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state)
    {
        if (!dispatcher.Dispatch(() =>
        {
            SynchronizationContext? previous = Current;
            SetSynchronizationContext(this);
            try
            {
                callback(state);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }))
        {
            throw new InvalidOperationException("The MAUI dispatcher rejected composition work.");
        }
    }
}
