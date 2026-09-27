using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime.Diagnostics
{
    public static class CompositionDiagnosticsExtensions
    {
        public static CompositionDiagnosticsSession StartDiagnostics<TNode>(
            this Composition<TNode> composition,
            CompositionDiagnosticsOptions? options = null)
            where TNode : class
        {
            if (composition == null)
                throw new ArgumentNullException(nameof(composition));
            return CompositionDiagnosticsRuntime.StartSession(composition, options);
        }
    }

    [DebuggerDisplay("Flags = {Options.Flags}, Disposed = {IsDisposed}")]
    [DebuggerTypeProxy(typeof(CompositionDiagnosticsSessionDebugView))]
    public sealed class CompositionDiagnosticsSession : IDisposable
    {
        private readonly CompositionDiagnosticsDispatcher _dispatcher;
        private readonly IDisposable _flagsRegistration;
        private int _disposed;

        internal CompositionDiagnosticsSession(
            CompositionDiagnosticsDispatcher dispatcher,
            CompositionDiagnosticsOptions options)
        {
            _dispatcher = dispatcher;
            Options = options;
            _flagsRegistration = CompositionDiagnosticsRuntime.RegisterSessionFlags(options.Flags);
            dispatcher.RegisterSession(this);
        }

        public CompositionDiagnosticsOptions Options { get; }
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public event EventHandler<CompositionDiagnosticsErrorEventArgs>? ObserverError;

        public IDisposable Subscribe(ICompositionObserver observer)
        {
            if (observer == null)
                throw new ArgumentNullException(nameof(observer));
            ThrowIfDisposed();
            return _dispatcher.Subscribe(this, observer);
        }

        public CompositionDiagnosticsSnapshot CaptureSnapshot()
        {
            ThrowIfDisposed();
            return _dispatcher.CaptureSnapshot();
        }

        internal void ReportObserverError(ICompositionObserver observer, Exception error) =>
            ObserverError?.Invoke(this, new CompositionDiagnosticsErrorEventArgs(observer, error));

        internal bool Accepts(CompositionDiagnosticsFlags category) =>
            (Options.Flags & category) != 0;

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(CompositionDiagnosticsSession));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _dispatcher.UnregisterSession(this);
            _flagsRegistration.Dispose();
        }
    }

    internal sealed class CompositionDiagnosticsSessionDebugView
    {
        private readonly CompositionDiagnosticsSession _session;
        internal CompositionDiagnosticsSessionDebugView(CompositionDiagnosticsSession session) => _session = session;
        public CompositionDiagnosticsFlags Flags => _session.Options.Flags;
        public bool IsDisposed => _session.IsDisposed;
        public CompositionDiagnosticsSnapshot? Snapshot =>
            _session.IsDisposed ? null : _session.CaptureSnapshot();
    }

    internal sealed class CompositionDiagnosticsDispatcher
    {
        private readonly object _gate = new object();
        private readonly List<CompositionDiagnosticsSession> _sessions = new List<CompositionDiagnosticsSession>();
        private readonly List<ObserverRegistration> _observers = new List<ObserverRegistration>();
        private CompositionDiagnosticsSnapshot _snapshot =
            CompositionDiagnosticsSnapshot.Unavailable("No completed diagnostic composition pass is available.");
        private CompositionDiagnosticsFlags _enabledFlags;
        private bool _disposed;

        internal CompositionDiagnosticsFlags EnabledFlags
        {
            get
            {
                lock (_gate)
                    return _enabledFlags;
            }
        }

        internal void RegisterSession(CompositionDiagnosticsSession session)
        {
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(CompositionDiagnosticsDispatcher));
                _sessions.Add(session);
                RecomputeEnabledFlags();
            }
        }

        internal void UnregisterSession(CompositionDiagnosticsSession session)
        {
            lock (_gate)
            {
                _sessions.Remove(session);
                _observers.RemoveAll(item => ReferenceEquals(item.Session, session));
                RecomputeEnabledFlags();
            }
        }

        private void RecomputeEnabledFlags()
        {
            CompositionDiagnosticsFlags flags = CompositionDiagnosticsFlags.None;
            foreach (CompositionDiagnosticsSession session in _sessions)
                if (!session.IsDisposed)
                    flags |= session.Options.Flags;
            _enabledFlags = flags;
        }

        internal IDisposable Subscribe(CompositionDiagnosticsSession session, ICompositionObserver observer)
        {
            ObserverRegistration registration = new ObserverRegistration(this, session, observer);
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(CompositionDiagnosticsDispatcher));
                _observers.Add(registration);
            }
            return registration;
        }

        internal void Publish(CompositionDiagnosticEvent diagnosticEvent)
        {
            ObserverRegistration[] observers;
            lock (_gate)
            {
                if (_disposed || _observers.Count == 0)
                    return;
                observers = _observers.ToArray();
            }

            foreach (ObserverRegistration registration in observers)
            {
                if (registration.IsDisposed || !registration.Session.Accepts(diagnosticEvent.Category))
                    continue;
                try
                {
                    registration.Observer.OnEvent(diagnosticEvent);
                }
                catch (Exception error)
                {
                    registration.Dispose();
                    registration.Session.ReportObserverError(registration.Observer, error);
                }
            }
        }

        internal void ReplaceSnapshot(CompositionDiagnosticsSnapshot snapshot)
        {
            lock (_gate)
                if (!_disposed)
                    _snapshot = snapshot;
        }

        internal CompositionDiagnosticsSnapshot CaptureSnapshot()
        {
            lock (_gate)
                return _snapshot;
        }

        internal void Dispose()
        {
            CompositionDiagnosticsSession[] sessions;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                sessions = _sessions.ToArray();
                _sessions.Clear();
                _observers.Clear();
                _enabledFlags = CompositionDiagnosticsFlags.None;
                _snapshot = CompositionDiagnosticsSnapshot.Unavailable("The composition has been disposed.");
            }
            foreach (CompositionDiagnosticsSession session in sessions)
                session.Dispose();
        }

        private void Remove(ObserverRegistration registration)
        {
            lock (_gate)
                _observers.Remove(registration);
        }

        private sealed class ObserverRegistration : IDisposable
        {
            private readonly CompositionDiagnosticsDispatcher _owner;
            private int _disposed;

            internal ObserverRegistration(
                CompositionDiagnosticsDispatcher owner,
                CompositionDiagnosticsSession session,
                ICompositionObserver observer)
            {
                _owner = owner;
                Session = session;
                Observer = observer;
            }

            internal CompositionDiagnosticsSession Session { get; }
            internal ICompositionObserver Observer { get; }
            internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    _owner.Remove(this);
            }
        }
    }
}
