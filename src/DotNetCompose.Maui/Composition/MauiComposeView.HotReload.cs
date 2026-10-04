using System.Diagnostics;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Maui;

public sealed partial class MauiComposeView
{
    private IDisposable? _hotReloadRegistration;

    public event EventHandler<HotReloadErrorEventArgs>? HotReloadFailed;

    /// <summary>Recreates the loaded UI, retaining the host and externally owned state.</summary>
    public void Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsLoaded)
        {
            return;
        }
        VerifyAccess();
        RunWithContext(() =>
        {
            Composition<MauiNode>? previous = _composition;
            _composition = null;
            previous?.Dispose();
        });
        MountOrUpdate();
    }

    private void AttachHotReload()
    {
        if (CompositionHotReload.IsSupported && _hotReloadRegistration == null)
        {
            _hotReloadRegistration = CompositionHotReload.Register(_context!, Reload, ReportHotReloadFailure);
        }
    }

    private void DetachHotReload()
    {
        _hotReloadRegistration?.Dispose();
        _hotReloadRegistration = null;
    }

    private void ReportHotReloadFailure(Exception exception)
    {
        HotReloadErrorEventArgs args = new(exception);
        if (HotReloadFailed == null)
        {
            return;
        }
        foreach (EventHandler<HotReloadErrorEventArgs> handler in HotReloadFailed.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception handlerException)
            {
                try
                {
                    Trace.TraceError("DotNetCompose Hot Reload handler: {0}", handlerException);
                }
                catch
                {
                    // A failing diagnostic listener must not interrupt other handlers.
                }
            }
        }
    }
}
