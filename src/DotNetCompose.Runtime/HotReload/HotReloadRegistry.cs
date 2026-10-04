using System;
using System.Collections.Generic;
using System.Threading;

namespace DotNetCompose.Runtime.HotReload;

/// <summary>Tracks hosts without extending their lifetime.</summary>
internal sealed class HotReloadRegistry
{
    private readonly object _gate = new();
    private readonly List<WeakReference<HotReloadRegistration>> _registrations = new();

    public IDisposable Register(SynchronizationContext context, Action reload, Action<Exception> onError)
    {
        HotReloadRegistration registration = new(this, context, reload, onError);
        lock (_gate)
        {
            _registrations.RemoveAll(reference => !reference.TryGetTarget(out _));
            _registrations.Add(new WeakReference<HotReloadRegistration>(registration));
        }
        return registration;
    }

    public void Unregister(HotReloadRegistration registration)
    {
        lock (_gate)
        {
            _registrations.RemoveAll(reference =>
                !reference.TryGetTarget(out HotReloadRegistration? target) || ReferenceEquals(target, registration));
        }
    }

    public void NotifyHosts()
    {
        List<HotReloadRegistration> registrations = new();
        lock (_gate)
        {
            _registrations.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (WeakReference<HotReloadRegistration> reference in _registrations)
            {
                if (reference.TryGetTarget(out HotReloadRegistration? registration))
                {
                    registrations.Add(registration);
                }
            }
        }

        // A context may execute Post immediately, so never dispatch under the registry lock.
        foreach (HotReloadRegistration registration in registrations)
        {
            registration.RequestReload();
        }
    }
}
