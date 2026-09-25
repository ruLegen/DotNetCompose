using System;
using System.Collections.Generic;
using System.Threading;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Composer
{
    public sealed class RecompositionErrorEventArgs : EventArgs
    {
        internal RecompositionErrorEventArgs(IControlledComposition composition, Exception exception)
        { Composition = composition; Exception = exception; }
        public IControlledComposition Composition { get; }
        public Exception Exception { get; }
    }

    /// <summary>Schedules sequential passes on a caller-supplied application context.</summary>
    public sealed class Recomposer : IDisposable
    {
        private readonly SynchronizationContext _context;
        private readonly int _thread = Environment.CurrentManagedThreadId;
        private readonly object _gate = new object();
        private readonly HashSet<IControlledComposition> _compositions = new HashSet<IControlledComposition>();
        private readonly ObserverHandle _writeObserver;
        private bool _scheduled;
        private bool _disposed;
        private bool _globalDirty;
        public event EventHandler<RecompositionErrorEventArgs>? Error;

        public Recomposer(SynchronizationContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            if (context.GetType() == typeof(SynchronizationContext))
                throw new ArgumentException("Supply a context with a sequential application dispatcher.", nameof(context));
            _writeObserver = Snapshot.RegisterGlobalWriteObserver(state =>
            {
                lock (_gate) _globalDirty = true;
                RequestWork();
            });
        }

        internal void VerifyAccess()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Recomposer));
            if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Use the owning application context.");
        }

        internal void Register(IControlledComposition composition) { VerifyAccess(); _compositions.Add(composition); }
        internal void Unregister(IControlledComposition composition) => _compositions.Remove(composition);
        internal void RequestWork()
        {
            lock (_gate)
            {
                if (_disposed || _scheduled) return;
                _scheduled = true;
            }
            _context.Post(ignored => Run(), null);
        }

        internal void ReportEffectError(IControlledComposition composition, Exception exception)
        {
            if (exception is AggregateException aggregate)
                exception = aggregate.Flatten().InnerExceptions.Count == 1
                    ? aggregate.Flatten().InnerExceptions[0]
                    : aggregate.Flatten();
            Exception reported = exception;
            _context.Post(_ =>
            {
                if (_disposed || composition.IsDisposed) return;
                if (Error == null) throw reported;
                Error(this, new RecompositionErrorEventArgs(composition, reported));
            }, null);
        }

        private void Run()
        {
            lock (_gate) { if (_disposed) return; _globalDirty = false; }
            VerifyAccess();
            HashSet<IControlledComposition> failed = new HashSet<IControlledComposition>();
            try
            {
                Snapshot.SendApplyNotifications();
                IControlledComposition[] compositions = new IControlledComposition[_compositions.Count];
                _compositions.CopyTo(compositions);
                foreach (IControlledComposition composition in compositions)
                {
                    if (composition.IsDisposed || composition.HasPendingChanges || !composition.HasInvalidations) continue;
                    try { if (composition.Recompose()) composition.ApplyChanges(); }
                    catch (Exception error)
                    {
                        failed.Add(composition);
                        if (Error == null) throw;
                        Error(this, new RecompositionErrorEventArgs(composition, error));
                    }
                }
            }
            finally { lock (_gate) _scheduled = false; }
            bool dirty;
            lock (_gate) dirty = _globalDirty;
            if (dirty) RequestWork();
            // Requests arriving during this pass are retained by each composition.
            foreach (IControlledComposition composition in _compositions)
                if (!failed.Contains(composition) && !composition.IsDisposed && !composition.HasPendingChanges && composition.HasInvalidations)
                { RequestWork(); break; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            VerifyAccess();
            lock (_gate) _disposed = true;
            _writeObserver.Dispose();
            _compositions.Clear();
        }
    }
}
