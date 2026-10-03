using System;
using System.Threading;
using DotNetCompose.Runtime.HotReload;
#if NET9_0_OR_GREATER
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(DotNetCompose.Runtime.CompositionHotReload))]
#endif

namespace DotNetCompose.Runtime;

/// <summary>Delivers applied code updates to composition hosts.</summary>
public static class CompositionHotReload
{
    private static readonly HotReloadRegistry Registry = new();

    public static bool IsSupported =>
#if NET9_0_OR_GREATER
        MetadataUpdater.IsSupported;
#else
        false;
#endif

    /// <summary>Registers a host using the current execution context. Keep the token alive until the host is unloaded.</summary>
    /// <param name="context">The host's serial UI synchronization context.</param>
    /// <param name="reload">Recreates the host's composition after code updates have been applied.</param>
    /// <param name="onError">Receives reload failures on the same context.</param>
    /// <returns>A token whose disposal cancels pending and future reloads.</returns>
    public static IDisposable Register(SynchronizationContext context, Action reload, Action<Exception> onError)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        if (reload == null)
        {
            throw new ArgumentNullException(nameof(reload));
        }
        if (onError == null)
        {
            throw new ArgumentNullException(nameof(onError));
        }
        return Registry.Register(context, reload, onError);
    }

    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        Registry.NotifyHosts();
    }
}
